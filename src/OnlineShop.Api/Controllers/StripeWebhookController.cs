// src/OnlineShop.Api/Controllers/StripeWebhookController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShop.Api.Data;
using OnlineShop.Api.Domain;
using OnlineShop.Api.Options;
using Stripe;
using Stripe.Checkout;

namespace OnlineShop.Api.Controllers;

[ApiController]
[Route("api/webhooks/stripe")]
public sealed class StripeWebhookController : ControllerBase
{
    private readonly OnlineShopDbContext _db;
    private readonly StripeOptions _stripe;

    public StripeWebhookController(OnlineShopDbContext db, IOptions<StripeOptions> stripe)
    {
        _db = db;
        _stripe = stripe.Value;
    }

    [HttpPost]
    public async Task<IActionResult> Handle(CancellationToken ct)
    {
        // Si no está configurado, NO aceptes webhooks (mejor fallar fuerte)
        if (string.IsNullOrWhiteSpace(_stripe.WebhookSecret))
            return StatusCode(500, new { error = "Stripe webhook no configurado (WebhookSecret vacío)." });

        var sigHeader = Request.Headers["Stripe-Signature"].ToString();
        if (string.IsNullOrWhiteSpace(sigHeader))
            return Unauthorized(); // sin firma

        var json = await new StreamReader(Request.Body).ReadToEndAsync(ct);

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(json, sigHeader, _stripe.WebhookSecret);
        }
        catch
        {
            return Unauthorized(); // firma inválida
        }

        // checkout.session.completed
        if (stripeEvent.Type == EventTypes.CheckoutSessionCompleted)
        {
            var session = stripeEvent.Data.Object as Session;
            if (session?.Id is null) return Ok();

            var orderIdStr =
                session.ClientReferenceId
                ?? (session.Metadata != null && session.Metadata.TryGetValue("orderId", out var oid) ? oid : null);

            if (!Guid.TryParse(orderIdStr, out var orderId))
                return Ok();

            var order = await _db.Orders
                .Include(o => o.Payments)
                .SingleOrDefaultAsync(o => o.Id == orderId, ct);

            if (order is null) return Ok();

            // idempotencia
            if (order.Status == OrderStatus.Paid)
                return Ok();

            var now = DateTime.UtcNow;

            order.Status = OrderStatus.Paid;
            order.PaidAt = now;
            order.Provider = "stripe";
            order.ProviderSessionId = session.Id;
            order.ProviderPaymentId = session.PaymentIntentId;

            // Marca intento como success (idempotente)
            var pay =
                order.Payments
                    .OrderByDescending(p => p.CreatedAt)
                    .FirstOrDefault(p => p.ProviderSessionId == session.Id)
                ?? order.Payments
                    .OrderByDescending(p => p.CreatedAt)
                    .FirstOrDefault();

            if (pay != null)
            {
                pay.Status = PaymentStatus.Succeeded;
                pay.ProviderPaymentId = session.PaymentIntentId ?? pay.ProviderPaymentId;
                pay.UpdatedAt = now;
            }

            // Cierra carrito (si existe)
            var cart = await _db.Carts
                .Where(c => c.StoreId == order.StoreId && c.Status == CartStatus.CheckoutPending)
                .Where(c => order.UserId != null ? c.UserId == order.UserId : c.GuestId == order.GuestId)
                .SingleOrDefaultAsync(ct);

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
