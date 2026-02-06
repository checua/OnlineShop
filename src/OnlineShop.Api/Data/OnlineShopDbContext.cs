using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Api.Domain;

namespace OnlineShop.Api.Data;

public class OnlineShopDbContext : IdentityDbContext<ApplicationUser>
{
    public OnlineShopDbContext(DbContextOptions<OnlineShopDbContext> options) : base(options) { }

    public DbSet<Store> Stores => Set<Store>();
    public DbSet<StoreCategory> StoreCategories => Set<StoreCategory>();

    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // 1) Default schema para TODO
        builder.HasDefaultSchema("SHOP");

        // 2) Identity tables explícitas en schema SHOP
        builder.Entity<ApplicationUser>().ToTable("Users");
        builder.Entity<IdentityRole>().ToTable("Roles");
        builder.Entity<IdentityUserRole<string>>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<string>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<string>>().ToTable("UserLogins");
        builder.Entity<IdentityRoleClaim<string>>().ToTable("RoleClaims");
        builder.Entity<IdentityUserToken<string>>().ToTable("UserTokens");

        // =========================
        // 3) Negocio: Stores
        // =========================
        builder.Entity<Store>(e =>
        {
            e.ToTable("Stores");

            e.HasKey(x => x.Id);

            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(120).IsRequired();
            e.Property(x => x.Status).HasMaxLength(20).IsRequired();

            e.HasIndex(x => x.Slug).IsUnique();

            // Store -> StoreCategory (opcional)
            // (Si tu CategoryId es nullable, esto va perfecto)
            e.HasOne(x => x.Category)
                .WithMany()
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<StoreCategory>(e =>
        {
            e.ToTable("StoreCategories");

            e.HasKey(x => x.Id);

            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
        });

        // =========================
        // 4) Catálogo
        // =========================
        builder.Entity<ProductCategory>(e =>
        {
            e.ToTable("ProductCategories");

            e.HasKey(x => x.Id);

            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.HasIndex(x => new { x.StoreId, x.Name }).IsUnique();

            // ProductCategory -> Store (requerido)
            // IMPORTANT: NO cascade para evitar rutas múltiples y para proteger borrados accidentales
            e.HasOne(x => x.Store)
                .WithMany()
                .HasForeignKey(x => x.StoreId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<Product>(e =>
        {
            e.ToTable("Products");

            e.HasKey(x => x.Id);

            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.BasePrice).HasColumnType("decimal(18,2)");

            e.HasIndex(x => new { x.StoreId, x.Name });

            // Product -> Store (requerido)
            e.HasOne(x => x.Store)
                .WithMany()
                .HasForeignKey(x => x.StoreId)
                .OnDelete(DeleteBehavior.NoAction);

            // Product -> ProductCategory (opcional)
            e.HasOne(x => x.Category)
                .WithMany()
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);

            // Product -> Variants (cascade ok)
            e.HasMany(x => x.Variants)
                .WithOne(v => v.Product)
                .HasForeignKey(v => v.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            // Product -> Images (cascade ok)
            e.HasMany(x => x.Images)
                .WithOne(i => i.Product)
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ProductVariant>(e =>
        {
            e.ToTable("ProductVariants");

            e.HasKey(x => x.Id);

            e.Property(x => x.Sku).HasMaxLength(80);
            e.Property(x => x.Size).HasMaxLength(40);
            e.Property(x => x.Color).HasMaxLength(40);
            e.Property(x => x.PriceDelta).HasColumnType("decimal(18,2)");

            e.HasIndex(x => new { x.ProductId, x.Sku })
                .IsUnique()
                .HasFilter("[Sku] IS NOT NULL");
        });

        builder.Entity<ProductImage>(e =>
        {
            e.ToTable("ProductImages");

            e.HasKey(x => x.Id);

            e.Property(x => x.Url).HasMaxLength(500).IsRequired();
            e.HasIndex(x => new { x.ProductId, x.SortOrder });
        });
    }
}
