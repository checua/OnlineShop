namespace OnlineShop.Api.Domain;

public sealed class ProcessedWebhookEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Provider { get; set; } = default!;   // stripe
    public string EventId { get; set; } = default!;    // evt_...
    public string EventType { get; set; } = default!;  // checkout.session.completed
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}