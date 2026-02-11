using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Api.Data;
using OnlineShop.Api.Domain;
using OnlineShop.Api.Models.Catalog;
using OnlineShop.Api.Models.Common;

namespace OnlineShop.Api.Controllers;

[ApiController]
[Route("api/admin/catalog")]
public sealed class AdminCatalogController : ControllerBase
{
    private readonly OnlineShopDbContext _db;
    public AdminCatalogController(OnlineShopDbContext db) => _db = db;

    // ----------------------------
    // Requests
    // ----------------------------

    public record CreateProductCategoryRequest(Guid StoreId, string Name, int SortOrder = 0);

    public record CreateProductRequest(
        Guid StoreId,
        int? CategoryId,
        string Name,
        string? Description,
        decimal BasePrice,
        bool IsActive = true
    );

    public record CreateVariantRequest(
        string? Sku,
        string? Size,
        string? Color,
        decimal PriceDelta = 0m,
        int Stock = 0
    );

    public record AddImageRequest(string Url, int SortOrder = 0);

    public sealed class UpdateProductRequest
    {
        public string? Name { get; set; }
        public string? Description { get; set; }

        // Para setear categoría:
        public int? CategoryId { get; set; }

        // Para LIMPIAR categoría explícitamente (porque null/ausente se ven igual en JSON)
        public bool? ClearCategory { get; set; }

        public decimal? BasePrice { get; set; }
        public bool? IsActive { get; set; }
    }

    // ----------------------------
    // Categories (Admin)
    // ----------------------------

    [HttpPost("product-categories")]
    public async Task<IActionResult> CreateCategory([FromBody] CreateProductCategoryRequest req, CancellationToken ct)
    {
        if (req.StoreId == Guid.Empty) return BadRequest(new { error = "StoreId inválido." });

        var existsStore = await _db.Stores.AnyAsync(s => s.Id == req.StoreId, ct);
        if (!existsStore) return BadRequest(new { error = "StoreId inválido." });

        var name = (req.Name ?? string.Empty).Trim();
        if (name.Length < 2) return BadRequest(new { error = "Nombre muy corto." });

        // (Opcional pero recomendado) Evitar duplicados por tienda
        var dup = await _db.ProductCategories.AnyAsync(
            c => c.StoreId == req.StoreId && c.Name == name,
            ct
        );
        if (dup) return BadRequest(new { error = "Ya existe una categoría con ese nombre para la tienda." });

        var entity = new ProductCategory
        {
            StoreId = req.StoreId,
            Name = name,
            SortOrder = req.SortOrder
        };

        _db.ProductCategories.Add(entity);
        await _db.SaveChangesAsync(ct);

        return Ok(new { entity.Id, entity.StoreId, entity.Name, entity.SortOrder });
    }

    // (Opcional) Listar categorías por tienda (útil para admin)
    [HttpGet("stores/{storeId:guid}/product-categories")]
    public async Task<IActionResult> GetCategoriesByStore([FromRoute] Guid storeId, CancellationToken ct)
    {
        var items = await _db.ProductCategories
            .AsNoTracking()
            .Where(c => c.StoreId == storeId)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .Select(c => new { c.Id, c.StoreId, c.Name, c.SortOrder })
            .ToListAsync(ct);

        return Ok(items);
    }

    // ----------------------------
    // Products (Admin)
    // ----------------------------

    [HttpPost("products")]
    public async Task<IActionResult> CreateProduct([FromBody] CreateProductRequest req, CancellationToken ct)
    {
        if (req.StoreId == Guid.Empty) return BadRequest(new { error = "StoreId inválido." });

        var name = (req.Name ?? string.Empty).Trim();
        if (name.Length < 2) return BadRequest(new { error = "Nombre muy corto." });

        if (req.BasePrice < 0) return BadRequest(new { error = "BasePrice inválido." });

        var store = await _db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == req.StoreId, ct);
        if (store is null) return BadRequest(new { error = "StoreId inválido." });

        if (req.CategoryId.HasValue)
        {
            var catOk = await _db.ProductCategories
                .AsNoTracking()
                .AnyAsync(c => c.Id == req.CategoryId.Value && c.StoreId == req.StoreId, ct);

            if (!catOk) return BadRequest(new { error = "CategoryId no pertenece a la tienda." });
        }

        var entity = new Product
        {
            StoreId = req.StoreId,
            CategoryId = req.CategoryId,
            Name = name,
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            BasePrice = req.BasePrice,
            IsActive = req.IsActive
        };

