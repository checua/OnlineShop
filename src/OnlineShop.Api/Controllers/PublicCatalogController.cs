using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Api.Data;
using OnlineShop.Api.Domain;
using OnlineShop.Api.Models.Catalog;
using OnlineShop.Api.Models.Common;

namespace OnlineShop.Api.Controllers;

[ApiController]
[Route("api/catalog")]
public sealed class PublicCatalogController : ControllerBase
{
    private readonly OnlineShopDbContext _db;

    public PublicCatalogController(OnlineShopDbContext db)
    {
        _db = db;
    }

    // GET /api/catalog/{storeSlug}/products?q=&categoryId=&minPrice=&maxPrice=&sort=&page=&pageSize=
    [HttpGet("{storeSlug}/products")]
    public async Task<ActionResult<PagedResult<CatalogProductListItemDto>>> GetProducts(
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

        // Store aprobada
        var store = await _db.Stores
            .AsNoTracking()
            .Where(s => s.Slug == storeSlug && s.Status == "Approved")
            .Select(s => new { s.Id })
            .SingleOrDefaultAsync(ct);

        if (store == null) return NotFound();

        var qText = (q ?? string.Empty).Trim();
        var hasQ = !string.IsNullOrWhiteSpace(qText);
        var like = $"%{qText}%";

        var products = _db.Products
            .AsNoTracking()
            .Where(p => p.StoreId == store.Id && p.IsActive);

        if (categoryId.HasValue)
            products = products.Where(p => p.CategoryId == categoryId.Value);

        var projected = products.Select(p => new
        {
            p.Id,
            p.Name,
            p.Description,
            p.CreatedAt,
            HasVariants = p.Variants.Any(),

            // Precio real: BasePrice + min/max PriceDelta (si no hay variants => BasePrice)
            MinPrice = p.BasePrice + (p.Variants.Select(v => (decimal?)v.PriceDelta).Min() ?? 0m),
            MaxPrice = p.BasePrice + (p.Variants.Select(v => (decimal?)v.PriceDelta).Max() ?? 0m),

            MainImageUrl = p.Images
                .OrderBy(i => i.SortOrder)
                .ThenBy(i => i.Id)
                .Select(i => i.Url)
                .FirstOrDefault()
        });

        // Filtro texto
        if (hasQ)
        {
            projected = projected.Where(x =>
                EF.Functions.Like(x.Name, like) ||
                (x.Description != null && EF.Functions.Like(x.Description, like)));
        }

        // Filtro precio (overlap)
        if (minPrice.HasValue)
            projected = projected.Where(x => x.MaxPrice >= minPrice.Value);

        if (maxPrice.HasValue)
            projected = projected.Where(x => x.MinPrice <= maxPrice.Value);

        // Sorting
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
                Summary = x.Description == null
                    ? null
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

    // GET /api/catalog/{storeSlug}/categories
    [HttpGet("{storeSlug}/categories")]
    public async Task<ActionResult<IReadOnlyList<CatalogCategoryDto>>> GetCategories(
        [FromRoute] string storeSlug,
        CancellationToken ct = default)
    {
        var store = await _db.Stores
            .AsNoTracking()
            .Where(s => s.Slug == storeSlug && s.Status == "Approved")
            .Select(s => new { s.Id })
            .SingleOrDefaultAsync(ct);

        if (store == null) return NotFound();

        var productCounts = _db.Products
            .AsNoTracking()
            .Where(p => p.StoreId == store.Id && p.IsActive && p.CategoryId != null)
            .GroupBy(p => p.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() });

        var categories = _db.ProductCategories.AsNoTracking();

        // Si ProductCategory tiene StoreId, lo filtramos para no mezclar categorías de otras tiendas
        var storeIdProp = typeof(ProductCategory).GetProperty("StoreId");
        if (storeIdProp != null)
        {
            if (storeIdProp.PropertyType == typeof(Guid))
            {
                categories = categories.Where(c => EF.Property<Guid>(c, "StoreId") == store.Id);
            }
            else if (storeIdProp.PropertyType == typeof(Guid?))
            {
                categories = categories.Where(c => EF.Property<Guid?>(c, "StoreId") == store.Id);
            }
        }

        var result = await categories
            .GroupJoin(
                productCounts,
                c => (int?)c.Id,
                pc => pc.CategoryId,
                (c, pcs) => new CatalogCategoryDto
                {
                    CategoryId = c.Id,
                    Name = c.Name,
                    SortOrder = c.SortOrder,
                    ProductCount = pcs.Select(x => x.Count).FirstOrDefault()
                })
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);

        return Ok(result);
    }

    // GET /api/catalog/{storeSlug}/products/{productId}
    [HttpGet("{storeSlug}/products/{productId:guid}")]
    public async Task<ActionResult<CatalogProductDetailDto>> GetProductDetail(
        [FromRoute] string storeSlug,
        [FromRoute] Guid productId,
        CancellationToken ct = default)
    {
        var store = await _db.Stores
            .AsNoTracking()
            .Where(s => s.Slug == storeSlug && s.Status == "Approved")
            .Select(s => new { s.Id })
            .SingleOrDefaultAsync(ct);

        if (store == null) return NotFound();

        var dto = await _db.Products
            .AsNoTracking()
            .Where(p => p.StoreId == store.Id && p.IsActive && p.Id == productId)
            .Select(p => new CatalogProductDetailDto
            {
                ProductId = p.Id,
                Name = p.Name,
                Description = p.Description,

                CategoryId = p.CategoryId,
                CategoryName = p.Category != null ? p.Category.Name : null,

                BasePrice = p.BasePrice,
                MinPrice = p.BasePrice + (p.Variants.Select(v => (decimal?)v.PriceDelta).Min() ?? 0m),
                MaxPrice = p.BasePrice + (p.Variants.Select(v => (decimal?)v.PriceDelta).Max() ?? 0m),

                CreatedAt = p.CreatedAt,

                Images = p.Images
                    .OrderBy(i => i.SortOrder)
                    .ThenBy(i => i.Id)
                    .Select(i => new CatalogProductImageDto
                    {
                        Url = i.Url,
                        SortOrder = i.SortOrder
                    })
                    .ToList(),

                Variants = p.Variants
                    .OrderBy(v => v.Color)
                    .ThenBy(v => v.Size)
                    .Select(v => new CatalogProductVariantDto
                    {
                        VariantId = v.Id,
                        Sku = v.Sku,
                        Size = v.Size,
                        Color = v.Color,
                        PriceDelta = v.PriceDelta,
                        Price = p.BasePrice + v.PriceDelta,
                        Stock = v.Stock
                    })
                    .ToList()
            })
            .SingleOrDefaultAsync(ct);

        if (dto == null) return NotFound();
        return Ok(dto);
    }
}
