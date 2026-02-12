using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using OnlineShop.Api.Data;
using OnlineShop.Api.Domain;

namespace OnlineShop.Api.Controllers;

[ApiController]
[Route("api/cart")]
public sealed class CartController : ControllerBase
{
    private readonly OnlineShopDbContext _db;
    public CartController(OnlineShopDbContext db) => _db = db;

    // ===== Requests =====
    public sealed record AddCartItemRequest(Guid ProductId, Guid? VariantId, int Quantity = 1);
    public sealed record UpdateCartItemRequest(int Quantity);

    // ===== DTOs =====
    public sealed record CartDto(
        Guid CartId,
        string StoreSlug,
        string? GuestId,
        int ItemsCount,
        decimal Subtotal,
        List<CartItemDto> Items
    );

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

    // GET /api/cart/{storeSlug}
    [HttpGet("{storeSlug}")]
    public async Task<IActionResult> GetCart([FromRoute] string storeSlug, CancellationToken ct)
    {
        var (userId, guestId) = ResolveActor();
        if (userId is null && guestId is null)
            return BadRequest(new { error = "Falta X-Guest-Id (o autenticar usuario)." });

        var store = await _db.Stores
            .AsNoTracking()
            .Where(s => s.Slug == storeSlug && s.Status == "Approved")
            .Select(s => new { s.Id, s.Slug })
            .SingleOrDefaultAsync(ct);

        if (store is null) return NotFound(new { error = "Store no encontrada o no aprobada." });

        // Crea/obtiene carrito activo (y si aplica: merge guest -> user)
        await GetOrCreateActiveCart(store.Id, userId, guestId, ct);

        // Responde consistente desde DB
        var dto = await BuildCartDto(store.Id, store.Slug, userId, guestId, ct);
        return Ok(dto);
    }

