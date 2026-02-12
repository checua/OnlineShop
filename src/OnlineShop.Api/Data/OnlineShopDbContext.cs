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

    // ===== Orders (0008) =====
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<PaymentAttempt> PaymentAttempts => Set<PaymentAttempt>();

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

            // clave para evitar multiple cascade paths
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

        // ===== Cart =====
        modelBuilder.Entity<Cart>(b =>
        {
            b.ToTable("Carts");
            b.HasKey(x => x.Id);

            b.Property(x => x.UserId).HasMaxLength(450);
            b.Property(x => x.GuestId).HasMaxLength(64);

            b.Property(x => x.Status).HasConversion<int>();

            b.Property(x => x.CreatedAt);
            b.Property(x => x.UpdatedAt);

            b.HasMany(x => x.Items)
                .WithOne(i => i.Cart)
                .HasForeignKey(i => i.CartId)
                .OnDelete(DeleteBehavior.Cascade);

            // Store FK
            b.HasOne<Store>()
                .WithMany()
                .HasForeignKey(x => x.StoreId)
                .OnDelete(DeleteBehavior.Cascade);

            // 1 carrito ACTIVO por (StoreId + UserId)
            b.HasIndex(x => new { x.StoreId, x.UserId })
                .IsUnique()
                .HasFilter("[UserId] IS NOT NULL AND [Status] = 0");

            // 1 carrito ACTIVO por (StoreId + GuestId)
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

            b.HasOne<Product>()
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne<ProductVariant>()
                .WithMany()
                .HasForeignKey(x => x.VariantId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(x => new { x.CartId, x.ProductId, x.VariantId });
        });

        // ===== Orders =====
        modelBuilder.Entity<Order>(b =>
        {
            b.ToTable("Orders");
            b.HasKey(x => x.Id);

            b.Property(x => x.UserId).HasMaxLength(450);
            b.Property(x => x.GuestId).HasMaxLength(64);

            b.Property(x => x.Status).HasConversion<int>();

            b.Property(x => x.Currency).HasMaxLength(8).IsRequired();

            b.Property(x => x.Subtotal).HasPrecision(18, 2);
            b.Property(x => x.Shipping).HasPrecision(18, 2);
            b.Property(x => x.Tax).HasPrecision(18, 2);
            b.Property(x => x.Total).HasPrecision(18, 2);

            b.Property(x => x.CustomerEmail).HasMaxLength(200).IsRequired();

            b.Property(x => x.ShippingName).HasMaxLength(200).IsRequired();
            b.Property(x => x.ShippingPhone).HasMaxLength(40).IsRequired();
            b.Property(x => x.ShippingAddress1).HasMaxLength(200).IsRequired();
            b.Property(x => x.ShippingAddress2).HasMaxLength(200);
            b.Property(x => x.ShippingCity).HasMaxLength(120).IsRequired();
            b.Property(x => x.ShippingState).HasMaxLength(120).IsRequired();
            b.Property(x => x.ShippingPostalCode).HasMaxLength(20).IsRequired();
            b.Property(x => x.ShippingCountry).HasMaxLength(2).IsRequired();

            b.Property(x => x.Provider).HasMaxLength(32);
            b.Property(x => x.ProviderSessionId).HasMaxLength(200);
            b.Property(x => x.ProviderPaymentId).HasMaxLength(200);

            b.HasIndex(x => new { x.StoreId, x.CreatedAt });
            b.HasIndex(x => new { x.UserId, x.CreatedAt });
            b.HasIndex(x => new { x.GuestId, x.CreatedAt });
            b.HasIndex(x => x.ProviderSessionId);
            b.HasIndex(x => x.ProviderPaymentId);

            b.HasOne<Store>()
                .WithMany()
                .HasForeignKey(x => x.StoreId)
                .OnDelete(DeleteBehavior.NoAction);

            b.HasMany(x => x.Items)
                .WithOne(i => i.Order)
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(x => x.Payments)
                .WithOne(p => p.Order)
                .HasForeignKey(p => p.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(b =>
        {
            b.ToTable("OrderItems");
            b.HasKey(x => x.Id);

            b.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
            b.Property(x => x.VariantSku).HasMaxLength(64);
            b.Property(x => x.VariantSize).HasMaxLength(64);
            b.Property(x => x.VariantColor).HasMaxLength(64);
            b.Property(x => x.ImageUrl).HasMaxLength(1000);

            b.Property(x => x.UnitPrice).HasPrecision(18, 2);
            b.Property(x => x.LineTotal).HasPrecision(18, 2);

            b.HasIndex(x => new { x.OrderId, x.ProductId, x.VariantId });

            // Opcional: no FK para evitar cascadas/locks con catálogo.
            // Si quieres FK, cámbialo a Restrict/NoAction.
        });

        modelBuilder.Entity<PaymentAttempt>(b =>
        {
            b.ToTable("PaymentAttempts");
            b.HasKey(x => x.Id);

            b.Property(x => x.Provider).HasMaxLength(32).IsRequired();
            b.Property(x => x.ProviderPaymentId).HasMaxLength(200).IsRequired();
            b.Property(x => x.ProviderSessionId).HasMaxLength(200);

            b.Property(x => x.Status).HasConversion<int>();

            b.Property(x => x.Amount).HasPrecision(18, 2);
            b.Property(x => x.Currency).HasMaxLength(8).IsRequired();
            b.Property(x => x.RawJson);

            b.HasIndex(x => x.ProviderPaymentId);
            b.HasIndex(x => x.ProviderSessionId);
        });
    }
}
