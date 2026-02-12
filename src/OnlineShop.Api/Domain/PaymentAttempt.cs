namespace OnlineShop.Api.Domain;

public sealed class PaymentAttempt
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }
    public Order Order { get; set; } = default!;

    public string Provider { get; set; } = "stripe";
    public string ProviderPaymentId { get; set; } = "";  // PaymentIntentId / charge id
    public string? ProviderSessionId { get; set; }       // Checkout Session Id
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "MXN";

    public string? RawJson { get; set; }                 // opcional (debug/auditoría)
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
