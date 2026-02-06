using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Api.Data;

namespace OnlineShop.Api.Controllers;

[ApiController]
[Route("api/stores")]
public class StoresController : ControllerBase
{
    private readonly OnlineShopDbContext _db;

    public StoresController(OnlineShopDbContext db) => _db = db;

    // GET /api/stores?query=zap&categoryId=2&page=1&pageSize=20
    [HttpGet]
    public async Task<IActionResult> GetStores(
        [FromQuery] string? query,
        [FromQuery] int? categoryId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 50 ? 20 : pageSize;

        var q = _db.Stores.AsNoTracking()
            .Where(s => s.Status == "Approved");

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            q = q.Where(s => s.Name.Contains(term) || s.Slug.Contains(term));
        }

        if (categoryId.HasValue)
            q = q.Where(s => s.CategoryId == categoryId.Value);

        var total = await q.CountAsync();

        var items = await q
            .OrderBy(s => s.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Slug,
                s.Status,
                s.CategoryId
            })
            .ToListAsync();

        return Ok(new { page, pageSize, total, items });
    }

    // GET /api/stores/{slug}
    [HttpGet("{slug}")]
    public async Task<IActionResult> GetBySlug(string slug)
    {
        var store = await _db.Stores.AsNoTracking()
            .Where(s => s.Status == "Approved" && s.Slug == slug)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Slug,
                s.Status,
                s.CategoryId,
                CategoryName = s.Category != null ? s.Category.Name : null
            })
            .FirstOrDefaultAsync();

        return store is null ? NotFound() : Ok(store);
    }
}
