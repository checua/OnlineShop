using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Api.Data;
using OnlineShop.Api.Domain;

namespace OnlineShop.Api.Controllers;

[ApiController]
[Route("api/cart")]
public sealed class CartController : ControllerBase
{
    private const string GuestHeader = "X-Guest-Id";
    private readonly OnlineShopDbContext _db;

    public CartController(OnlineShopDbContext db) => _db = db;

    // ✅ Record => ya puedes usar "with" si lo ocupas
    public sealed record AddCartItemRequest(Guid ProductId, Guid? VariantId, int Quantity = 1);

    public sealed record CartItemDto(
        Guid ItemId,
        Guid ProductId,
        Guid? VariantId,
        int Quantity,
        decimal UnitPrice,
        decimal LineTotal,
        string ProductName,
        string? VariantSku,
        string? VariantSize,
        string? VariantColor,
        string? ImageUrl
    );

    public sealed record CartDto(
        Guid CartId,
        string StoreSlug,
        string? GuestId,
        int ItemsCount,
        decimal Subtotal,
        List<CartItemDto> Items
    );

    // GET /api/cart/{storeSlug}
    [HttpGet("{storeSlug}")]
    public async Task<ActionResult<CartDto>> GetCart([FromRoute] string storeSlug, CancellationToken ct)
    {
        var store = await _db.Stores
            .AsNoTracking()
            .Where(s => s.Slug == storeSlug && s.Status == "Approved")
            .Select(s => new { s.Id, s.Slug })
            .SingleOrDefaultAsync(ct);

        if (store is null) return NotFound(new { error = "Tienda no encontrada o no aprobada." });

        var userId = GetUserId();
        var guestId = userId is null ? GetGuestIdOrNull() : null;

        if (userId is null && string.IsNullOrWhiteSpace(guestId))
            return BadRequest(new { error = $"Header requerido: {GuestHeader}" });

        var cart = await FindActiveCart(store.Id, userId, guestId!, ct);

        if (cart is null)
        {
            // No creamos carrito en GET si no existe (opcional).
            // Si tú quieres crearlo aquí, llama GetOrCreateActiveCart(...) en vez de FindActiveCart(...)
            return Ok(new CartDto(
                CartId: Guid.Empty,
                StoreSlug: store.Slug,
                GuestId: guestId,
                ItemsCount: 0,
                Subtotal: 0m,
                Items: new List<CartItemDto>()
            ));
        }

        return Ok(ToDto(cart, store.Slug, guestId));
    }

