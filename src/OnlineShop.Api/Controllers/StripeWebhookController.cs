// src/OnlineShop.Api/Controllers/StripeWebhookController.cs
using System.IO;
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

        if (!string.IsNullOrWhiteSpace(_stripe.SecretKey))
            StripeConfiguration.ApiKey = _stripe.SecretKey;
    }

    [HttpPost]
    public async Task<IActionResult> Handle(CancellationToken ct)
    {
        // 1) Leer raw body (Stripe firma el raw string)
        var json = await new StreamReader(Request.Body).ReadToEndAsync(ct);

        // 2) Construir evento (con firma si existe WebhookSecret)
        Event stripeEvent;
        try
        {
            var sigHeader = Request.Headers["Stripe-Signature"].ToString();

            if (!string.IsNullOrWhiteSpace(_stripe.WebhookSecret))
            {
                stripeEvent = EventUtility.ConstructEvent(
                    json,
                    sigHeader,
                    _stripe.WebhookSecret
                );
            }
            else
            {
                // Dev fallback (NO recomendado en prod)
                stripeEvent = EventUtility.ParseEvent(json);
            }
        }
        catch (Exception ex)
        {
            // Firma inválida o payload corrupto
            return BadRequest(new { error = "Invalid Stripe webhook.", detail = ex.Message });
        }

        // 3) Procesar SOLO lo necesario (Checkout Session)
        // Usamos checkout.session.completed porque ahí tenemos client_reference_id=OrderId y metadata.
        const string CheckoutSessionCompleted = "checkout.session.completed";
        const string CheckoutSessionAsyncSucceeded = "checkout.session.async_payment_succeeded";

        if (stripeEvent.Type == CheckoutSessionCompleted || stripeEvent.Type == CheckoutSessionAsyncSucceeded)
        {
            var session = stripeEvent.Data.Object as Session;
            if (session is null)
                return Ok();

            // Para mode=payment: solo continuar si está pagada
            // (En async, también llega como paid)
            if (!string.Equals(session.Mode, "payment", StringComparison.OrdinalIgnoreCase))
                return Ok();

            // Stripe usa "paid"/"unpaid"
            if (!string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
                return Ok();

            // 4) Localizar PaymentAttempt/Order
            // Preferimos ProviderSessionId=session.Id (porque nosotros lo guardamos)
            var attempt = await _db.PaymentAttempts
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync(p =>
                    p.Provider == "stripe" &&
                    (p.ProviderSessionId == session.Id || p.ProviderPaymentId == session.PaymentIntentId),
                    ct);

            if (attempt is null)
            {
                // No tenemos un attempt para este evento -> aceptamos para evitar retries infinitos,
                // pero en prod conviene log/bitácora.
                return Ok();
            }

            var order = await _db.Orders.SingleOrDefaultAsync(o => o.Id == attempt.OrderId, ct);
            if (order is null)
                return Ok();

            // 5) Idempotencia: si ya está pagada + attempt succeeded, no tocar
            if (order.PaidAt is not null && order.Status == OrderStatus.Paid && attempt.Status == PaymentStatus.Succeeded)
                return Ok();

            // 6) Validación cruzada monto/moneda (Stripe viene en cents)
            // session.AmountTotal puede ser null en algunos edge cases, pero normalmente viene.
            var stripeCurrency = (session.Currency ?? "").Trim().ToUpperInvariant();
            var stripeAmount = session.AmountTotal.HasValue
                ? Math.Round(session.AmountTotal.Value / 100m, 2, MidpointRounding.AwayFromZero)
                : (decimal?)null;

            // Si Stripe sí manda monto/moneda, exigimos match.
            if (stripeAmount.HasValue)
            {
                if (!string.Equals(stripeCurrency, order.Currency?.Trim().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase) ||
                    stripeAmount.Value != order.Total)
                {
                    // No marcamos pagada si no cuadra (evita fraudes/mala config)
                    return Conflict(new
                    {
                        error = "Amount/currency mismatch.",
                        orderTotal = order.Total,
                        orderCurrency = order.Currency,
                        stripeAmount = stripeAmount.Value,
                        stripeCurrency
                    });
                }
            }

            // 7) Marcar succeeded + order paid + cerrar carrito
            var now = DateTime.UtcNow;

            attempt.Status = PaymentStatus.Succeeded;
            attempt.ProviderSessionId = session.Id;
            if (!string.IsNullOrWhiteSpace(session.PaymentIntentId))
                attempt.ProviderPaymentId = session.PaymentIntentId;

            attempt.Amount = order.Total;
            attempt.Currency = order.Currency;
            attempt.RawJson = json;
            attempt.UpdatedAt = now;

            order.Status = OrderStatus.Paid;
            order.PaidAt ??= now;
            order.Provider = "stripe";
            order.ProviderSessionId = session.Id;
            order.ProviderPaymentId = session.PaymentIntentId;
            order.UpdatedAt = now;

            await CloseCheckoutPendingCartAsync(order, now, ct);

            await _db.SaveChangesAsync(ct);
            return Ok();
        }

        // Ignorar demás eventos por ahora (pero responder 200 para que Stripe no reintente)
        return Ok();
    }

    private async Task CloseCheckoutPendingCartAsync(Order order, DateTime now, CancellationToken ct)
    {
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