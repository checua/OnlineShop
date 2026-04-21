using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using OnlineShop.Admin.Data;

var builder = WebApplication.CreateBuilder(args);

// Blazor Server + Razor Pages
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSingleton<WeatherForecastService>();

// HttpClient hacia OnlineShop.Api
builder.Services.AddHttpClient("OnlineShopApi", client =>
{
    var baseUrl = builder.Configuration["OnlineShopFrontend:ApiBaseUrl"]
                  ?? throw new InvalidOperationException("Falta OnlineShopFrontend:ApiBaseUrl");

    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(20);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// Importante: mapear Razor Pages antes del fallback de Blazor
app.MapRazorPages();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();