using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Api.Domain;

namespace OnlineShop.Api.Data;

public sealed class OnlineShopDbContext
    : IdentityDbContext<ApplicationUser, IdentityRole, string>
{
    public OnlineShopDbContext(DbContextOptions<OnlineShopDbContext> options)
        : base(options) { }

    // ===== Negocio =====
    public DbSet<StoreCategory> StoreCategories => Set<StoreCategory>();
    public DbSet<Store> Stores => Set<Store>();

    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();

    // ===== Carrito =====
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("SHOP");

        // ===== Identity tables in SHOP schema =====
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

            b.HasOne<Store>()
                .WithMany()
                .HasForeignKey(x => x.StoreId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(x => new { x.StoreId, x.Name });
        });

        // ===== Product =====
        modelBuilder.Entity<Product>(b =>
        {
            b.ToTable("Products");
            b.HasKey(x => x.Id);

            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Description).HasMaxLength(4000);
            b.Property(x => x.BasePrice).HasPrecision(18, 2);

            b.HasIndex(x => x.StoreId);
            b.HasIndex(x => new { x.StoreId, x.IsActive });

            // 🔥 CLAVE: NO ACTION para evitar multiple cascade paths
            b.HasOne(x => x.Store)
                .WithMany()
                .HasForeignKey(x => x.StoreId)
                .OnDelete(DeleteBehavior.NoAction);

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

            b.HasOne(x => x.Product)
                .WithMany(p => p.Variants)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(x => x.ProductId);

            // Único por producto cuando Sku != null (evita broncas con nulls)
            b.HasIndex(x => new { x.ProductId, x.Sku })
                .IsUnique()
                .HasFilter("[Sku] IS NOT NULL");
        });

        // ===== ProductImage =====
        modelBuilder.Entity<ProductImage>(b =>
        {
            b.ToTable("ProductImages");
            b.HasKey(x => x.Id);

            b.Property(x => x.Url).HasMaxLength(1000).IsRequired();
            b.Property(x => x.SortOrder).HasDefaultValue(0);

            b.HasOne(x => x.Product)
                .WithMany(p => p.Images)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(x => x.ProductId);
        });

        // ===== Cart =====
        modelBuilder.Entity<Cart>(b =>
        {
            b.ToTable("Carts");
            b.HasKey(x => x.Id);

            b.Property(x => x.UserId).HasMaxLength(450);
            b.Property(x => x.GuestId).HasMaxLength(64);

            b.Property(x => x.Status).HasConversion<int>();

            b.HasMany(x => x.Items)
                .WithOne(i => i.Cart)
                .HasForeignKey(i => i.CartId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne<Store>()
                .WithMany()
                .HasForeignKey(x => x.StoreId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(x => new { x.StoreId, x.UserId })
                .IsUnique()
                .HasFilter("[UserId] IS NOT NULL AND [Status] = 0");

            b.HasIndex(x => new { x.StoreId, x.GuestId })
                .IsUnique()
                .HasFilter("[GuestId] IS NOT NULL AND [Status] = 0");
        });

        // ===== CartItem =====
        modelBuilder.Entity<CartItem>(b =>
        {
            b.ToTable("CartItems");
            b.HasKey(x => x.Id);

            b.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
            b.Property(x => x.VariantSku).HasMaxLength(64);
            b.Property(x => x.VariantSize).HasMaxLength(64);
            b.Property(x => x.VariantColor).HasMaxLength(64);
            b.Property(x => x.ImageUrl).HasMaxLength(1000);

            b.Property(x => x.UnitPrice).HasPrecision(18, 2);

            // No cascades hacia productos/variantes (seguro)
            b.HasOne<Product>()
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne<ProductVariant>()
                .WithMany()
                .HasForeignKey(x => x.VariantId)
                .OnDelete(DeleteBehavior.Restrict);

            // Idealmente único para evitar duplicados por mismo producto/variante en el carrito
            b.HasIndex(x => new { x.CartId, x.ProductId, x.VariantId })
                .IsUnique();
        });
    }
}
