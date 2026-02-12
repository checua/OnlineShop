namespace OnlineShop.Api.Models.Cart;

public sealed class AddCartItemRequest
{
    public Guid ProductId { get; set; }
    public Guid? VariantId { get; set; }
    public int Quantity { get; set; } = 1;
}

public sealed class UpdateCartItemRequest
{
    public int Quantity { get; set; }
}
