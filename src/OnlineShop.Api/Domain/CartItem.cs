namespace OnlineShop.Api.Domain;

public sealed class CartItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CartId { get; set; }
    public Cart Cart { get; set; } = default!;

    public Guid ProductId { get; set; }
    public Guid? VariantId { get; set; }

    public int Quantity { get; set; }

    // Snapshot (precio al momento de agregar)
    public decimal UnitPrice { get; set; }

    // Snapshot (para UI rápida sin joins)
    public string ProductName { get; set; } = default!;
    public string? VariantSku { get; set; }
    public string? VariantSize { get; set; }
    public string? VariantColor { get; set; }
    public string? ImageUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
