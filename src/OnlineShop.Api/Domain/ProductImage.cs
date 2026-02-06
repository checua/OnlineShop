namespace OnlineShop.Api.Domain;

public class ProductImage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }

    public string Url { get; set; } = default!;
    public int SortOrder { get; set; } = 0;

    public Product? Product { get; set; }
}
