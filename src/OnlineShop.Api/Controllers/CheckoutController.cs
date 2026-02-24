// src/OnlineShop.Api/Controllers/CheckoutController.cs
using System.Security.Claims;
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
[Route("api/checkout")]
public sealed class CheckoutController : ControllerBase
{
    private readonly OnlineShopDbContext _db;
    private readonly StripeOptions _stripe;

    public CheckoutController(OnlineShopDbContext db, IOptions<StripeOptions> stripe)
    {
        _db = db;
        _stripe = stripe.Value;

        // Set global (si existe)
        if (!string.IsNullOrWhiteSpace(_stripe.SecretKey))
            StripeConfiguration.ApiKey = _stripe.SecretKey;
    }

    public sealed record ShippingDto(
        string Name,
        string Phone,
        string Address1,
        string? Address2,
        string City,
        string State,
        string PostalCode,
        string Country = "MX"
    );

    public sealed record StartCheckoutRequest(
        string CustomerEmail,
        ShippingDto Shipping,
        string PaymentMethod = "manual" // manual | stripe (por ahora)
    );

    // POST /api/checkout/{storeSlug}/start
    [HttpPost("{storeSlug}/start")]
    public async Task<IActionResult> Start([FromRoute] string storeSlug, [FromBody] StartCheckoutRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.CustomerEmail))
            return BadRequest(new { error = "CustomerEmail requerido." });

        var (userId, guestId) = ResolveActor();
        if (userId is null && guestId is null)
            return BadRequest(new { error = "Falta X-Guest-Id (o autenticar usuario)." });

        var store = await _db.Stores
            .AsNoTracking()
            .Where(s => s.Slug == storeSlug && s.Status == "Approved")
            .Select(s => new { s.Id, s.Slug })
            .SingleOrDefaultAsync(ct);

        if (store is null)
            return NotFound(new { error = "Store no encontrada o no aprobada." });

        // ===== Carrito ACTIVO (tracked) =====
        var cart = await _db.Carts
            .Include(c => c.Items)
            .Where(c => c.StoreId == store.Id && c.Status == CartStatus.Active)
            .Where(c => userId != null ? c.UserId == userId : c.GuestId == guestId)
            .SingleOrDefaultAsync(ct);

        if (cart is null)
        {
            // Si existe uno CheckoutPending, dilo explícito (no “vacío”)
            var pending = await _db.Carts.AsNoTracking()
                .Where(c => c.StoreId == store.Id && c.Status == CartStatus.CheckoutPending)
                .Where(c => userId != null ? c.UserId == userId : c.GuestId == guestId)
                .Select(c => c.Id)
                .FirstOrDefaultAsync(ct);

            if (pending != Guid.Empty)
                return Conflict(new { error = "Carrito no está activo (CheckoutPending). Usa otro GuestId o reinicia el carrito en BD." });

            return BadRequest(new { error = "Carrito vacío o no encontrado." });
        }

        if (cart.Items.Count == 0)
            return BadRequest(new { error = "Carrito vacío o no encontrado." });

        var now = DateTime.UtcNow;

        // Totales desde snapshot del carrito
        var subtotal = cart.Items.Sum(i => i.UnitPrice * i.Quantity);
        var shipping = 0m; // MVP
        var tax = 0m;      // MVP
        var total = subtotal + shipping + tax;

        // ===== Crear Order en memoria (todavía NO guardamos) =====
        var order = new Order
        {
            Id = Guid.NewGuid(),
            StoreId = store.Id,
            UserId = userId,
            GuestId = guestId,

            Status = OrderStatus.PendingPayment,
            Currency = string.IsNullOrWhiteSpace(_stripe.Currency) ? "MXN" : _stripe.Currency,

            Subtotal = subtotal,
            Shipping = shipping,
            Tax = tax,
            Total = total,

            CustomerEmail = req.CustomerEmail.Trim(),

            ShippingName = req.Shipping.Name.Trim(),
            ShippingPhone = req.Shipping.Phone.Trim(),
            ShippingAddress1 = req.Shipping.Address1.Trim(),
            ShippingAddress2 = string.IsNullOrWhiteSpace(req.Shipping.Address2) ? null : req.Shipping.Address2.Trim(),
            ShippingCity = req.Shipping.City.Trim(),
            ShippingState = req.Shipping.State.Trim(),
            ShippingPostalCode = req.Shipping.PostalCode.Trim(),
            ShippingCountry = (req.Shipping.Country ?? "MX").Trim().ToUpperInvariant(),

            CreatedAt = now,
            UpdatedAt = now
        };

        foreach (var ci in cart.Items)
        {
            order.Items.Add(new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                ProductId = ci.ProductId,
                VariantId = ci.VariantId,

                Quantity = ci.Quantity,
                UnitPrice = ci.UnitPrice,
                LineTotal = ci.UnitPrice * ci.Quantity,

                ProductName = ci.ProductName,
                VariantSku = ci.VariantSku,
                VariantSize = ci.VariantSize,
                VariantColor = ci.VariantColor,
                ImageUrl = ci.ImageUrl,

                CreatedAt = now,
                UpdatedAt = now
            });
        }

        // ===== Elegir método de pago =====
        var method = (req.PaymentMethod ?? "manual").Trim().ToLowerInvariant();

        string provider;
        string providerPaymentId;
        string? providerSessionId = null;
        string? paymentUrl = null;
        PaymentStatus payStatus = PaymentStatus.Pending;

        if (method == "manual")
        {
            provider = "manual";
            providerPaymentId = $"manual-{order.Id}";
            paymentUrl = null;
            payStatus = PaymentStatus.Pending; // (manual = “pendiente”)
        }
        else if (method == "stripe")
        {
            provider = "stripe";

            if (string.IsNullOrWhiteSpace(_stripe.SecretKey))
                return BadRequest(new { error = "Stripe no configurado (SecretKey vacío)." });

            var successUrl = $"{_stripe.FrontendBaseUrl}/checkout/success?orderId={order.Id}";
            var cancelUrl = $"{_stripe.FrontendBaseUrl}/checkout/cancel?orderId={order.Id}";

            var sessionOptions = new SessionCreateOptions
            {
                Mode = "payment",
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl,
                CustomerEmail = order.CustomerEmail,
                ClientReferenceId = order.Id.ToString(),
                Metadata = new Dictionary<string, string>
                {
                    ["orderId"] = order.Id.ToString(),
                    ["storeId"] = store.Id.ToString()
                },
                LineItems = order.Items.Select(i => new SessionLineItemOptions
                {
                    Quantity = i.Quantity,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = order.Currency.ToLowerInvariant(),
                        UnitAmount = (long)Math.Round(i.UnitPrice * 100m, 0, MidpointRounding.AwayFromZero),
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = i.ProductName,
                            Description = string.Join(" / ",
                                new[] { i.VariantSku, i.VariantSize, i.VariantColor }.Where(x => !string.IsNullOrWhiteSpace(x))),
                            Images = string.IsNullOrWhiteSpace(i.ImageUrl) ? null : new List<string> { i.ImageUrl! }
                        }
                    }
                }).ToList()
            };

            var service = new SessionService();
            var session = await service.CreateAsync(sessionOptions, cancellationToken: ct);

            providerSessionId = session.Id;
            providerPaymentId = session.PaymentIntentId ?? $"pi-pending-{order.Id}";
            paymentUrl = session.Url;
            payStatus = PaymentStatus.Pending;
        }
        else
        {
            return BadRequest(new { error = "paymentMethod inválido. Usa: manual | stripe" });
        }

        // ===== AQUI está la corrección clave: INSERT del PaymentAttempt =====
        order.Provider = provider;
        order.ProviderSessionId = providerSessionId;
        order.ProviderPaymentId = providerPaymentId;

        order.Payments.Add(new PaymentAttempt
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Provider = provider,
            ProviderPaymentId = providerPaymentId,
            ProviderSessionId = providerSessionId,
            Status = payStatus,
            Amount = order.Total,
            Currency = order.Currency,
            RawJson = null,
            CreatedAt = now,
            UpdatedAt = now
        });

        // Congelar carrito (ya que la orden quedó armada)
        cart.Status = CartStatus.CheckoutPending; // asegúrate que exista en tu enum
        cart.UpdatedAt = now;

        _db.Orders.Add(order);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "Conflicto de concurrencia. Reintenta." });
        }

        return Ok(new
        {
            orderId = order.Id,
            provider,
            paymentUrl
        });
    }

    private (string? userId, string? guestId) ResolveActor()
    {
        var userId = User?.Identity?.IsAuthenticated == true
            ? User.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;

        string? guestId = null;
        if (Request.Headers.TryGetValue("X-Guest-Id", out var values))
        {
            var v = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(v)) guestId = v.Trim();
        }

        return (userId, guestId);
    }
}