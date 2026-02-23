namespace OnlineShop.Api.Options;

public sealed class MercadoPagoOptions
{
    public string AccessToken { get; set; } = "";
    public string NotificationUrl { get; set; } = "";
    public string FrontendBaseUrl { get; set; } = "http://localhost:3000";
    public string Currency { get; set; } = "MXN";
}
