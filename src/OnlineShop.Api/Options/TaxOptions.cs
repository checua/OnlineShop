namespace OnlineShop.Api.Options;

public sealed class TaxOptions
{
    public decimal DefaultRate { get; set; } = 0m;

    // Ej: { "MXN": 0.16 }
    public Dictionary<string, decimal> Rates { get; set; } = new();

    public decimal GetRate(string? currency)
    {
        var key = (currency ?? "").Trim().ToUpperInvariant();
        return Rates.TryGetValue(key, out var r) ? r : DefaultRate;
    }
}