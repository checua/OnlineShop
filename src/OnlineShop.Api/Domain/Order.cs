namespace OnlineShop.Api.Domain;

public sealed class Order
{
    public Guid Id { get; set; }

    public Guid StoreId { get; set; }
    public string? UserId { get; set; }     // null si es guest
    public string? GuestId { get; set; }    // null si es user

    public OrderStatus Status { get; set; } = OrderStatus.PendingPayment;

    public string Currency { get; set; } = "MXN";
    public decimal Subtotal { get; set; }
    public decimal Shipping { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }

    public string CustomerEmail { get; set; } = "";

    // Shipping (MVP)
    public string ShippingName { get; set; } = "";
    public string ShippingPhone { get; set; } = "";
    public string ShippingAddress1 { get; set; } = "";
    public string? ShippingAddress2 { get; set; }
    public string ShippingCity { get; set; } = "";
    public string ShippingState { get; set; } = "";
    public string ShippingPostalCode { get; set; } = "";
    public string ShippingCountry { get; set; } = "MX";

    // Proveedor
    public string? Provider { get; set; }               // "stripe"
    public string? ProviderSessionId { get; set; }      // Stripe Checkout Session Id
    public string? ProviderPaymentId { get; set; }      // PaymentIntent Id u otro
    public DateTime? PaidAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<OrderItem> Items { get; set; } = new();
    public List<PaymentAttempt> Payments { get; set; } = new();
}
