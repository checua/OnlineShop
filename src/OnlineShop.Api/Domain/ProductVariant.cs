namespace OnlineShop.Api.Domain;

public class ProductVariant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }

    public string? Sku { get; set; }
    public string? Size { get; set; }
    public string? Color { get; set; }

    public decimal PriceDelta { get; set; } = 0m;
    public int Stock { get; set; } = 0;

    public Product? Product { get; set; }
}
