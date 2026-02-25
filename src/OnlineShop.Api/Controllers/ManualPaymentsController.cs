using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Api.Data;
using OnlineShop.Api.Domain;

namespace OnlineShop.Api.Controllers;

[ApiController]
[Route("api/admin/payments")]
[Authorize(Roles = "MasterAdmin,StoreOwner,Staff")]
public sealed class ManualPaymentsController : ControllerBase
{
    private readonly OnlineShopDbContext _db;

    public ManualPaymentsController(OnlineShopDbContext db) => _db = db;

    public sealed record ConfirmManualPaymentRequest(
        Guid OrderId,
        string? ProviderPaymentId = null,
        string? RawJson = null
    );

    public sealed record ConfirmManualPaymentResponse(
        Guid OrderId,
        int OrderStatus,
        DateTime? PaidAt,
        Guid PaymentAttemptId,
        int PaymentStatus,
        string Provider,
        string ProviderPaymentId
    );

    /// <summary>
    /// Confirma un pago MANUAL (admin). Idempotente.
    /// POST /api/admin/payments/manual/confirm
    /// Body: { "orderId": "GUID", "providerPaymentId": "opcional", "rawJson": "opcional" }
    /// </summary>
    [HttpPost("manual/confirm")]
    public async Task<IActionResult> ConfirmManual([FromBody] ConfirmManualPaymentRequest req, CancellationToken ct)
    {
        if (req.OrderId == Guid.Empty)
            return BadRequest(new { error = "OrderId inválido." });

        var strategy = _db.Database.CreateExecutionStrategy();

        ConfirmManualPaymentResponse? response = null;

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var order = await _db.Orders
                .SingleOrDefaultAsync(o => o.Id == req.OrderId, ct);

            if (order is null)
                throw new HttpRequestException("Order no encontrada.", null, System.Net.HttpStatusCode.NotFound);

            var provider = "manual";

            // Idempotencia: ProviderPaymentId estable
            var providerPaymentId = !string.IsNullOrWhiteSpace(req.ProviderPaymentId)
                ? req.ProviderPaymentId!.Trim()
                : (!string.IsNullOrWhiteSpace(order.ProviderPaymentId)
                    ? order.ProviderPaymentId!.Trim()
                    : $"manual-{order.Id}");

            var now = DateTime.UtcNow;

            // Busca attempt por providerPaymentId (o crea si falta)
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

            // Si ya estaba succeeded, es idempotente
            if (attempt.Status != PaymentStatus.Succeeded)
            {
                attempt.Status = PaymentStatus.Succeeded;
                attempt.Amount = order.Total;
                attempt.Currency = order.Currency;
                attempt.RawJson = string.IsNullOrWhiteSpace(req.RawJson) ? attempt.RawJson : req.RawJson;
                attempt.UpdatedAt = now;
            }

            // Marca orden como pagada (idempotente)
            // Asumimos: 1 = Paid
            if ((int)order.Status != 1)
                order.Status = (OrderStatus)1;

            order.Provider = provider;
            order.ProviderPaymentId = providerPaymentId;
            order.PaidAt ??= now;
            order.UpdatedAt = now;

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            response = new ConfirmManualPaymentResponse(
                OrderId: order.Id,
                OrderStatus: (int)order.Status,
                PaidAt: order.PaidAt,
                PaymentAttemptId: attempt.Id,
                PaymentStatus: (int)attempt.Status,
                Provider: provider,
                ProviderPaymentId: providerPaymentId
            );
        });

        return Ok(response);
    }
}