        _db.Products.Add(entity);
        await _db.SaveChangesAsync(ct);

        return Ok(new { entity.Id, entity.StoreId, entity.CategoryId, entity.Name, entity.BasePrice, entity.IsActive });
    }

    // PATCH /api/admin/catalog/products/{productId}
    // Permite: name/description/basePrice/isActive y categoryId (validando que pertenezca a la tienda)
    // Para limpiar category: { "clearCategory": true }
    [HttpPatch("products/{productId:guid}")]
    public async Task<IActionResult> UpdateProduct(
        [FromRoute] Guid productId,
        [FromBody] UpdateProductRequest req,
        CancellationToken ct)
    {
        var product = await _db.Products.SingleOrDefaultAsync(p => p.Id == productId, ct);
        if (product == null) return NotFound();

        // Limpiar categoría explícitamente
        if (req.ClearCategory == true)
        {
            product.CategoryId = null;
        }
        else if (req.CategoryId.HasValue)
        {
            // Validar que exista y que sea de la misma tienda que el producto
            var catOk = await _db.ProductCategories
                .AsNoTracking()
                .AnyAsync(c => c.Id == req.CategoryId.Value && c.StoreId == product.StoreId, ct);

            if (!catOk)
                return BadRequest(new { error = "CategoryId inválido o no pertenece a la tienda del producto.", req.CategoryId });

            product.CategoryId = req.CategoryId.Value;
        }

        if (req.Name != null)
        {
            var name = req.Name.Trim();
            if (name.Length < 2) return BadRequest(new { error = "Nombre muy corto." });
            product.Name = name;
        }

        if (req.Description != null)
        {
            var desc = req.Description.Trim();
            product.Description = string.IsNullOrWhiteSpace(desc) ? null : desc;
        }

        if (req.BasePrice.HasValue)
        {
            if (req.BasePrice.Value < 0) return BadRequest(new { error = "BasePrice inválido." });
            product.BasePrice = req.BasePrice.Value;
        }

        if (req.IsActive.HasValue)
            product.IsActive = req.IsActive.Value;

        await _db.SaveChangesAsync(ct);
        return NoContent(); // 204
    }

    // (Opcional) Listado admin por slug (NO filtra Approved; admin debe ver todo)
    [HttpGet("stores/{storeSlug}/products")]
    public async Task<ActionResult<PagedResult<CatalogProductListItemDto>>> GetProductsByStoreSlug(
        [FromRoute] string storeSlug,
        [FromQuery] string? q,
        [FromQuery] int? categoryId,
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] string? sort,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : pageSize;
        pageSize = Math.Min(pageSize, 100);

        var store = await _db.Stores
            .AsNoTracking()
            .Where(s => s.Slug == storeSlug)
            .Select(s => new { s.Id })
            .SingleOrDefaultAsync(ct);

        if (store is null) return NotFound();

        var qText = (q ?? string.Empty).Trim();
        var hasQ = !string.IsNullOrWhiteSpace(qText);
        var like = $"%{qText}%";

        var products = _db.Products
            .AsNoTracking()
            .Where(p => p.StoreId == store.Id);

        if (categoryId.HasValue)
            products = products.Where(p => p.CategoryId == categoryId.Value);

        var projected = products.Select(p => new
        {
            p.Id,
            p.Name,
            p.Description,
            p.CreatedAt,
            HasVariants = p.Variants.Any(),

            MinPrice = p.BasePrice + (p.Variants.Select(v => (decimal?)v.PriceDelta).Min() ?? 0m),
            MaxPrice = p.BasePrice + (p.Variants.Select(v => (decimal?)v.PriceDelta).Max() ?? 0m),

            MainImageUrl = p.Images
                .OrderBy(i => i.SortOrder)
                .ThenBy(i => i.Id)
                .Select(i => i.Url)
                .FirstOrDefault()
        });

        if (hasQ)
        {
            projected = projected.Where(x =>
                EF.Functions.Like(x.Name, like) ||
                (x.Description != null && EF.Functions.Like(x.Description, like)));
        }

        if (minPrice.HasValue)
            projected = projected.Where(x => x.MaxPrice >= minPrice.Value);

        if (maxPrice.HasValue)
            projected = projected.Where(x => x.MinPrice <= maxPrice.Value);

        var sortKey = (sort ?? string.Empty).Trim().ToLowerInvariant();
        projected = sortKey switch
        {
            "price_asc" => projected.OrderBy(x => x.MinPrice).ThenBy(x => x.Name),
            "price_desc" => projected.OrderByDescending(x => x.MinPrice).ThenBy(x => x.Name),
            "relevance" when hasQ => projected
                .OrderBy(x => EF.Functions.Like(x.Name, qText + "%") ? 0 : (EF.Functions.Like(x.Name, like) ? 1 : 2))
                .ThenBy(x => x.Name),
            _ => projected.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Name)
        };

        var total = await projected.CountAsync(ct);

        var items = await projected
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new CatalogProductListItemDto
            {
                ProductId = x.Id,
                Name = x.Name,
                Summary = x.Description == null ? null
                    : (x.Description.Length <= 140 ? x.Description : x.Description.Substring(0, 140) + "..."),
                MinPrice = x.MinPrice,
                MaxPrice = x.MaxPrice,
                MainImageUrl = x.MainImageUrl,
                HasVariants = x.HasVariants
            })
            .ToListAsync(ct);

        return Ok(new PagedResult<CatalogProductListItemDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            Total = total
        });
    }

    // ----------------------------
    // Variants (Admin)
    // ----------------------------

    [HttpPost("products/{productId:guid}/variants")]
    public async Task<IActionResult> CreateVariant([FromRoute] Guid productId, [FromBody] CreateVariantRequest req, CancellationToken ct)
    {
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product is null) return NotFound();

        if (req.Stock < 0) return BadRequest(new { error = "Stock inválido." });

        var sku = string.IsNullOrWhiteSpace(req.Sku) ? null : req.Sku.Trim();

        // (Opcional) SKU único por producto si viene
        if (!string.IsNullOrWhiteSpace(sku))
        {
            var skuDup = await _db.ProductVariants.AnyAsync(v => v.ProductId == productId && v.Sku == sku, ct);
            if (skuDup) return BadRequest(new { error = "SKU duplicado para el mismo producto." });
        }

        var entity = new ProductVariant
        {
            ProductId = productId,
            Sku = sku,
            Size = string.IsNullOrWhiteSpace(req.Size) ? null : req.Size.Trim(),
            Color = string.IsNullOrWhiteSpace(req.Color) ? null : req.Color.Trim(),
            PriceDelta = req.PriceDelta,
            Stock = req.Stock
        };

        _db.ProductVariants.Add(entity);
        await _db.SaveChangesAsync(ct);

        return Ok(new { entity.Id, entity.ProductId, entity.Sku, entity.Size, entity.Color, entity.PriceDelta, entity.Stock });
    }

    // Esto te evita el “405” cuando querías ver variantes con GET
    [HttpGet("products/{productId:guid}/variants")]
    public async Task<IActionResult> GetVariants([FromRoute] Guid productId, CancellationToken ct)
    {
        var items = await _db.ProductVariants
            .AsNoTracking()
            .Where(v => v.ProductId == productId)
            .OrderBy(v => v.Color)
            .ThenBy(v => v.Size)
            .ThenBy(v => v.Sku)
            .Select(v => new { v.Id, v.ProductId, v.Sku, v.Size, v.Color, v.PriceDelta, v.Stock })
            .ToListAsync(ct);

        return Ok(items);
    }

    // ----------------------------
    // Images (Admin)
    // ----------------------------

    [HttpPost("products/{productId:guid}/images")]
    public async Task<IActionResult> AddImage([FromRoute] Guid productId, [FromBody] AddImageRequest req, CancellationToken ct)
    {
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product is null) return NotFound();

        var url = (req.Url ?? string.Empty).Trim();
        if (url.Length < 10) return BadRequest(new { error = "URL inválida." });

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            return BadRequest(new { error = "URL inválida." });

        if (req.SortOrder < 0) return BadRequest(new { error = "SortOrder inválido." });

        var entity = new ProductImage
        {
            ProductId = productId,
            Url = url,
            SortOrder = req.SortOrder
        };

        _db.ProductImages.Add(entity);
        await _db.SaveChangesAsync(ct);

        return Ok(new { entity.Id, entity.ProductId, entity.Url, entity.SortOrder });
    }

    // Esto te evita el “405” cuando probaste GET /images
    [HttpGet("products/{productId:guid}/images")]
    public async Task<IActionResult> GetImages([FromRoute] Guid productId, CancellationToken ct)
    {
        var items = await _db.ProductImages
            .AsNoTracking()
            .Where(i => i.ProductId == productId)
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Id)
            .Select(i => new { i.Id, i.ProductId, i.Url, i.SortOrder })
            .ToListAsync(ct);

        return Ok(items);
    }
}
