using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OnlineShop.Api.Data;
using OnlineShop.Api.Domain;
using OnlineShop.Api.Options;
using OnlineShop.Api.Services;

namespace OnlineShop.Api.Controllers;

[ApiController]
[Route("api/checkout")]
public sealed class CheckoutController : ControllerBase
{
    private readonly OnlineShopDbContext _db;
    private readonly IConfiguration _cfg;
    private readonly MercadoPagoClient _mp;
    private readonly MercadoPagoOptions _mpOpt;

    public CheckoutController(OnlineShopDbContext db, IConfiguration cfg, MercadoPagoClient mp, Microsoft.Extensions.Options.IOptions<MercadoPagoOptions> mpOpt)
    {
        _db = db;
        _cfg = cfg;
        _mp = mp;
        _mpOpt = mpOpt.Value;
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
        string PaymentMethod = "manual" // "manual" | "mercadopago"
    );

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

        if (store is null) return NotFound(new { error = "Store no encontrada o no aprobada." });

        // carrito ACTIVO (tracked) + items (no tracking)
        var cart = await _db.Carts
            .SingleOrDefaultAsync(c => c.StoreId == store.Id && c.Status == CartStatus.Active &&
                (userId != null ? c.UserId == userId : c.GuestId == guestId), ct);

        if (cart is null) return BadRequest(new { error = "Carrito vacío o no encontrado." });

        var items = await _db.CartItems
            .AsNoTracking()
            .Where(i => i.CartId == cart.Id)
            .ToListAsync(ct);

        if (items.Count == 0) return BadRequest(new { error = "Carrito vacío o no encontrado." });

        var now = DateTime.UtcNow;

        var subtotal = items.Sum(i => i.UnitPrice * i.Quantity);
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
            Currency = _mpOpt.Currency,

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

        foreach (var ci in items)
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

        // congela carrito (sale de “Active”)
        cart.Status = CartStatus.CheckoutPending;
        cart.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);

        // ===== Pago =====
        var method = (req.PaymentMethod ?? "manual").Trim().ToLowerInvariant();

        if (method == "mercadopago")
        {
            if (!_mp.IsConfigured)
                return Ok(new { orderId = order.Id, paymentUrl = (string?)null, note = "MercadoPago AccessToken no configurado." });

            var mpItems = order.Items.Select(i =>
                (title: i.ProductName, quantity: i.Quantity, unitPrice: i.UnitPrice, currencyId: order.Currency)).ToList();

            var (prefId, initPoint) = await _mp.CreatePreferenceAsync(order.Id, order.CustomerEmail, mpItems, ct);

            order.Provider = "mercadopago";
            order.ProviderSessionId = prefId;

            order.Payments.Add(new PaymentAttempt
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Provider = "mercadopago",
                ProviderSessionId = prefId,
                ProviderPaymentId = "",
                Status = PaymentStatus.Pending,
                Amount = order.Total,
                Currency = order.Currency,
                CreatedAt = now,
                UpdatedAt = now
            });

            order.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);

            return Ok(new { orderId = order.Id, paymentUrl = initPoint, provider = "mercadopago" });
        }

        // manual / spei etc (MVP)
        return Ok(new
        {
            orderId = order.Id,
            provider = "manual",
            paymentUrl = (string?)null,
            note = "MVP: pago manual/transfer. Próximo: SPEI con referencia + confirmación."
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