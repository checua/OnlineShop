using Microsoft.EntityFrameworkCore;
using OnlineShop.Api.Domain;

namespace OnlineShop.Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(OnlineShopDbContext db)
    {
        // Categorías globales
        if (!await db.StoreCategories.AnyAsync())
        {
            db.StoreCategories.AddRange(
                new StoreCategory { Name = "Moda" },
                new StoreCategory { Name = "Zapatos" },
                new StoreCategory { Name = "Electrónica" },
                new StoreCategory { Name = "Hogar" },
                new StoreCategory { Name = "Salud y Belleza" },
                new StoreCategory { Name = "Deportes" },
                new StoreCategory { Id = 1, Name = "Moda", SortOrder = 1 },
                new StoreCategory { Id = 2, Name = "Calzado", SortOrder = 2 }


            );
            await db.SaveChangesAsync();
        }

        // Tiendas demo
        if (!await db.Stores.AnyAsync())
        {
            var cats = await db.StoreCategories.AsNoTracking().ToListAsync();

            int Cat(string name) => cats.First(c => c.Name == name).Id;

            db.Stores.AddRange(

                new Store { Name = "Zapatería Centro", Slug = "zapateria-centro", Status = "Approved", CategoryId = Cat("Zapatos") },
                new Store { Name = "Moda Urbana", Slug = "moda-urbana", Status = "Approved", CategoryId = Cat("Moda") },
                new Store { Name = "Gadgets MX", Slug = "gadgets-mx", Status = "Approved", CategoryId = Cat("Electrónica") },
                new Store { Name = "Casa & Orden", Slug = "casa-orden", Status = "Approved", CategoryId = Cat("Hogar") },
                new Store { Name = "FitZone", Slug = "fitzone", Status = "Approved", CategoryId = Cat("Deportes") },
                new Store { Name = "Beauty Lab", Slug = "beauty-lab", Status = "Approved", CategoryId = Cat("Salud y Belleza") },
                new Store { Name = "Kicks Premium", Slug = "kicks-premium", Status = "Approved", CategoryId = Cat("Zapatos") },
                new Store { Name = "Tech Outlet", Slug = "tech-outlet", Status = "Approved", CategoryId = Cat("Electrónica") },
                new Store { Name = "Minimal Home", Slug = "minimal-home", Status = "Approved", CategoryId = Cat("Hogar") },
                new Store { Name = "Runner Pro", Slug = "runner-pro", Status = "Approved", CategoryId = Cat("Deportes") }
            );

            await db.SaveChangesAsync();
        }
    }
}
