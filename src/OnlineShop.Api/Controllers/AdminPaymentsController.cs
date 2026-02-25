using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Api.Data;
using OnlineShop.Api.Domain;

namespace OnlineShop.Api.Controllers;

[ApiController]
[Route("api/admin/payments")]
[Authorize(Roles = "MasterAdmin,StoreOwner,Staff")]
public sealed class AdminPaymentsController : ControllerBase
{
    private readonly OnlineShopDbContext _db;

    public AdminPaymentsController(OnlineShopDbContext db) => _db = db;

    public sealed record CancelManualPaymentRequest(
        Guid OrderId,
        string? ProviderPaymentId = null,
        string? RawJson = null
    );

    public sealed record CancelManualPaymentResponse(
        Guid OrderId,
        int OrderStatus,
        DateTime? PaidAt,
        Guid PaymentAttemptId,
        int PaymentStatus,
        string Provider,
        string ProviderPaymentId
    );

    public sealed record PaymentStatusResponse(
        Guid OrderId,
        int OrderStatus,
        DateTime? PaidAt,
        Guid? PaymentAttemptId,
        int? PaymentStatus,
        string? Provider,
        string? ProviderPaymentId
    );

    /// <summary>
    /// Cancela un pago MANUAL (admin). Idempotente.
    /// POST /api/admin/payments/manual/cancel
    /// Body: { "orderId": "GUID", "providerPaymentId": "opcional", "rawJson": "opcional" }
    /// Regla: si Order ya está Paid (1), regresa 409.
    /// </summary>
    [HttpPost("manual/cancel")]
    public async Task<IActionResult> CancelManual([FromBody] CancelManualPaymentRequest req, CancellationToken ct)
    {
        if (req.OrderId == Guid.Empty)
            return BadRequest(new { error = "OrderId inválido." });

        // ✅ Chequeo “barato” primero, para devolver 409 sin meternos al ExecutionStrategy/Tx.
        var currentStatus = await _db.Orders
            .AsNoTracking()
            .Where(o => o.Id == req.OrderId)
            .Select(o => (int)o.Status)
            .SingleOrDefaultAsync(ct);

        if (currentStatus == 0)
        {
            // SingleOrDefaultAsync para int regresa 0 si no encontró; pero 0 puede ser Pending.
            // Entonces confirmamos existencia real:
            var exists = await _db.Orders.AsNoTracking().AnyAsync(o => o.Id == req.OrderId, ct);
            if (!exists)
                return NotFound(new { error = "Order no encontrada." });
        }

        // Asumimos: 1 = Paid
        if (currentStatus == 1)
            return Conflict(new { error = "Order ya está pagada; no se puede cancelar." });

        var strategy = _db.Database.CreateExecutionStrategy();

        CancelManualPaymentResponse? response = null;

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var order = await _db.Orders
                .SingleOrDefaultAsync(o => o.Id == req.OrderId, ct);

            if (order is null)
                return; // ya validamos arriba, pero por seguridad

            // Revalida dentro de TX por concurrencia
            if ((int)order.Status == 1)
                return; // luego afuera devolvemos 409

            var provider = "manual";

            var providerPaymentId = !string.IsNullOrWhiteSpace(req.ProviderPaymentId)
                ? req.ProviderPaymentId!.Trim()
                : (!string.IsNullOrWhiteSpace(order.ProviderPaymentId)
                    ? order.ProviderPaymentId!.Trim()
                    : $"manual-{order.Id}");

            var now = DateTime.UtcNow;

            var attempt = await _db.PaymentAttempts
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync(p =>
                    p.OrderId == order.Id &&
                    p.Provider == provider &&
                    p.ProviderPaymentId == providerPaymentId, ct);

            if (attempt is null)
            {
                attempt = new PaymentAttempt
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    Provider = provider,
                    ProviderPaymentId = providerPaymentId,
                    ProviderSessionId = null,
                    Status = PaymentStatus.Pending,
                    Amount = order.Total,
                    Currency = order.Currency,
                    RawJson = null,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _db.PaymentAttempts.Add(attempt);
            }

            // Idempotencia: si ya estaba Failed, no cambia nada (solo puede setear RawJson si llega)
            if (attempt.Status != PaymentStatus.Failed)
            {
                attempt.Status = PaymentStatus.Failed;
                attempt.Amount = order.Total;
                attempt.Currency = order.Currency;
                attempt.RawJson = string.IsNullOrWhiteSpace(req.RawJson) ? attempt.RawJson : req.RawJson;
                attempt.UpdatedAt = now;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(attempt.RawJson) && !string.IsNullOrWhiteSpace(req.RawJson))
                {
                    attempt.RawJson = req.RawJson;
                    attempt.UpdatedAt = now;
                }
            }

