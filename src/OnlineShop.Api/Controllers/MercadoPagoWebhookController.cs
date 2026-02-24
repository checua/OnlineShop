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
        // MP suele mandar data.id en query (webhook v2)
        var paymentId =
            Request.Query["data.id"].FirstOrDefault()
            ?? Request.Query["id"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(paymentId))
            return Ok();

        if (!_mp.IsConfigured)
            return Ok();

        var (status, externalRef, pid) = await _mp.GetPaymentAsync(paymentId, ct);

        if (!Guid.TryParse(externalRef, out var orderId))
            return Ok();

        var order = await _db.Orders
            .Include(o => o.Payments)
            .SingleOrDefaultAsync(o => o.Id == orderId, ct);

        if (order is null)
            return Ok();

        // idempotencia
        if (order.Status == OrderStatus.Paid)
            return Ok();

        // Solo aprobados marcan Paid
        if (!string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase))
            return Ok();

        var now = DateTime.UtcNow;

        order.Status = OrderStatus.Paid;
        order.PaidAt = now;
        order.Provider = "mercadopago";
        order.ProviderPaymentId = pid;
        order.UpdatedAt = now;

        // PaymentAttempt: marca último pending como succeeded o crea uno si no existe
        var pay = order.Payments
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefault(p => p.Provider == "mercadopago")
            ?? new PaymentAttempt
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Provider = "mercadopago",
                ProviderPaymentId = $"mp:{pid}",
                ProviderSessionId = order.ProviderSessionId,
                Status = PaymentStatus.Pending,
                Amount = order.Total,
                Currency = order.Currency,
                CreatedAt = now,
                UpdatedAt = now
            };

        if (!order.Payments.Contains(pay))
            order.Payments.Add(pay);

        pay.ProviderPaymentId = $"mp:{pid}";
        pay.Status = PaymentStatus.Succeeded;
        pay.Amount = order.Total;
        pay.Currency = order.Currency;
        pay.UpdatedAt = now;

        // cierra carrito CheckoutPending
        var cart = await _db.Carts
            .SingleOrDefaultAsync(c =>
                c.StoreId == order.StoreId &&
                c.Status == CartStatus.CheckoutPending &&
                (order.UserId != null ? c.UserId == order.UserId : c.GuestId == order.GuestId), ct);

        if (cart != null)
        {
            cart.Status = CartStatus.Completed;
            cart.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct);
        return Ok();
    }
}