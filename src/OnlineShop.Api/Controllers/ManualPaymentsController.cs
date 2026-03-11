// src/OnlineShop.Api/Controllers/ManualPaymentsController.cs
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
    [Authorize(Roles = "MasterAdmin")]
    public async Task<IActionResult> ConfirmManual([FromBody] ConfirmManualPaymentRequest req, CancellationToken ct)
    {
        if (req is null || req.OrderId == Guid.Empty)
            return BadRequest(new { error = "OrderId inválido." });

        var strategy = _db.Database.CreateExecutionStrategy();

        ConfirmManualPaymentResponse? response = null;
        string? conflict = null;

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var now = DateTime.UtcNow;

            var order = await _db.Orders
                .SingleOrDefaultAsync(o => o.Id == req.OrderId, ct);

            if (order is null)
                return;

            const string provider = "manual";

            // Idempotencia: ProviderPaymentId estable
            var providerPaymentId = !string.IsNullOrWhiteSpace(req.ProviderPaymentId)
                ? req.ProviderPaymentId!.Trim()
                : (!string.IsNullOrWhiteSpace(order.ProviderPaymentId)
                    ? order.ProviderPaymentId!.Trim()
                    : $"manual-{order.Id}");

            // Busca attempt por provider + providerPaymentId
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
                await _db.SaveChangesAsync(ct);
            }

            // ===== Guard: monto/moneda deben coincidir con la orden (evita marcar pagada algo raro)
            if (attempt.Currency != order.Currency || attempt.Amount != order.Total)
            {
                conflict = $"Monto/moneda no coincide. Attempt={attempt.Amount} {attempt.Currency} vs Order={order.Total} {order.Currency}.";
                await tx.CommitAsync(ct);
                return;
            }

            // ===== Idempotencia: si ya está pagada + succeeded => OK sin tocar nada
            if (order.PaidAt is not null && order.Status == OrderStatus.Paid && attempt.Status == PaymentStatus.Succeeded)
            {
                response = new ConfirmManualPaymentResponse(
                    OrderId: order.Id,
                    OrderStatus: (int)order.Status,
                    PaidAt: order.PaidAt,
                    PaymentAttemptId: attempt.Id,
                    PaymentStatus: (int)attempt.Status,
                    Provider: provider,
                    ProviderPaymentId: providerPaymentId
                );

                await tx.CommitAsync(ct);
                return;
            }

            // ===== Marcar attempt succeeded
            attempt.Status = PaymentStatus.Succeeded;
            attempt.Amount = order.Total;
            attempt.Currency = order.Currency;
            attempt.RawJson = string.IsNullOrWhiteSpace(req.RawJson) ? attempt.RawJson : req.RawJson;
            attempt.UpdatedAt = now;

            // ===== Marcar orden pagada
            order.Status = OrderStatus.Paid;
            order.Provider = provider;
            order.ProviderPaymentId = providerPaymentId;
            order.PaidAt ??= now;
            order.UpdatedAt = now;

            // ===== Cerrar carrito CheckoutPending del actor (si existe)
            await CloseCheckoutPendingCartAsync(order, now, ct);

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

        if (response is null && conflict is null)
            return NotFound(new { error = "Order no encontrada." });

        if (conflict is not null)
            return Conflict(new { error = conflict });

        return Ok(response);
    }

    private async Task CloseCheckoutPendingCartAsync(Order order, DateTime now, CancellationToken ct)
    {
        // Cierra el carrito CheckoutPending para el mismo Store + mismo actor (UserId o GuestId)
        var q = _db.Carts
            .Where(c => c.StoreId == order.StoreId && c.Status == CartStatus.CheckoutPending);

        if (!string.IsNullOrWhiteSpace(order.UserId))
            q = q.Where(c => c.UserId == order.UserId);
        else
            q = q.Where(c => c.GuestId == order.GuestId);

        var carts = await q.ToListAsync(ct);

        foreach (var c in carts)
        {
            c.Status = CartStatus.Completed;
            c.UpdatedAt = now;
        }
    }
}