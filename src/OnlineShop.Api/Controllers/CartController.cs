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

        await GetOrCreateActiveCart(store.Id, userId, guestId, ct);

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

        // Producto + datos útiles
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

        // Reglas variantes
        if (product.VariantCount > 0 && req.VariantId is null)
            return BadRequest(new { error = "VariantId requerido para este producto." });

        if (product.VariantCount == 0 && req.VariantId is not null)
            return BadRequest(new { error = "Este producto no maneja variantes. Envía variantId = null." });

        // Variante si aplica
        ProductVariant? variant = null;
        if (req.VariantId is not null)
        {
            variant = await _db.ProductVariants
                .AsNoTracking()
                .SingleOrDefaultAsync(v => v.Id == req.VariantId.Value && v.ProductId == product.Id, ct);

            if (variant is null)
                return BadRequest(new { error = "VariantId inválido (no existe o no pertenece al producto)." });
        }

        // Retry robusto
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            try
            {
                _db.ChangeTracker.Clear();

                var now = DateTime.UtcNow;
                var unitPrice = product.BasePrice + (variant?.PriceDelta ?? 0m);

                var cart = await GetOrCreateActiveCart(store.Id, userId, guestId, ct);

                // UP SERT del item por (CartId, ProductId, VariantId) directamente en DB
                var item = await _db.CartItems
                    .SingleOrDefaultAsync(i =>
                        i.CartId == cart.Id &&
                        i.ProductId == product.Id &&
                        i.VariantId == req.VariantId, ct);

                if (item is null)
                {
                    item = new CartItem
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

                    _db.CartItems.Add(item);
                }
                else
                {
                    item.Quantity += req.Quantity;
                    item.UnitPrice = unitPrice;
                    item.ProductName = product.Name;
                    item.VariantSku = variant?.Sku;
                    item.VariantSize = variant?.Size;
                    item.VariantColor = variant?.Color;
                    item.ImageUrl = product.MainImageUrl;
                    item.UpdatedAt = now;
                }

                cart.UpdatedAt = now;

                await _db.SaveChangesAsync(ct);

                var dto = await BuildCartDto(store.Id, store.Slug, userId, guestId, ct);
                return Ok(dto);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                Console.WriteLine($"[CART] Concurrency attempt={attempt}: {ex.Message}");
                await Backoff(attempt, ct);
                continue;
            }
            catch (DbUpdateException ex)
            {
                var n = TryGetSqlNumber(ex);
                Console.WriteLine($"[CART] DbUpdate attempt={attempt} sql={n}: {ex.Message}");

                if (n == 547)
                    return BadRequest(new { error = "Producto/variante inválidos o no pertenecen a la tienda." });

                if (n is not null && (IsTransientSql(n.Value) || IsDeadlock(n.Value) || IsUnique(n.Value)))
                {
                    await Backoff(attempt, ct);
                    continue;
                }

                return Conflict(new { error = "Conflicto de concurrencia del carrito. Reintenta." });
            }
        }

        return Conflict(new { error = "Conflicto de concurrencia del carrito. Reintenta." });
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

        for (var attempt = 1; attempt <= 6; attempt++)
        {
            try
            {
                _db.ChangeTracker.Clear();

                var cart = await FindActiveCartTracked(store.Id, userId, guestId, includeItems: false, ct);
                if (cart is null) return Ok(await BuildCartDto(store.Id, store.Slug, userId, guestId, ct));

                var item = await _db.CartItems
                    .SingleOrDefaultAsync(i => i.Id == itemId && i.CartId == cart.Id, ct);

                if (item is null) return NotFound(new { error = "Item no encontrado en el carrito." });

                var now = DateTime.UtcNow;

                if (req.Quantity == 0)
                {
                    _db.CartItems.Remove(item);
                }
                else
                {
                    item.Quantity = req.Quantity;
                    item.UpdatedAt = now;
                }

                cart.UpdatedAt = now;

                await _db.SaveChangesAsync(ct);
                return Ok(await BuildCartDto(store.Id, store.Slug, userId, guestId, ct));
            }
            catch (DbUpdateConcurrencyException)
            {
                await Backoff(attempt, ct);
                continue;
            }
            catch (DbUpdateException ex)
            {
                var n = TryGetSqlNumber(ex);
                if (n is not null && (IsTransientSql(n.Value) || IsDeadlock(n.Value) || IsUnique(n.Value)))
                {
                    await Backoff(attempt, ct);
                    continue;
                }
                return Conflict(new { error = "Conflicto de concurrencia del carrito. Reintenta." });
            }
        }

        return Conflict(new { error = "Conflicto de concurrencia del carrito. Reintenta." });
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

        for (var attempt = 1; attempt <= 6; attempt++)
        {
            try
            {
                _db.ChangeTracker.Clear();

                var cart = await FindActiveCartTracked(store.Id, userId, guestId, includeItems: false, ct);
                if (cart is null) return Ok(await BuildCartDto(store.Id, store.Slug, userId, guestId, ct));

                var item = await _db.CartItems
                    .SingleOrDefaultAsync(i => i.Id == itemId && i.CartId == cart.Id, ct);

                if (item is null) return NotFound(new { error = "Item no encontrado." });

                _db.CartItems.Remove(item);
                cart.UpdatedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync(ct);
                return Ok(await BuildCartDto(store.Id, store.Slug, userId, guestId, ct));
            }
            catch (DbUpdateConcurrencyException)
            {
                await Backoff(attempt, ct);
                continue;
            }
            catch (DbUpdateException ex)
            {
                var n = TryGetSqlNumber(ex);
                if (n is not null && (IsTransientSql(n.Value) || IsDeadlock(n.Value) || IsUnique(n.Value)))
                {
                    await Backoff(attempt, ct);
                    continue;
                }
                return Conflict(new { error = "Conflicto de concurrencia del carrito. Reintenta." });
            }
        }

        return Conflict(new { error = "Conflicto de concurrencia del carrito. Reintenta." });
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
        // ====== 0) merge/claim solo si viene userId + guestId ======
        if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(guestId))
        {
            var userCart = await FindActiveCartTracked(storeId, userId, null, includeItems: true, ct);
            var guestCart = await FindActiveCartTracked(storeId, null, guestId, includeItems: true, ct);

            if (userCart is null && guestCart is not null)
            {
                guestCart.UserId = userId;
                guestCart.GuestId = null;
                guestCart.UpdatedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync(ct);
                return guestCart;
            }

            if (userCart is not null && guestCart is not null && userCart.Id != guestCart.Id)
            {
                var now = DateTime.UtcNow;

                foreach (var src in guestCart.Items.ToList())
                {
                    var dst = userCart.Items.FirstOrDefault(i => i.ProductId == src.ProductId && i.VariantId == src.VariantId);
                    if (dst is null)
                    {
                        src.CartId = userCart.Id;
                        src.UpdatedAt = now;
                        userCart.Items.Add(src);
                    }
                    else
                    {
                        dst.Quantity += src.Quantity;
                        dst.UpdatedAt = now;

                        guestCart.Items.Remove(src);
                        _db.CartItems.Remove(src);
                    }
                }

                guestCart.Status = CartStatus.Merged;
                guestCart.UpdatedAt = now;
                userCart.UpdatedAt = now;

                await _db.SaveChangesAsync(ct);
                return userCart;
            }

            if (userCart is not null) return userCart;
        }

        // ====== 1) intenta cargar existente (sin items) ======
        var existing = await FindActiveCartTracked(storeId, userId, guestId, includeItems: false, ct);
        if (existing is not null) return existing;

        // ====== 2) crea nuevo (retry si choca unique por race) ======
        var now2 = DateTime.UtcNow;
        var cart = new Cart
        {
            Id = Guid.NewGuid(),
            StoreId = storeId,
            UserId = userId,
            GuestId = guestId,
            Status = CartStatus.Active,
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
            var retry = await FindActiveCartTracked(storeId, userId, guestId, includeItems: false, ct);
            if (retry is not null) return retry;
            throw;
        }
    }

    private async Task<Cart?> FindActiveCartTracked(Guid storeId, string? userId, string? guestId, bool includeItems, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId) && string.IsNullOrWhiteSpace(guestId))
            return null;

        IQueryable<Cart> q = _db.Carts
            .Where(c => c.StoreId == storeId && c.Status == CartStatus.Active);

        if (includeItems)
            q = q.Include(c => c.Items);

        if (!string.IsNullOrWhiteSpace(userId))
            q = q.Where(c => c.UserId == userId);
        else
            q = q.Where(c => c.GuestId == guestId);

        return await q.SingleOrDefaultAsync(ct);
    }

    private async Task<CartDto> BuildCartDto(Guid storeId, string storeSlug, string? userId, string? guestId, CancellationToken ct)
    {
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
        if (ex.InnerException is SqlException sql)
            return sql.Number is 2601 or 2627;
        return false;
    }

    private static int? TryGetSqlNumber(Exception ex)
    {
        while (ex is not null)
        {
            if (ex is SqlException sql) return sql.Number;
            ex = ex.InnerException!;
        }
        return null;
    }

    private static bool IsUnique(int n) => n is 2601 or 2627;
    private static bool IsDeadlock(int n) => n == 1205;
    private static bool IsTransientSql(int n) => n is 40613 or 40197 or 40501 or 10928 or 10929;

    private static Task Backoff(int attempt, CancellationToken ct)
    {
        var ms = Math.Min(1500, 200 * attempt);
        return Task.Delay(ms, ct);
    }
}