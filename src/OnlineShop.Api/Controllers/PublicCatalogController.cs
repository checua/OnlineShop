using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Api.Data;

namespace OnlineShop.Api.Controllers;

[ApiController]
public class PublicCatalogController : ControllerBase
{
    private readonly OnlineShopDbContext _db;
    public PublicCatalogController(OnlineShopDbContext db) => _db = db;

    // GET /api/stores/{slug}/products?query=&categoryId=&page=1&pageSize=20
    [HttpGet("api/stores/{slug}/products")]
    public async Task<IActionResult> GetProductsByStoreSlug(
        string slug,
        [FromQuery] string? query,
        [FromQuery] int? categoryId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 50 ? 20 : pageSize;

        var store = await _db.Stores.AsNoTracking()
            .Where(s => s.Status == "Approved" && s.Slug == slug)
            .Select(s => new { s.Id, s.Name, s.Slug })
            .FirstOrDefaultAsync();

        if (store is null) return NotFound();

        var q = _db.Products.AsNoTracking()
            .Where(p => p.StoreId == store.Id && p.IsActive);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            q = q.Where(p => p.Name.Contains(term));
        }

        if (categoryId.HasValue)
            q = q.Where(p => p.CategoryId == categoryId.Value);

        var total = await q.CountAsync();

        var items = await q
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.BasePrice,
                p.CategoryId,
                p.CreatedAt
            })
            .ToListAsync();

        return Ok(new { store, page, pageSize, total, items });
    }

    // GET /api/products/{id}
    [HttpGet("api/products/{id:guid}")]
    public async Task<IActionResult> GetProduct(Guid id)
    {
        var product = await _db.Products.AsNoTracking()
            .Where(p => p.Id == id && p.IsActive)
            .Select(p => new
            {
                p.Id,
                p.StoreId,
                p.CategoryId,
                p.Name,
                p.Description,
                p.BasePrice,
                p.CreatedAt,
                Variants = p.Variants.Select(v => new
                {
                    v.Id,
                    v.Sku,
                    v.Size,
                    v.Color,
                    v.PriceDelta,
                    v.Stock
                }).ToList(),
                Images = p.Images.OrderBy(i => i.SortOrder).Select(i => new
                {
                    i.Id,
                    i.Url,
                    i.SortOrder
                }).ToList()
            })
            .FirstOrDefaultAsync();

        return product is null ? NotFound() : Ok(product);
    }
}
