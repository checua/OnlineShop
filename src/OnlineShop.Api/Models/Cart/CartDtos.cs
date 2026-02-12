namespace OnlineShop.Api.Models.Cart;

public sealed class CartDto
{
    public Guid CartId { get; set; }
    public string StoreSlug { get; set; } = default!;
    public string? GuestId { get; set; }

    public int ItemsCount { get; set; }
    public decimal Subtotal { get; set; }

    public List<CartItemDto> Items { get; set; } = new();
}

public sealed class CartItemDto
{
    public Guid ItemId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? VariantId { get; set; }

    public string Name { get; set; } = default!;
    public string? Sku { get; set; }
    public string? Size { get; set; }
    public string? Color { get; set; }
    public string? ImageUrl { get; set; }

    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}
