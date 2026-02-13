// src/OnlineShop.Api/Controllers/CheckoutController.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShop.Api.Data;
using OnlineShop.Api.Domain;
using OnlineShop.Api.Options;
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
        ShippingDto Shipping
    );

    // POST /api/checkout/{storeSlug}/start
    [HttpPost("{storeSlug}/start")]
    public async Task<IActionResult> Start(
        [FromRoute] string storeSlug,
        [FromBody] StartCheckoutRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(storeSlug))
            return BadRequest(new { error = "storeSlug requerido." });

        if (req is null)
            return BadRequest(new { error = "Body requerido." });

        if (string.IsNullOrWhiteSpace(req.CustomerEmail))
            return BadRequest(new { error = "CustomerEmail requerido." });

        if (req.Shipping is null)
            return BadRequest(new { error = "Shipping requerido." });

        if (string.IsNullOrWhiteSpace(req.Shipping.Name) ||
            string.IsNullOrWhiteSpace(req.Shipping.Phone) ||
            string.IsNullOrWhiteSpace(req.Shipping.Address1) ||
            string.IsNullOrWhiteSpace(req.Shipping.City) ||
            string.IsNullOrWhiteSpace(req.Shipping.State) ||
            string.IsNullOrWhiteSpace(req.Shipping.PostalCode))
        {
            return BadRequest(new { error = "Shipping incompleto (Name/Phone/Address1/City/State/PostalCode)." });
        }

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

        // Trae carrito activo del actor
        var cart = await _db.Carts
            .Include(c => c.Items)
            .Where(c => c.StoreId == store.Id && c.Status == CartStatus.Active)
            .Where(c => userId != null ? c.UserId == userId : c.GuestId == guestId)
            .SingleOrDefaultAsync(ct);

        if (cart is null || cart.Items.Count == 0)
            return BadRequest(new { error = "Carrito vacío o no encontrado." });

        var now = DateTime.UtcNow;

        // Totales desde snapshot del carrito
        var subtotal = cart.Items.Sum(i => i.UnitPrice * i.Quantity);
        var shipping = 0m; // MVP
        var tax = 0m;      // MVP
        var total = subtotal + shipping + tax;

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

        // Asegura colecciones (por si tus entidades no las inicializan)
        order.Items ??= new List<OrderItem>();
        order.Payments ??= new List<PaymentAttempt>();

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

        _db.Orders.Add(order);

        // Congelar carrito mientras paga
        cart.Status = CartStatus.CheckoutPending;
        cart.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);

        // ===== Stripe Checkout Session =====
        if (string.IsNullOrWhiteSpace(_stripe.SecretKey))
        {
            // Dev / sin proveedor configurado
            return Ok(new
            {
                orderId = order.Id,
                paymentUrl = (string?)null,
                note = "Stripe SecretKey no configurada (modo dev)."
            });
        }

        var frontendBase = string.IsNullOrWhiteSpace(_stripe.FrontendBaseUrl)
            ? "http://localhost:3000"
            : _stripe.FrontendBaseUrl.Trim().TrimEnd('/');

        var successUrl = $"{frontendBase}/checkout/success?orderId={order.Id}";
        var cancelUrl = $"{frontendBase}/checkout/cancel?orderId={order.Id}";

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
                            new[] { i.VariantSku, i.VariantSize, i.VariantColor }
                                .Where(x => !string.IsNullOrWhiteSpace(x))),
                        Images = string.IsNullOrWhiteSpace(i.ImageUrl) ? null : new List<string> { i.ImageUrl! }
                    }
                }
            }).ToList()
        };

        var service = new SessionService();
        var session = await service.CreateAsync(sessionOptions, cancellationToken: ct);

        order.Provider = "stripe";
        order.ProviderSessionId = session.Id;
        order.ProviderPaymentId = session.PaymentIntentId;

        order.Payments.Add(new PaymentAttempt
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Provider = "stripe",
            ProviderSessionId = session.Id,
            ProviderPaymentId = session.PaymentIntentId ?? "",
            Status = PaymentStatus.Pending,
            Amount = order.Total,
            Currency = order.Currency,
            CreatedAt = now,
            UpdatedAt = now
        });

        order.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);

        return Ok(new { orderId = order.Id, paymentUrl = session.Url });
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
