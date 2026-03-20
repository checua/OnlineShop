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
    private readonly ILogger<StripeWebhookController> _logger;

    public StripeWebhookController(
        OnlineShopDbContext db,
        IOptions<StripeOptions> stripe,
        ILogger<StripeWebhookController> logger)
    {
        _db = db;
        _stripe = stripe.Value;
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(_stripe.SecretKey))
            StripeConfiguration.ApiKey = _stripe.SecretKey;
    }

    [HttpPost]
    public async Task<IActionResult> Handle(CancellationToken ct)
    {
        var json = await new StreamReader(Request.Body).ReadToEndAsync(ct);

        Event stripeEvent;
        try
        {
            var sigHeader = Request.Headers["Stripe-Signature"].ToString();

            if (!string.IsNullOrWhiteSpace(_stripe.WebhookSecret))
            {
                stripeEvent = EventUtility.ConstructEvent(
                    json,
                    sigHeader,
                    _stripe.WebhookSecret,
                    throwOnApiVersionMismatch: false
                );
            }
            else
            {
                // Solo fallback de desarrollo
                stripeEvent = EventUtility.ParseEvent(json);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid Stripe webhook. Body: {Body}", json);
            return BadRequest(new
            {
                error = "Invalid Stripe webhook.",
                detail = ex.Message
            });
        }

        const string CheckoutSessionCompleted = "checkout.session.completed";
        const string CheckoutSessionAsyncSucceeded = "checkout.session.async_payment_succeeded";

        if (stripeEvent.Type == CheckoutSessionCompleted || stripeEvent.Type == CheckoutSessionAsyncSucceeded)
        {
            var session = stripeEvent.Data.Object as Session;
            if (session is null)
            {
                _logger.LogInformation(
                    "Stripe event {EventType} parsed but Session was null.",
                    stripeEvent.Type
                );
                return Ok();
            }

            if (!string.Equals(session.Mode, "payment", StringComparison.OrdinalIgnoreCase))
                return Ok();

            if (!string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
                return Ok();

            var attempt = await _db.PaymentAttempts
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync(p =>
                    p.Provider == "stripe" &&
                    (p.ProviderSessionId == session.Id || p.ProviderPaymentId == session.PaymentIntentId),
                    ct);

            if (attempt is null)
            {
                _logger.LogInformation(
                    "Stripe webhook accepted but no PaymentAttempt found. SessionId={SessionId}, PaymentIntentId={PaymentIntentId}",
                    session.Id,
                    session.PaymentIntentId
                );
                return Ok();
            }

            var order = await _db.Orders.SingleOrDefaultAsync(o => o.Id == attempt.OrderId, ct);
            if (order is null)
            {
                _logger.LogInformation(
                    "Stripe webhook accepted but Order not found for attempt {AttemptId}.",
                    attempt.Id
                );
                return Ok();
            }

            if (order.PaidAt is not null &&
                order.Status == OrderStatus.Paid &&
                attempt.Status == PaymentStatus.Succeeded)
            {
                return Ok();
            }

            var stripeCurrency = (session.Currency ?? string.Empty).Trim().ToUpperInvariant();
            var stripeAmount = session.AmountTotal.HasValue
                ? Math.Round(session.AmountTotal.Value / 100m, 2, MidpointRounding.AwayFromZero)
                : (decimal?)null;

            if (stripeAmount.HasValue)
            {
                var orderCurrency = (order.Currency ?? string.Empty).Trim().ToUpperInvariant();

                if (!string.Equals(stripeCurrency, orderCurrency, StringComparison.OrdinalIgnoreCase) ||
                    stripeAmount.Value != order.Total)
                {
                    _logger.LogWarning(
                        "Stripe amount/currency mismatch. OrderId={OrderId}, OrderTotal={OrderTotal}, OrderCurrency={OrderCurrency}, StripeAmount={StripeAmount}, StripeCurrency={StripeCurrency}",
                        order.Id,
                        order.Total,
                        order.Currency,
                        stripeAmount.Value,
                        stripeCurrency
                    );

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

            _logger.LogInformation(
                "Stripe webhook processed successfully. OrderId={OrderId}, SessionId={SessionId}, PaymentIntentId={PaymentIntentId}",
                order.Id,
                session.Id,
                session.PaymentIntentId
            );

            return Ok();
        }

        // Eventos no usados por ahora: responder 200 para evitar reintentos inútiles
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