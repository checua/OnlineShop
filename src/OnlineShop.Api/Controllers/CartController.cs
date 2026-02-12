using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Api.Data;
using OnlineShop.Api.Domain;
using OnlineShop.Api.Models.Cart;

namespace OnlineShop.Api.Controllers;

[ApiController]
[Route("api/cart")]
public sealed class CartController : ControllerBase
{
    private const string GuestHeader = "X-Guest-Id";
    private readonly OnlineShopDbContext _db;

    public CartController(OnlineShopDbContext db) => _db = db;

    // GET /api/cart/{storeSlug}
    [HttpGet("{storeSlug}")]
    public async Task<ActionResult<CartDto>> GetCart([FromRoute] string storeSlug, CancellationToken ct)
    {
        var store = await GetApprovedStoreOr404(storeSlug, ct);

        var (userId, guestId) = EnsureActor();
        var cart = await GetOrCreateActiveCart(store.Id, userId, guestId, ct);

        return Ok(ToDto(cart, storeSlug, guestId));
    }

    // POST /api/cart/{storeSlug}/items
    [HttpPost("{storeSlug}/items")]
    public async Task<ActionResult<CartDto>> AddItem([FromRoute] string storeSlug, [FromBody] AddCartItemRequest req, CancellationToken ct)
    {
        if (req.Quantity < 1) return BadRequest(new { error = "Quantity inválida." });

        var store = await GetApprovedStoreOr404(storeSlug, ct);
        var (userId, guestId) = EnsureActor();
        var cart = await GetOrCreateActiveCart(store.Id, userId, guestId, ct);

        var product = await _db.Products
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .SingleOrDefaultAsync(p => p.Id == req.ProductId && p.StoreId == store.Id && p.IsActive, ct);

        if (product == null) return NotFound(new { error = "Producto no encontrado." });

        // Si tiene variantes, requiere VariantId
        ProductVariant? variant = null;
        var hasVariants = product.Variants.Any();

        if (hasVariants)
        {
            if (req.VariantId == null) return BadRequest(new { error = "VariantId requerido para este producto." });

            variant = product.Variants.SingleOrDefault(v => v.Id == req.VariantId.Value);
            if (variant == null) return BadRequest(new { error = "VariantId inválido." });

            // Stock: si Stock <= 0, no se puede vender (regla simple)
            if (variant.Stock <= 0) return BadRequest(new { error = "Sin stock.", stock = variant.Stock });
            if (req.Quantity > variant.Stock) return BadRequest(new { error = "Stock insuficiente.", stock = variant.Stock });
        }
        else
        {
            req.VariantId = null;
        }

        var unitPrice = product.BasePrice + (variant?.PriceDelta ?? 0m);

        var mainImage = product.Images
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Id)
            .Select(i => i.Url)
            .FirstOrDefault();

        // Merge por ProductId + VariantId
        var existing = cart.Items.FirstOrDefault(i => i.ProductId == product.Id && i.VariantId == req.VariantId);

