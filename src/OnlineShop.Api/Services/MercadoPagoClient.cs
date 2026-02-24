using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OnlineShop.Api.Options;

namespace OnlineShop.Api.Services;

public sealed class MercadoPagoClient
{
    private readonly HttpClient _http;
    private readonly MercadoPagoOptions _opt;

    public MercadoPagoClient(HttpClient http, IOptions<MercadoPagoOptions> opt)
    {
        _http = http;
        _opt = opt.Value;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_opt.AccessToken);

    public async Task<(string preferenceId, string initPoint)> CreatePreferenceAsync(
        Guid orderId,
        string customerEmail,
        IEnumerable<(string title, int quantity, decimal unitPrice, string currencyId)> items,
        CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("MercadoPago AccessToken no configurado.");

        var payload = new
        {
            external_reference = orderId.ToString(),
            payer = new { email = customerEmail },
            items = items.Select(i => new
            {
                title = i.title,
                quantity = i.quantity,
                unit_price = i.unitPrice,
                currency_id = i.currencyId
            }),
            back_urls = new
            {
                success = $"{_opt.FrontendBaseUrl}/checkout/success?orderId={orderId}",
                pending = $"{_opt.FrontendBaseUrl}/checkout/pending?orderId={orderId}",
                failure = $"{_opt.FrontendBaseUrl}/checkout/failure?orderId={orderId}"
            },
            auto_return = "approved",
            notification_url = string.IsNullOrWhiteSpace(_opt.NotificationUrl) ? null : _opt.NotificationUrl
        };

        var json = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.mercadopago.com/checkout/preferences");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opt.AccessToken);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"MP CreatePreference failed: {(int)res.StatusCode} {body}");

        using var doc = JsonDocument.Parse(body);
        var prefId = doc.RootElement.GetProperty("id").GetString() ?? "";
        var initPoint = doc.RootElement.GetProperty("init_point").GetString() ?? "";

        return (prefId, initPoint);
    }

    public async Task<(string status, string externalReference, string paymentId)> GetPaymentAsync(string paymentId, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("MercadoPago AccessToken no configurado.");

        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.mercadopago.com/v1/payments/{paymentId}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opt.AccessToken);

        using var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"MP GetPayment failed: {(int)res.StatusCode} {body}");

        using var doc = JsonDocument.Parse(body);

        var status = doc.RootElement.TryGetProperty("status", out var st) ? (st.GetString() ?? "") : "";

        var externalRef = doc.RootElement.TryGetProperty("external_reference", out var er)
            ? (er.GetString() ?? "")
            : "";

        // payment id puede venir como number
        var id = doc.RootElement.TryGetProperty("id", out var pid)
            ? pid.ToString()
            : paymentId;

        return (status, externalRef, id);
    }
}