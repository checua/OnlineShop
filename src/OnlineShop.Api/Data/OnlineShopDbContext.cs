using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Api.Data;
using OnlineShop.Api.Domain;

namespace OnlineShop.Api.Controllers;

[ApiController]
[Route("api/store-categories")]
public class StoreCategoriesController : ControllerBase
{
    private readonly OnlineShopDbContext _db;

    public StoreCategoriesController(OnlineShopDbContext db)
    {
        _db = db;
    }

    // GET /api/store-categories?withCounts=true
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] bool withCounts = false)
    {
        var cats = await _db.StoreCategories
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .Select(c => new StoreCategoryDto
            {
                Id = c.Id,
                Name = c.Name,
                SortOrder = c.SortOrder
            })
            .ToListAsync();

        if (!withCounts)
            return Ok(cats);

        // Conteo de tiendas "Approved" por CategoryId
        var counts = await _db.Stores
            .AsNoTracking()
            .Where(s => s.CategoryId != null && s.Status == "Approved")
            .GroupBy(s => s.CategoryId!.Value)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count);

        foreach (var c in cats)
        {
            c.ApprovedStoreCount = counts.TryGetValue(c.Id, out var n) ? n : 0;
        }

        return Ok(cats);
    }

    public class StoreCategoryDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int SortOrder { get; set; }
        public int? ApprovedStoreCount { get; set; } // solo cuando withCounts=true
    }
}
