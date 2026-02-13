using Microsoft.EntityFrameworkCore;
using OnlineShop.Api.Data;
using OnlineShop.Api.Domain;
using Microsoft.Data.SqlClient;
using OnlineShop.Api.Controllers;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddOptions<StripeOptions>()
    .Bind(builder.Configuration.GetSection("Stripe"))
    .Validate(o => !string.IsNullOrWhiteSpace(o.SecretKey), "Stripe:SecretKey requerido")
    .Validate(o => !string.IsNullOrWhiteSpace(o.WebhookSecret), "Stripe:WebhookSecret requerido")
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

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OnlineShopDbContext>();
    var csb = new SqlConnectionStringBuilder(db.Database.GetDbConnection().ConnectionString);

    Console.WriteLine($"[DB-RUNTIME] Server={csb.DataSource} | Database={csb.InitialCatalog}");
}



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
