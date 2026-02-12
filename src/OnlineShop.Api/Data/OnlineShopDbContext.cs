using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Api.Domain;

namespace OnlineShop.Api.Data;

public sealed class OnlineShopDbContext
    : IdentityDbContext<ApplicationUser, IdentityRole, string>
{
    public OnlineShopDbContext(DbContextOptions<OnlineShopDbContext> options)
        : base(options)
    {
    }

    // ===== Identity (viene de IdentityDbContext) =====
    // DbSet<ApplicationUser> Users -> ya existe en base class

    // ===== Negocio =====
    public DbSet<StoreCategory> StoreCategories => Set<StoreCategory>();
    public DbSet<Store> Stores => Set<Store>();

    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Default schema: SHOP
        modelBuilder.HasDefaultSchema("SHOP");

        // ===== Identity tables names in SHOP schema =====
        modelBuilder.Entity<ApplicationUser>().ToTable("Users");
        modelBuilder.Entity<IdentityRole>().ToTable("Roles");
        modelBuilder.Entity<IdentityUserRole<string>>().ToTable("UserRoles");
        modelBuilder.Entity<IdentityUserClaim<string>>().ToTable("UserClaims");
        modelBuilder.Entity<IdentityUserLogin<string>>().ToTable("UserLogins");
        modelBuilder.Entity<IdentityRoleClaim<string>>().ToTable("RoleClaims");
        modelBuilder.Entity<IdentityUserToken<string>>().ToTable("UserTokens");

        // ===== StoreCategory =====
        modelBuilder.Entity<StoreCategory>(b =>
        {
            b.ToTable("StoreCategories");
            b.HasKey(x => x.Id);

            b.Property(x => x.Name).HasMaxLength(120).IsRequired();
            b.Property(x => x.SortOrder).HasDefaultValue(0);

            b.HasIndex(x => x.Name);
        });

        // ===== Store =====
        modelBuilder.Entity<Store>(b =>
        {
            b.ToTable("Stores");
            b.HasKey(x => x.Id);

            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Slug).HasMaxLength(120).IsRequired();
            b.Property(x => x.Status).HasMaxLength(32).IsRequired();

            b.HasIndex(x => x.Slug).IsUnique();

            // Store.CategoryId -> StoreCategory (opcional)
            b.HasOne(x => x.Category)
                .WithMany()
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ===== ProductCategory =====
        modelBuilder.Entity<ProductCategory>(b =>
        {
            b.ToTable("ProductCategories");
            b.HasKey(x => x.Id);

            b.Property(x => x.Name).HasMaxLength(120).IsRequired();
            b.Property(x => x.SortOrder).HasDefaultValue(0);

            // Pertenece a una Store
            b.HasIndex(x => new { x.StoreId, x.Name });
        });

        // ===== Product =====
        modelBuilder.Entity<Product>(b =>
        {
            b.ToTable("Products");
            b.HasKey(x => x.Id);

            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Description).HasMaxLength(4000);

            // Si ya tienes precisión en migraciones, esto no debería moverte nada.
            b.Property(x => x.BasePrice).HasPrecision(18, 2);

            b.HasIndex(x => x.StoreId);
            b.HasIndex(x => new { x.StoreId, x.IsActive });

            // Product -> Store
            b.HasOne(x => x.Store)
                .WithMany()
                .HasForeignKey(x => x.StoreId)
                .OnDelete(DeleteBehavior.Cascade);

            // Product -> ProductCategory (opcional)
            b.HasOne(x => x.Category)
                .WithMany()
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ===== ProductVariant =====
        modelBuilder.Entity<ProductVariant>(b =>
        {
            b.ToTable("ProductVariants");
            b.HasKey(x => x.Id);

            b.Property(x => x.Sku).HasMaxLength(64);
            b.Property(x => x.Size).HasMaxLength(64);
            b.Property(x => x.Color).HasMaxLength(64);

            b.Property(x => x.PriceDelta).HasPrecision(18, 2);

            b.HasIndex(x => x.ProductId);

            b.HasOne(x => x.Product)
                .WithMany(p => p.Variants)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ===== ProductImage =====
        modelBuilder.Entity<ProductImage>(b =>
        {
            b.ToTable("ProductImages");
            b.HasKey(x => x.Id);

            b.Property(x => x.Url).HasMaxLength(1000).IsRequired();
            b.Property(x => x.SortOrder).HasDefaultValue(0);

            b.HasIndex(x => x.ProductId);

            b.HasOne(x => x.Product)
                .WithMany(p => p.Images)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
