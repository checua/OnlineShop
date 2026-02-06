using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Api.Data;

namespace OnlineShop.Api.Controllers;

[ApiController]
[Route("api/store-categories")]
public class StoreCategoriesController : ControllerBase
{
    private readonly OnlineShopDbContext _db;
    public StoreCategoriesController(OnlineShopDbContext db) => _db = db;

    // GET /api/store-categories
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var items = await _db.StoreCategories.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync();

        return Ok(items);
    }
}
