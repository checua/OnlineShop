using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Api.Data;

namespace OnlineShop.Api.Controllers;

[ApiController]
[Route("api/store-categories")]
public sealed class StoreCategoriesController : ControllerBase
{
    private readonly OnlineShopDbContext _db;

    public StoreCategoriesController(OnlineShopDbContext db)
    {
        _db = db;
    }

    // GET /api/store-categories?withCounts=true&q=algo
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StoreCategoryDto>>> Get(
        [FromQuery] bool withCounts = false,
        [FromQuery] string? q = null,
        CancellationToken ct = default)
    {
        var categories = _db.StoreCategories.AsNoTracking();

        // Búsqueda opcional por nombre
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = $"%{q.Trim()}%";
            categories = categories.Where(c => EF.Functions.Like(c.Name, like));
        }

        if (!withCounts)
        {
            var list = await categories
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .Select(c => new StoreCategoryDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    SortOrder = c.SortOrder,
                    ApprovedStoreCount = null
                })
                .ToListAsync(ct);

            return Ok(list);
        }

        // Conteo de tiendas Approved por CategoryId (Store.CategoryId)
        var approvedCounts = _db.Stores
            .AsNoTracking()
            .Where(s => s.CategoryId != null && s.Status == "Approved")
            .GroupBy(s => s.CategoryId!.Value)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() });

        var result = await categories
            .GroupJoin(
                approvedCounts,
                c => c.Id,
                x => x.CategoryId,
                (c, grp) => new StoreCategoryDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    SortOrder = c.SortOrder,
                    ApprovedStoreCount = grp.Select(x => x.Count).FirstOrDefault()
                })
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);

        return Ok(result);
    }

    public sealed class StoreCategoryDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int SortOrder { get; set; }
        public int? ApprovedStoreCount { get; set; } // solo cuando withCounts=true
    }
}
