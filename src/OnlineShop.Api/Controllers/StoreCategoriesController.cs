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

        // 1) Default schema para TODO (incluye Identity y negocio)
        builder.HasDefaultSchema("SHOP");

        // 2) Identity tables en SHOP
        builder.Entity<ApplicationUser>().ToTable("Users");
        builder.Entity<IdentityRole>().ToTable("Roles");
        builder.Entity<IdentityUserRole<string>>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<string>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<string>>().ToTable("UserLogins");
        builder.Entity<IdentityRoleClaim<string>>().ToTable("RoleClaims");
        builder.Entity<IdentityUserToken<string>>().ToTable("UserTokens");

        // =========================
        // Stores
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
            // Esto evita ShadowFKs si tu Store tiene:
            // int? CategoryId  + StoreCategory? Category
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
            e.Property(x => x.SortOrder).HasDefaultValue(0);

            e.HasIndex(x => x.SortOrder);
        });

        // =========================
        // Catálogo
        // =========================
        builder.Entity<ProductCategory>(e =>
        {
            e.ToTable("ProductCategories");
            e.HasKey(x => x.Id);

            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.HasIndex(x => new { x.StoreId, x.Name }).IsUnique();

            // ProductCategory -> Store (requerido)
            // NO ACTION para evitar múltiples rutas de cascade
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
        });
    }
}