    // POST /api/cart/{storeSlug}/items
    [HttpPost("{storeSlug}/items")]
    public async Task<ActionResult<CartDto>> AddItem([FromRoute] string storeSlug, [FromBody] AddCartItemRequest req, CancellationToken ct)
    {
        // Normaliza qty (sin "with" si no quieres)
        var qty = req.Quantity < 1 ? 1 : Math.Min(req.Quantity, 99);

        var store = await _db.Stores
            .AsNoTracking()
            .Where(s => s.Slug == storeSlug && s.Status == "Approved")
            .Select(s => new { s.Id, s.Slug })
            .SingleOrDefaultAsync(ct);

        if (store is null) return NotFound(new { error = "Tienda no encontrada o no aprobada." });

        var userId = GetUserId();
        var guestId = userId is null ? GetGuestIdOrNull() : null;

        if (userId is null && string.IsNullOrWhiteSpace(guestId))
            return BadRequest(new { error = $"Header requerido: {GuestHeader}" });

        // Producto + variantes + imagen principal (proyección ligera)
        var product = await _db.Products
            .AsNoTracking()
            .Where(p => p.Id == req.ProductId && p.StoreId == store.Id && p.IsActive)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.BasePrice,
                HasVariants = p.Variants.Any(),
                MainImageUrl = p.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault()
            })
            .SingleOrDefaultAsync(ct);

        if (product is null)
            return BadRequest(new { error = "ProductId inválido para esta tienda (o inactivo)." });

        ProductVariant? variantEntity = null;

        if (product.HasVariants)
        {
            if (req.VariantId is null)
                return BadRequest(new { error = "VariantId requerido para este producto." });

            variantEntity = await _db.ProductVariants
                .AsNoTracking()
                .Where(v => v.Id == req.VariantId.Value && v.ProductId == product.Id)
                .SingleOrDefaultAsync(ct);

            if (variantEntity is null)
                return BadRequest(new { error = "VariantId inválido." });
        }
        else
        {
            // si el producto NO tiene variantes, ignoramos VariantId si llega
            variantEntity = null;
        }

        var unitPrice = product.BasePrice + (variantEntity?.PriceDelta ?? 0m);

        // ✅ Carrito activo (crea si no existe) con manejo de carrera
        var cart = await GetOrCreateActiveCart(store.Id, userId, guestId!, ct);

        // Buscar item existente (tracked)
        var existingItem = await _db.CartItems
            .Where(i => i.CartId == cart.Id && i.ProductId == product.Id && i.VariantId == (variantEntity != null ? variantEntity.Id : null))
            .SingleOrDefaultAsync(ct);

        var now = DateTime.UtcNow;

        if (existingItem is null)
        {
            var newItem = new CartItem
            {
                Id = Guid.NewGuid(),
                CartId = cart.Id,
                ProductId = product.Id,
                VariantId = variantEntity?.Id,
                Quantity = qty,
                UnitPrice = unitPrice,

                ProductName = product.Name,
                VariantSku = variantEntity?.Sku,
                VariantSize = variantEntity?.Size,
                VariantColor = variantEntity?.Color,
                ImageUrl = product.MainImageUrl,

                CreatedAt = now,
                UpdatedAt = now
            };

            // ✅ NUEVO => Add (NO Update)
            _db.CartItems.Add(newItem);
        }
        else
        {
            existingItem.Quantity += qty;
            existingItem.UnitPrice = unitPrice; // opcional (si quieres mantener el snapshot, quítalo)
            existingItem.UpdatedAt = now;
        }

        cart.UpdatedAt = now;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Carrera típica: dos requests meten el mismo item al mismo tiempo por el índice único.
            // Re-lee y actualiza.
            var retryItem = await _db.CartItems
                .Where(i => i.CartId == cart.Id && i.ProductId == product.Id && i.VariantId == (variantEntity != null ? variantEntity.Id : null))
                .SingleAsync(ct);

            retryItem.Quantity += qty;
            retryItem.UpdatedAt = DateTime.UtcNow;
            cart.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
        }

        // Responder el carrito actualizado
        var fullCart = await _db.Carts
            .AsNoTracking()
            .Include(c => c.Items)
            .Where(c => c.Id == cart.Id)
            .SingleAsync(ct);

        return Ok(ToDto(fullCart, store.Slug, guestId));
    }

    // ----------------- Helpers -----------------

    private string? GetUserId()
        => User?.Identity?.IsAuthenticated == true
            ? User.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;

    private string? GetGuestIdOrNull()
    {
        if (!Request.Headers.TryGetValue(GuestHeader, out var v)) return null;
        var s = v.ToString().Trim();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private async Task<Cart?> FindActiveCart(Guid storeId, string? userId, string guestId, CancellationToken ct)
    {
        var q = _db.Carts
            .AsNoTracking()
            .Include(c => c.Items)
            .Where(c => c.StoreId == storeId && c.Status == CartStatus.Active);

        q = userId is not null
            ? q.Where(c => c.UserId == userId)
            : q.Where(c => c.GuestId == guestId);

        return await q.SingleOrDefaultAsync(ct);
    }

    private async Task<Cart> GetOrCreateActiveCart(Guid storeId, string? userId, string guestId, CancellationToken ct)
    {
        var q = _db.Carts
            .Include(c => c.Items)
            .Where(c => c.StoreId == storeId && c.Status == CartStatus.Active);

        q = userId is not null
            ? q.Where(c => c.UserId == userId)
            : q.Where(c => c.GuestId == guestId);

        var cart = await q.SingleOrDefaultAsync(ct);
        if (cart is not null) return cart;

        var now = DateTime.UtcNow;

        cart = new Cart
        {
            Id = Guid.NewGuid(),
            StoreId = storeId,
            UserId = userId,
            GuestId = userId is null ? guestId : null,
            Status = CartStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Carts.Add(cart);

        try
        {
            await _db.SaveChangesAsync(ct);
            return cart;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Ya lo creó alguien más, recargamos
            _db.Entry(cart).State = EntityState.Detached;

            var existing = await q.SingleAsync(ct);
            return existing;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is SqlException sql && (sql.Number == 2601 || sql.Number == 2627);

    private static CartDto ToDto(Cart cart, string storeSlug, string? guestId)
    {
        var items = cart.Items
            .OrderBy(i => i.CreatedAt)
            .Select(i => new CartItemDto(
                ItemId: i.Id,
                ProductId: i.ProductId,
                VariantId: i.VariantId,
                Quantity: i.Quantity,
                UnitPrice: i.UnitPrice,
                LineTotal: i.UnitPrice * i.Quantity,
                ProductName: i.ProductName,
                VariantSku: i.VariantSku,
                VariantSize: i.VariantSize,
                VariantColor: i.VariantColor,
                ImageUrl: i.ImageUrl
            ))
            .ToList();

        var subtotal = items.Sum(x => x.LineTotal);
        var itemsCount = items.Sum(x => x.Quantity);

        return new CartDto(
            CartId: cart.Id,
            StoreSlug: storeSlug,
            GuestId: guestId,
            ItemsCount: itemsCount,
            Subtotal: subtotal,
            Items: items
        );
    }
}