            // Marca orden como cancelada (idempotente)
            // Asumimos: 2 = Cancelled
            if ((int)order.Status != 2)
                order.Status = (OrderStatus)2;

            order.Provider = provider;
            order.ProviderPaymentId = providerPaymentId;
            order.UpdatedAt = now;

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            response = new CancelManualPaymentResponse(
                OrderId: order.Id,
                OrderStatus: (int)order.Status,
                PaidAt: order.PaidAt,
                PaymentAttemptId: attempt.Id,
                PaymentStatus: (int)attempt.Status,
                Provider: provider,
                ProviderPaymentId: providerPaymentId
            );
        });

        // Si revalidó y vio Paid dentro de TX, response queda null → 409
        if (response is null)
            return Conflict(new { error = "Order ya está pagada; no se puede cancelar." });

        return Ok(response);
    }

    /// <summary>
    /// Consulta estado por orderId o providerPaymentId.
    /// GET /api/admin/payments/status?orderId=...
    /// GET /api/admin/payments/status?providerPaymentId=...
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status([FromQuery] Guid? orderId, [FromQuery] string? providerPaymentId, CancellationToken ct)
    {
        if (orderId is null && string.IsNullOrWhiteSpace(providerPaymentId))
            return BadRequest(new { error = "Debes enviar orderId o providerPaymentId." });

        if (!string.IsNullOrWhiteSpace(providerPaymentId))
        {
            var attemptByPid = await _db.PaymentAttempts
                .AsNoTracking()
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync(p => p.ProviderPaymentId == providerPaymentId.Trim(), ct);

            if (attemptByPid is null)
                return NotFound(new { error = "PaymentAttempt no encontrado." });

            var orderByPid = await _db.Orders
                .AsNoTracking()
                .SingleOrDefaultAsync(o => o.Id == attemptByPid.OrderId, ct);

            if (orderByPid is null)
                return NotFound(new { error = "Order no encontrada para ese paymentAttempt." });

            return Ok(new PaymentStatusResponse(
                OrderId: orderByPid.Id,
                OrderStatus: (int)orderByPid.Status,
                PaidAt: orderByPid.PaidAt,
                PaymentAttemptId: attemptByPid.Id,
                PaymentStatus: (int)attemptByPid.Status,
                Provider: attemptByPid.Provider,
                ProviderPaymentId: attemptByPid.ProviderPaymentId
            ));
        }

        var order = await _db.Orders
            .AsNoTracking()
            .SingleOrDefaultAsync(o => o.Id == orderId!.Value, ct);

        if (order is null)
            return NotFound(new { error = "Order no encontrada." });

        var attempt = await _db.PaymentAttempts
            .AsNoTracking()
            .Where(p => p.OrderId == order.Id)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (attempt is null)
        {
            return Ok(new PaymentStatusResponse(
                OrderId: order.Id,
                OrderStatus: (int)order.Status,
                PaidAt: order.PaidAt,
                PaymentAttemptId: null,
                PaymentStatus: null,
                Provider: order.Provider,
                ProviderPaymentId: order.ProviderPaymentId
            ));
        }

        return Ok(new PaymentStatusResponse(
            OrderId: order.Id,
            OrderStatus: (int)order.Status,
            PaidAt: order.PaidAt,
            PaymentAttemptId: attempt.Id,
            PaymentStatus: (int)attempt.Status,
            Provider: attempt.Provider,
            ProviderPaymentId: attempt.ProviderPaymentId
        ));
    }
}