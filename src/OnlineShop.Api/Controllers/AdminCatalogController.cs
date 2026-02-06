using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Api.Data;
using OnlineShop.Api.Domain;

namespace OnlineShop.Api.Controllers;

[ApiController]
[Route("api/admin/catalog")]
public class AdminCatalogController : ControllerBase
{
    private readonly OnlineShopDbContext _db;
    public AdminCatalogController(OnlineShopDbContext db) => _db = db;

    public record CreateProductCategoryRequest(Guid StoreId, string Name, int SortOrder = 0);

    [HttpPost("product-categories")]
    public async Task<IActionResult> CreateCategory([FromBody] CreateProductCategoryRequest req)
    {
        var existsStore = await _db.Stores.AnyAsync(s => s.Id == req.StoreId);
        if (!existsStore) return BadRequest(new { error = "StoreId inválido." });

        var name = req.Name.Trim();
        if (name.Length < 2) return BadRequest(new { error = "Nombre muy corto." });

        var entity = new ProductCategory
        {
            StoreId = req.StoreId,
            Name = name,
            SortOrder = req.SortOrder
        };

        _db.ProductCategories.Add(entity);
        await _db.SaveChangesAsync();

        return Ok(new { entity.Id, entity.StoreId, entity.Name, entity.SortOrder });
    }

    public record CreateProductRequest(
        Guid StoreId,
        int? CategoryId,
        string Name,
        string? Description,
        decimal BasePrice,
        bool IsActive = true
    );

    [HttpPost("products")]
    public async Task<IActionResult> CreateProduct([FromBody] CreateProductRequest req)
    {
        if (req.BasePrice < 0) return BadRequest(new { error = "BasePrice inválido." });

        var store = await _db.Stores.FirstOrDefaultAsync(s => s.Id == req.StoreId);
        if (store is null) return BadRequest(new { error = "StoreId inválido." });

        if (req.CategoryId.HasValue)
        {
            var catOk = await _db.ProductCategories.AnyAsync(c => c.Id == req.CategoryId && c.StoreId == req.StoreId);
            if (!catOk) return BadRequest(new { error = "CategoryId no pertenece a la tienda." });
        }

        var entity = new Product
        {
            StoreId = req.StoreId,
            CategoryId = req.CategoryId,
            Name = req.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            BasePrice = req.BasePrice,
            IsActive = req.IsActive
        };

        _db.Products.Add(entity);
        await _db.SaveChangesAsync();

        return Ok(new { entity.Id, entity.StoreId, entity.CategoryId, entity.Name, entity.BasePrice, entity.IsActive });
    }

    public record CreateVariantRequest(string? Sku, string? Size, string? Color, decimal PriceDelta = 0m, int Stock = 0);

    [HttpPost("products/{productId:guid}/variants")]
    public async Task<IActionResult> CreateVariant(Guid productId, [FromBody] CreateVariantRequest req)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId);
        if (product is null) return NotFound();

        var entity = new ProductVariant
        {
            ProductId = productId,
            Sku = string.IsNullOrWhiteSpace(req.Sku) ? null : req.Sku.Trim(),
            Size = string.IsNullOrWhiteSpace(req.Size) ? null : req.Size.Trim(),
            Color = string.IsNullOrWhiteSpace(req.Color) ? null : req.Color.Trim(),
            PriceDelta = req.PriceDelta,
            Stock = req.Stock
        };

        _db.ProductVariants.Add(entity);
        await _db.SaveChangesAsync();

        return Ok(new { entity.Id, entity.ProductId, entity.Sku, entity.Size, entity.Color, entity.PriceDelta, entity.Stock });
    }

    public record AddImageRequest(string Url, int SortOrder = 0);

    [HttpPost("products/{productId:guid}/images")]
    public async Task<IActionResult> AddImage(Guid productId, [FromBody] AddImageRequest req)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId);
        if (product is null) return NotFound();

        var url = req.Url.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            return BadRequest(new { error = "URL inválida." });

        var entity = new ProductImage
        {
            ProductId = productId,
            Url = url,
            SortOrder = req.SortOrder
        };

        _db.ProductImages.Add(entity);
        await _db.SaveChangesAsync();

        return Ok(new { entity.Id, entity.ProductId, entity.Url, entity.SortOrder });
    }
}
