using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Api.Data;
using OnlineShop.Api.Domain;
using OnlineShop.Api.Services;

namespace OnlineShop.Api.Controllers;

[ApiController]
[Route("api/webhooks/mercadopago")]
public sealed class MercadoPagoWebhookController : ControllerBase
{
    private readonly OnlineShopDbContext _db;
    private readonly MercadoPagoClient _mp;

    public MercadoPagoWebhookController(OnlineShopDbContext db, MercadoPagoClient mp)
    {
        _db = db;
        _mp = mp;
    }

    [HttpPost]
    public async Task<IActionResult> Handle(CancellationToken ct)
    {
        // MP manda muchas variantes: query data.id / id / etc.
        var paymentId =
            Request.Query["data.id"].FirstOrDefault()
            ?? Request.Query["id"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(paymentId))
            return Ok();

        if (!_mp.IsConfigured)
            return Ok();

        // Verifica con API (evita spoof; webhook puede duplicarse)
        var (status, externalRef, pid) = await _mp.GetPaymentAsync(paymentId, ct);

        if (!Guid.TryParse(externalRef, out var orderId))
            return Ok();

        var order = await _db.Orders
            .Include(o => o.Payments)
            .SingleOrDefaultAsync(o => o.Id == orderId, ct);

        if (order is null) return Ok();

        // Idempotencia
        if (order.Status == OrderStatus.Paid)
            return Ok();

        if (string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase))
        {
            var now = DateTime.UtcNow;

            order.Status = OrderStatus.Paid;
            order.PaidAt = now;
            order.Provider = "mercadopago";
            order.ProviderPaymentId = pid;

            var last = order.Payments.OrderByDescending(p => p.CreatedAt).FirstOrDefault()
                       ?? new PaymentAttempt { Id = Guid.NewGuid(), OrderId = order.Id, Provider = "mercadopago", CreatedAt = now, UpdatedAt = now, Currency = order.Currency };

            if (!order.Payments.Contains(last))
                order.Payments.Add(last);

            last.Provider = "mercadopago";
            last.ProviderPaymentId = pid;
            last.Status = PaymentStatus.Succeeded;
            last.Amount = order.Total;
            last.Currency = order.Currency;
            last.UpdatedAt = now;

            // cierra carrito pendiente (si existe)
            var cart = await _db.Carts
                .SingleOrDefaultAsync(c => c.StoreId == order.StoreId && c.Status == CartStatus.CheckoutPending &&
                    (order.UserId != null ? c.UserId == order.UserId : c.GuestId == order.GuestId), ct);

            if (cart != null)
            {
                cart.Status = CartStatus.Completed;
                cart.UpdatedAt = now;
            }

            order.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);
        }

        return Ok();
    }
}