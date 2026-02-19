// src/OnlineShop.Api/Program.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using OnlineShop.Api.Data;
using OnlineShop.Api.Domain;
using OnlineShop.Api.Options;
using Stripe;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddOptions<StripeOptions>()
    .Bind(builder.Configuration.GetSection("Stripe"))
    .Validate(o =>
    {
        // Permite correr sin Stripe en DEV o cuando aún no lo uses
        if (builder.Environment.IsDevelopment()) return true;

        // Si en PROD todavía no lo usarás, permite vacío (o mejor usa un flag Provider)
        var usingStripe = builder.Configuration["Payments:Provider"] == "stripe";
        if (!usingStripe) return true;

        return !string.IsNullOrWhiteSpace(o.SecretKey)
            && !string.IsNullOrWhiteSpace(o.WebhookSecret);
    }, "Stripe keys requeridas si Payments:Provider=stripe")
    .ValidateOnStart();

builder.Services.AddDbContext<OnlineShopDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentityCore<ApplicationUser>(opt =>
{
    opt.User.RequireUniqueEmail = true;
})
.AddRoles<Microsoft.AspNetCore.Identity.IdentityRole>()
.AddEntityFrameworkStores<OnlineShopDbContext>();

builder.Services.AddHealthChecks();

var app = builder.Build();

// Set Stripe API key una sola vez (si existe)
var stripeOpts = app.Services.GetRequiredService<IOptions<StripeOptions>>().Value;
if (!string.IsNullOrWhiteSpace(stripeOpts.SecretKey))
{
    StripeConfiguration.ApiKey = stripeOpts.SecretKey;
}

// Log runtime DB (sin exponer password)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OnlineShopDbContext>();
    var csb = new SqlConnectionStringBuilder(db.Database.GetDbConnection().ConnectionString);
    Console.WriteLine($"[DB-RUNTIME] Server={csb.DataSource} | Database={csb.InitialCatalog}");
}

// Seed
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OnlineShopDbContext>();
    await DbSeeder.SeedAsync(db);
}

app.MapHealthChecks("/health");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
