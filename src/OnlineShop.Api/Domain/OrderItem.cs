namespace OnlineShop.Api.Domain;

public sealed class OrderItem
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }
    public Order Order { get; set; } = default!;

    public Guid ProductId { get; set; }
    public Guid? VariantId { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }

    // Snapshot
    public string ProductName { get; set; } = "";
    public string? VariantSku { get; set; }
    public string? VariantSize { get; set; }
    public string? VariantColor { get; set; }
    public string? ImageUrl { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
