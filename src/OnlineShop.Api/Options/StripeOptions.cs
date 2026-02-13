namespace OnlineShop.Api.Options;

public sealed class StripeOptions
{
    public string SecretKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string FrontendBaseUrl { get; set; } = "";
    public string Currency { get; set; } = "MXN";
}

