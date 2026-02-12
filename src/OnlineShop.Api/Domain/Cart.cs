namespace OnlineShop.Api.Domain;

public sealed class Cart
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid StoreId { get; set; }

    // Identity normalmente usa string Id
    public string? UserId { get; set; }

    // GuestId lo guardas en app/localStorage (ej: GUID sin guiones)
    public string? GuestId { get; set; }

    public CartStatus Status { get; set; } = CartStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<CartItem> Items { get; set; } = new();
}
