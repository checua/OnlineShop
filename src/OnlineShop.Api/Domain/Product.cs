namespace OnlineShop.Api.Domain;

public class Product
{
    public Guid Id { get; set; }
    public Guid StoreId { get; set; }
    public int? CategoryId { get; set; }

    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public decimal BasePrice { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // navegaciones opcionales (solo una por relación)
    public Store? Store { get; set; }
    public ProductCategory? Category { get; set; }
    public List<ProductVariant> Variants { get; set; } = new();
    public List<ProductImage> Images { get; set; } = new();

}