    // POST /api/cart/{storeSlug}/items
    [HttpPost("{storeSlug}/items")]
    public async Task<IActionResult> AddItem([FromRoute] string storeSlug, [FromBody] AddCartItemRequest req, CancellationToken ct)
    {
        if (req.Quantity <= 0) return BadRequest(new { error = "Quantity inválida." });

        var (userId, guestId) = ResolveActor();
        if (userId is null && guestId is null)
            return BadRequest(new { error = "Falta X-Guest-Id (o autenticar usuario)." });

        var store = await _db.Stores
            .AsNoTracking()
            .Where(s => s.Slug == storeSlug && s.Status == "Approved")
            .Select(s => new { s.Id, s.Slug })
            .SingleOrDefaultAsync(ct);

        if (store is null) return NotFound(new { error = "Store no encontrada o no aprobada." });

        // Producto + datos útiles (incluye si tiene variantes e imagen principal)
        var product = await _db.Products
            .AsNoTracking()
            .Where(p => p.Id == req.ProductId && p.StoreId == store.Id && p.IsActive)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.BasePrice,
                VariantCount = p.Variants.Count(),
                MainImageUrl = p.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault()
            })
            .SingleOrDefaultAsync(ct);

        if (product is null)
            return BadRequest(new { error = "ProductId inválido (no existe / no pertenece a la tienda / inactivo)." });

        // Si hay variantes, variantId es requerido. Si no hay variantes, debe ir null.
        if (product.VariantCount > 0 && req.VariantId is null)
            return BadRequest(new { error = "VariantId requerido para este producto." });

        if (product.VariantCount == 0 && req.VariantId is not null)
            return BadRequest(new { error = "Este producto no maneja variantes. Envía variantId = null." });

        // Carga variante si aplica y valida que pertenezca al producto
        ProductVariant? variant = null;
        if (req.VariantId is not null)
        {
            variant = await _db.ProductVariants
                .AsNoTracking()
                .SingleOrDefaultAsync(v => v.Id == req.VariantId.Value && v.ProductId == product.Id, ct);

            if (variant is null)
                return BadRequest(new { error = "VariantId inválido (no existe o no pertenece al producto)." });
        }

        var now = DateTime.UtcNow;
        var unitPrice = product.BasePrice + (variant?.PriceDelta ?? 0m);

        // Carrito activo (aquí también puede hacer merge guest->user si aplica)
        var cart = await GetOrCreateActiveCart(store.Id, userId, guestId, ct);

        // Busca si ya existe línea del mismo producto+variante
        var existing = cart.Items.FirstOrDefault(i =>
            i.ProductId == product.Id && i.VariantId == req.VariantId);

        if (existing is null)
        {
            var item = new CartItem
            {
                Id = Guid.NewGuid(),
                CartId = cart.Id,
                ProductId = product.Id,
                VariantId = req.VariantId,
                Quantity = req.Quantity,
                UnitPrice = unitPrice,

                // snapshot
                ProductName = product.Name,
                VariantSku = variant?.Sku,
                VariantSize = variant?.Size,
                VariantColor = variant?.Color,
                ImageUrl = product.MainImageUrl,

                CreatedAt = now,
                UpdatedAt = now
            };

            cart.Items.Add(item);
        }
        else
        {
            existing.Quantity += req.Quantity;
            existing.UnitPrice = unitPrice;
            existing.ProductName = product.Name;
            existing.VariantSku = variant?.Sku;
            existing.VariantSize = variant?.Size;
            existing.VariantColor = variant?.Color;
            existing.ImageUrl = product.MainImageUrl;
            existing.UpdatedAt = now;
        }

        cart.UpdatedAt = now;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "Conflicto de concurrencia del carrito. Reintenta." });
        }

        var dto = await BuildCartDto(store.Id, store.Slug, userId, guestId, ct);
        return Ok(dto);
    }

    // PATCH /api/cart/{storeSlug}/items/{itemId}
    [HttpPatch("{storeSlug}/items/{itemId:guid}")]
    public async Task<IActionResult> UpdateItemQty([FromRoute] string storeSlug, [FromRoute] Guid itemId, [FromBody] UpdateCartItemRequest req, CancellationToken ct)
    {
        var (userId, guestId) = ResolveActor();
        if (userId is null && guestId is null)
            return BadRequest(new { error = "Falta X-Guest-Id (o autenticar usuario)." });

        if (req.Quantity < 0) return BadRequest(new { error = "Quantity inválida." });

        var store = await _db.Stores
            .AsNoTracking()
            .Where(s => s.Slug == storeSlug && s.Status == "Approved")
            .Select(s => new { s.Id, s.Slug })
            .SingleOrDefaultAsync(ct);

        if (store is null) return NotFound();

        var cart = await FindActiveCartTracked(store.Id, userId, guestId, ct);
        if (cart is null) return Ok(await BuildCartDto(store.Id, store.Slug, userId, guestId, ct));

        var item = cart.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return NotFound(new { error = "Item no encontrado en el carrito." });

        var now = DateTime.UtcNow;

        if (req.Quantity == 0)
        {
            cart.Items.Remove(item);
            _db.CartItems.Remove(item);
        }
        else
        {
            item.Quantity = req.Quantity;
            item.UpdatedAt = now;
        }

        cart.UpdatedAt = now;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "Conflicto de concurrencia del carrito. Reintenta." });
        }

        return Ok(await BuildCartDto(store.Id, store.Slug, userId, guestId, ct));
    }

    // DELETE /api/cart/{storeSlug}/items/{itemId}
    [HttpDelete("{storeSlug}/items/{itemId:guid}")]
    public async Task<IActionResult> RemoveItem([FromRoute] string storeSlug, [FromRoute] Guid itemId, CancellationToken ct)
    {
        var (userId, guestId) = ResolveActor();
        if (userId is null && guestId is null)
            return BadRequest(new { error = "Falta X-Guest-Id (o autenticar usuario)." });

        var store = await _db.Stores
            .AsNoTracking()
            .Where(s => s.Slug == storeSlug && s.Status == "Approved")
            .Select(s => new { s.Id, s.Slug })
            .SingleOrDefaultAsync(ct);

        if (store is null) return NotFound();

        var cart = await FindActiveCartTracked(store.Id, userId, guestId, ct);
        if (cart is null) return Ok(await BuildCartDto(store.Id, store.Slug, userId, guestId, ct));

        var item = cart.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return NotFound(new { error = "Item no encontrado." });

        cart.Items.Remove(item);
        _db.CartItems.Remove(item);
        cart.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "Conflicto de concurrencia del carrito. Reintenta." });
        }

        return Ok(await BuildCartDto(store.Id, store.Slug, userId, guestId, ct));
    }

    // ===== Helpers =====

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

    private async Task<Cart> GetOrCreateActiveCart(Guid storeId, string? userId, string? guestId, CancellationToken ct)
    {
        // ====== 0) Si viene userId + guestId => merge/claim ======
        if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(guestId))
        {
            var userCart = await FindActiveCartTracked(storeId, userId, null, ct);
            var guestCart = await FindActiveCartTracked(storeId, null, guestId, ct);

            // a) No hay carrito user, pero sí guest => “claim”: lo convierto a user
            if (userCart is null && guestCart is not null)
            {
                guestCart.UserId = userId;
                guestCart.GuestId = null;
                guestCart.UpdatedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync(ct);
                return guestCart;
            }

            // b) Hay ambos => merge items hacia user
            if (userCart is not null && guestCart is not null && userCart.Id != guestCart.Id)
            {
                var now = DateTime.UtcNow;

                foreach (var src in guestCart.Items.ToList())
                {
                    var dst = userCart.Items.FirstOrDefault(i => i.ProductId == src.ProductId && i.VariantId == src.VariantId);
                    if (dst is null)
                    {
                        // mover item
                        src.CartId = userCart.Id;
                        src.UpdatedAt = now;
                        userCart.Items.Add(src);
                    }
                    else
                    {
                        // sumar qty y eliminar duplicado
                        dst.Quantity += src.Quantity;
                        dst.UpdatedAt = now;

                        guestCart.Items.Remove(src);
                        _db.CartItems.Remove(src);
                    }
                }

                // IMPORTANTE: sacar a guestCart del filtro de unique index (Status != Active)
                guestCart.Status = CartStatus.Merged; // agrega este valor al enum (no requiere migración)
                guestCart.UpdatedAt = now;
                userCart.UpdatedAt = now;

                await _db.SaveChangesAsync(ct);
                return userCart;
            }

            if (userCart is not null) return userCart;
            // si no hay ninguno, cae al flujo normal de creación
        }

        // ====== 1) intenta cargar existente ======
        var existing = await FindActiveCartTracked(storeId, userId, guestId, ct);
        if (existing is not null) return existing;

        // ====== 2) crea nuevo (puede chocar con unique index si hay race => retry) ======
        var now2 = DateTime.UtcNow;
        var cart = new Cart
        {
            Id = Guid.NewGuid(),
            StoreId = storeId,
            UserId = userId,
            GuestId = guestId,
            Status = CartStatus.Active, // 0
            CreatedAt = now2,
            UpdatedAt = now2,
            Items = new List<CartItem>()
        };

        _db.Carts.Add(cart);

        try
        {
            await _db.SaveChangesAsync(ct);
            return cart;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();
            var retry = await FindActiveCartTracked(storeId, userId, guestId, ct);
            if (retry is not null) return retry;
            throw;
        }
    }

    private async Task<Cart?> FindActiveCartTracked(Guid storeId, string? userId, string? guestId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId) && string.IsNullOrWhiteSpace(guestId))
            return null;

        var q = _db.Carts
            .Include(c => c.Items)
            .Where(c => c.StoreId == storeId && c.Status == CartStatus.Active);

        if (!string.IsNullOrWhiteSpace(userId))
            q = q.Where(c => c.UserId == userId);
        else
            q = q.Where(c => c.GuestId == guestId);

        return await q.SingleOrDefaultAsync(ct);
    }

    private async Task<CartDto> BuildCartDto(Guid storeId, string storeSlug, string? userId, string? guestId, CancellationToken ct)
    {
        // OJO: filtrar por StoreId para que no truene cuando exista carrito en 2 tiendas
        var q = _db.Carts
            .AsNoTracking()
            .Where(c => c.StoreId == storeId && c.Status == CartStatus.Active);

        if (!string.IsNullOrWhiteSpace(userId))
            q = q.Where(c => c.UserId == userId);
        else
            q = q.Where(c => c.GuestId == guestId);

        var cart = await q
            .Select(c => new
            {
                c.Id,
                c.GuestId,
                Items = c.Items.Select(i => new CartItemDto(
                    i.Id,
                    i.ProductId,
                    i.VariantId,
                    i.Quantity,
                    i.UnitPrice,
                    i.UnitPrice * i.Quantity,
                    i.ProductName,
                    i.VariantSku,
                    i.VariantSize,
                    i.VariantColor,
                    i.ImageUrl
                )).ToList()
            })
            .SingleOrDefaultAsync(ct);

        if (cart is null)
            return new CartDto(Guid.Empty, storeSlug, guestId, 0, 0m, new List<CartItemDto>());

        var itemsCount = cart.Items.Sum(x => x.Quantity);
        var subtotal = cart.Items.Sum(x => x.LineTotal);

        return new CartDto(cart.Id, storeSlug, cart.GuestId, itemsCount, subtotal, cart.Items);
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        // SQL Server unique constraint violation numbers: 2601, 2627
        if (ex.InnerException is SqlException sql)
            return sql.Number is 2601 or 2627;

        return false;
    }
}
