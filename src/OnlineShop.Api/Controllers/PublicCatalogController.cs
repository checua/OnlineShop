using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Api.Data;
using OnlineShop.Api.Models.Common;
using OnlineShop.Api.Models.Catalog;

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

    [HttpGet("{storeSlug}/products")]
    public async Task<ActionResult<PagedResult<CatalogProductListItemDto>>> GetProducts(
        [FromRoute] string storeSlug,
        [FromQuery] string? q,
        [FromQuery] int? categoryId,      // por ahora lo ignoramos (ver nota abajo)
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

        // 1) Store por slug (SIN IsApproved porque tu Store no lo trae con ese nombre)
        var store = await _db.Stores
            .AsNoTracking()
            .Where(s => s.Slug == storeSlug)
            .Select(s => new { s.Id })
            .SingleOrDefaultAsync(ct);

        if (store is null) return NotFound();

        var qText = (q ?? string.Empty).Trim();
        var hasQ = !string.IsNullOrWhiteSpace(qText);
        var like = $"%{qText}%";

        // Detectar el campo de precio en ProductVariant
        // Candidatos típicos: Price, UnitPrice, Amount, Value
        var variantPriceName = typeof(OnlineShop.Api.Domain.ProductVariant)
            .GetProperties()
            .FirstOrDefault(p =>
                (p.PropertyType == typeof(decimal) || p.PropertyType == typeof(decimal?)) &&
                new[] { "Price", "UnitPrice", "Amount", "Value" }.Contains(p.Name))
            ?.Name;

        // 2) Query base
        var products = _db.Products
            .AsNoTracking()
            .Where(p => p.StoreId == store.Id);

        // Nota: categoryId -> lo activamos cuando me confirmes el nombre real del FK en Product
        // (ahorita tu Product NO tiene ProductCategoryId, por eso te tronaba)

        // 3) Proyección
        var projected = products.Select(p => new
        {
            p.Id,
            p.Name,
            p.Description,
            p.CreatedAt,
            HasVariants = p.Variants.Any(),
            MainImageUrl = p.Images
                .OrderBy(i => i.SortOrder)
                .ThenBy(i => i.Id)
                .Select(i => i.Url)
                .FirstOrDefault(),

            MinPrice = variantPriceName == null
                ? 0m
                : (p.Variants.Select(v => (decimal?)EF.Property<decimal>(v, variantPriceName)).Min() ?? 0m),

            MaxPrice = variantPriceName == null
                ? 0m
                : (p.Variants.Select(v => (decimal?)EF.Property<decimal>(v, variantPriceName)).Max() ?? 0m),
        });

        // 4) Filtros texto
        if (hasQ)
        {
            projected = projected.Where(x =>
                EF.Functions.Like(x.Name, like) ||
                (x.Description != null && EF.Functions.Like(x.Description, like)));
        }

        // 5) Filtros precio (solo si detectamos campo)
        if (variantPriceName != null)
        {
            if (minPrice.HasValue)
                projected = projected.Where(x => x.MaxPrice >= minPrice.Value);

            if (maxPrice.HasValue)
                projected = projected.Where(x => x.MinPrice <= maxPrice.Value);
        }

        // 6) Sorting
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

        // 7) Page
        var total = await projected.CountAsync(ct);

        var items = await projected
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new CatalogProductListItemDto
            {
                ProductId = x.Id,
                Name = x.Name,
                Summary = x.Description == null ? null : (x.Description.Length <= 140 ? x.Description : x.Description.Substring(0, 140) + "..."),
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
}