        if (existing != null)
        {
            var newQty = existing.Quantity + req.Quantity;

            if (variant != null && newQty > variant.Stock)
                return BadRequest(new { error = "Stock insuficiente.", stock = variant.Stock, requested = newQty });

            existing.Quantity = newQty;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            cart.Items.Add(new CartItem
            {
                CartId = cart.Id,
                ProductId = product.Id,
                VariantId = req.VariantId,
                Quantity = req.Quantity,
                UnitPrice = unitPrice,
                ProductName = product.Name,
                VariantSku = variant?.Sku,
                VariantSize = variant?.Size,
                VariantColor = variant?.Color,
                ImageUrl = mainImage,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        cart.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(ToDto(cart, storeSlug, guestId));
    }

    // PATCH /api/cart/{storeSlug}/items/{itemId}
    [HttpPatch("{storeSlug}/items/{itemId:guid}")]
    public async Task<ActionResult<CartDto>> UpdateItem([FromRoute] string storeSlug, [FromRoute] Guid itemId, [FromBody] UpdateCartItemRequest req, CancellationToken ct)
    {
        if (req.Quantity < 0) return BadRequest(new { error = "Quantity inválida." });

        var store = await GetApprovedStoreOr404(storeSlug, ct);
        var (userId, guestId) = EnsureActor();
        var cart = await GetOrCreateActiveCart(store.Id, userId, guestId, ct);

        var item = cart.Items.SingleOrDefault(i => i.Id == itemId);
        if (item == null) return NotFound();

        if (req.Quantity == 0)
        {
            cart.Items.Remove(item);
        }
        else
        {
            // validar stock si tiene variante
            if (item.VariantId != null)
            {
                var variant = await _db.ProductVariants
                    .AsNoTracking()
                    .SingleOrDefaultAsync(v => v.Id == item.VariantId.Value, ct);

                if (variant != null)
                {
                    if (variant.Stock <= 0) return BadRequest(new { error = "Sin stock.", stock = variant.Stock });
                    if (req.Quantity > variant.Stock) return BadRequest(new { error = "Stock insuficiente.", stock = variant.Stock });
                }
            }

            item.Quantity = req.Quantity;
            item.UpdatedAt = DateTime.UtcNow;
        }

        cart.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(ToDto(cart, storeSlug, guestId));
    }

    // DELETE /api/cart/{storeSlug}/items/{itemId}
    [HttpDelete("{storeSlug}/items/{itemId:guid}")]
    public async Task<ActionResult<CartDto>> DeleteItem([FromRoute] string storeSlug, [FromRoute] Guid itemId, CancellationToken ct)
    {
        var store = await GetApprovedStoreOr404(storeSlug, ct);
        var (userId, guestId) = EnsureActor();
        var cart = await GetOrCreateActiveCart(store.Id, userId, guestId, ct);

        var item = cart.Items.SingleOrDefault(i => i.Id == itemId);
        if (item == null) return NotFound();

        cart.Items.Remove(item);
        cart.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(ToDto(cart, storeSlug, guestId));
    }

    // POST /api/cart/{storeSlug}/merge  (guest -> user)
    [Authorize]
    [HttpPost("{storeSlug}/merge")]
    public async Task<ActionResult<CartDto>> MergeGuestToUser([FromRoute] string storeSlug, CancellationToken ct)
    {
        var store = await GetApprovedStoreOr404(storeSlug, ct);

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        var guestId = Request.Headers[GuestHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(guestId))
            return BadRequest(new { error = $"Falta header {GuestHeader}." });

        var guestCart = await _db.Carts
            .Include(c => c.Items)
            .SingleOrDefaultAsync(c => c.StoreId == store.Id && c.GuestId == guestId && c.Status == CartStatus.Active, ct);

        // Si no hay guest cart, regresa el del usuario
        if (guestCart == null)
        {
            var userCartOnly = await GetOrCreateActiveCart(store.Id, userId, null, ct);
            return Ok(ToDto(userCartOnly, storeSlug, null));
        }

        var userCart = await _db.Carts
            .Include(c => c.Items)
            .SingleOrDefaultAsync(c => c.StoreId == store.Id && c.UserId == userId && c.Status == CartStatus.Active, ct);

        // Si el usuario no tenía carrito, convierte guest->user
        if (userCart == null)
        {
            guestCart.UserId = userId;
            guestCart.GuestId = null;
            guestCart.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return Ok(ToDto(guestCart, storeSlug, null));
        }

        // Merge items
        foreach (var gi in guestCart.Items.ToList())
        {
            var existing = userCart.Items.FirstOrDefault(ui => ui.ProductId == gi.ProductId && ui.VariantId == gi.VariantId);
            if (existing != null)
            {
                existing.Quantity += gi.Quantity;
                existing.UpdatedAt = DateTime.UtcNow;
                _db.CartItems.Remove(gi);
            }
            else
            {
                gi.CartId = userCart.Id;
                gi.Cart = userCart;
                userCart.Items.Add(gi);
            }
        }

        guestCart.Status = CartStatus.Merged;
        guestCart.UpdatedAt = DateTime.UtcNow;
        userCart.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return Ok(ToDto(userCart, storeSlug, null));
    }

    // ---------------- Helpers ----------------

    private (string? userId, string? guestId) EnsureActor()
    {
        if (User?.Identity?.IsAuthenticated == true)
        {
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return (uid, null);
        }

        var guestId = Request.Headers[GuestHeader].FirstOrDefault()
                      ?? Request.Query["guestId"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(guestId))
        {
            guestId = Guid.NewGuid().ToString("N");
            Response.Headers[GuestHeader] = guestId; // para que el cliente lo guarde
        }

        return (null, guestId);
    }

    private async Task<Store> GetApprovedStoreOr404(string storeSlug, CancellationToken ct)
    {
        var store = await _db.Stores
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Slug == storeSlug && s.Status == "Approved", ct);

        if (store == null) throw new InvalidOperationException("STORE_NOT_FOUND_OR_NOT_APPROVED");
        return store;
    }

    private async Task<Cart> GetOrCreateActiveCart(Guid storeId, string? userId, string? guestId, CancellationToken ct)
    {
        var query = _db.Carts
            .Include(c => c.Items)
            .Where(c => c.StoreId == storeId && c.Status == CartStatus.Active);

        Cart? cart;
        if (!string.IsNullOrWhiteSpace(userId))
            cart = await query.SingleOrDefaultAsync(c => c.UserId == userId, ct);
        else
            cart = await query.SingleOrDefaultAsync(c => c.GuestId == guestId, ct);

        if (cart != null) return cart;

        cart = new Cart
        {
            StoreId = storeId,
            UserId = userId,
            GuestId = guestId,
            Status = CartStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Carts.Add(cart);
        await _db.SaveChangesAsync(ct);

        return await _db.Carts.Include(c => c.Items).SingleAsync(c => c.Id == cart.Id, ct);
    }

    private static CartDto ToDto(Cart cart, string storeSlug, string? guestId)
    {
        var items = cart.Items
            .OrderBy(i => i.CreatedAt)
            .Select(i => new CartItemDto
            {
                ItemId = i.Id,
                ProductId = i.ProductId,
                VariantId = i.VariantId,
                Name = i.ProductName,
                Sku = i.VariantSku,
                Size = i.VariantSize,
                Color = i.VariantColor,
                ImageUrl = i.ImageUrl,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                LineTotal = i.UnitPrice * i.Quantity
            })
            .ToList();

        return new CartDto
        {
            CartId = cart.Id,
            StoreSlug = storeSlug,
            GuestId = guestId,
            Items = items,
            ItemsCount = items.Sum(x => x.Quantity),
            Subtotal = items.Sum(x => x.LineTotal)
        };
    }
}
