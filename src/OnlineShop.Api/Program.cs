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

// Stripe options (estricto en no-dev; flexible en dev para que puedas correr sin Stripe)
builder.Services.AddOptions<StripeOptions>()
    .Bind(builder.Configuration.GetSection("Stripe"))
    .Validate(o => builder.Environment.IsDevelopment() || !string.IsNullOrWhiteSpace(o.SecretKey),
        "Stripe:SecretKey requerido")
    .Validate(o => builder.Environment.IsDevelopment() || !string.IsNullOrWhiteSpace(o.WebhookSecret),
        "Stripe:WebhookSecret requerido")
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
