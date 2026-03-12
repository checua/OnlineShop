// src/OnlineShop.Api/Options/StripeOptions.cs
namespace OnlineShop.Api.Options;

public sealed class StripeOptions
{
    public string SecretKey { get; set; } = "";
    public string WebhookSecret { get; set; } = ""; // <-- agrega esto
    public string Currency { get; set; } = "MXN";
    public string FrontendBaseUrl { get; set; } = "";
}

