using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OnlineShop.Api.Data;
using OnlineShop.Api.Domain;
using OnlineShop.Api.Options;
using OnlineShop.Api.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.Configure<OnlineShop.Api.Options.TaxOptions>(
    builder.Configuration.GetSection("Tax"));

// ===== Swagger (incluye Bearer Auth) =====
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "OnlineShop.Api", Version = "1.0" });

    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "JWT Authorization header. Ej: \"Bearer {token}\"",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = "Bearer"
        }
    };

    c.AddSecurityDefinition("Bearer", securityScheme);

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { securityScheme, Array.Empty<string>() }
    });
});

// ===== Options =====
builder.Services.AddOptions<MercadoPagoOptions>()
    .Bind(builder.Configuration.GetSection("MercadoPago"))
    .ValidateOnStart();

// ===== Http Clients =====
builder.Services.AddHttpClient<MercadoPagoClient>();

// ===== DB (SQL Azure retry) =====
builder.Services.AddDbContext<OnlineShopDbContext>(opt =>
    opt.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql => sql.EnableRetryOnFailure(
            maxRetryCount: 8,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null
        )
    ));

// ===== Identity =====
builder.Services.AddIdentityCore<ApplicationUser>(opt =>
{
    opt.User.RequireUniqueEmail = true;
})
.AddRoles<Microsoft.AspNetCore.Identity.IdentityRole>()
.AddEntityFrameworkStores<OnlineShopDbContext>();

// ===== JWT Auth =====
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var key = builder.Configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key))
        {
            // En dev te conviene que reviente claro para que no quede “falso protegido”
            throw new InvalidOperationException("Falta configuración Jwt:Key. Agrega Jwt:Key en appsettings.Development.json (recomendado) o appsettings.json.");
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),

            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddHealthChecks();

var app = builder.Build();

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
else
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();