using Microsoft.EntityFrameworkCore;
using OnlineShop.Api.Domain;

namespace OnlineShop.Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(OnlineShopDbContext db, CancellationToken ct = default)
    {
        // Evita conflictos de tracking si algo ya quedó en el ChangeTracker
        db.ChangeTracker.Clear();

        // =========================
        // 1) StoreCategories (UPSERT por Name)
        // =========================
        var desiredCategories = new (string Name, int SortOrder)[]
        {
            ("Moda", 0),
            ("Calzado", 1),
            ("Electrónica", 2),
            ("Hogar", 3),
            ("Salud y Belleza", 4),
            ("Deportes", 5),
        };

        // Cargamos TRACKED para poder actualizar SortOrder si ya existe
        var existingCats = await db.StoreCategories.ToListAsync(ct);

        // Si hubiera duplicados por nombre, nos quedamos con el de menor Id
        StoreCategory? FindCat(string name) =>
            existingCats
                .Where(c => c.Name != null && c.Name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Id)
                .FirstOrDefault();

        foreach (var (name, sort) in desiredCategories)
        {
            var cat = FindCat(name);
            if (cat is null)
            {
                db.StoreCategories.Add(new StoreCategory
                {
                    Name = name,
                    SortOrder = sort
                });
            }
            else
            {
                // Ajusta SortOrder si lo quieres consistente
                if (cat.SortOrder != sort)
                    cat.SortOrder = sort;

                // Normaliza nombre (por si alguien metió espacios raros)
                if (!string.Equals(cat.Name, name, StringComparison.Ordinal))
                    cat.Name = name;
            }
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        // Relee categorías para mapear Ids (sin tracking)
        var cats = await db.StoreCategories.AsNoTracking().ToListAsync(ct);

        int? CatId(string name) =>
            cats
                .Where(c => c.Name != null && c.Name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Id)
                .Select(c => (int?)c.Id)
                .FirstOrDefault();

        // =========================
        // 2) Stores demo (UPSERT por Slug)
        // =========================
        var desiredStores = new (string Name, string Slug, string Status, string CategoryName)[]
        {
            ("Zapatería Centro", "zapateria-centro", "Approved", "Calzado"),
            ("Moda Urbana", "moda-urbana", "Approved", "Moda"),
            ("Gadgets MX", "gadgets-mx", "Approved", "Electrónica"),
            ("Casa & Orden", "casa-orden", "Approved", "Hogar"),
            ("FitZone", "fitzone", "Approved", "Deportes"),
            ("Beauty Lab", "beauty-lab", "Approved", "Salud y Belleza"),
            ("Kicks Premium", "kicks-premium", "Approved", "Calzado"),
            ("Tech Outlet", "tech-outlet", "Approved", "Electrónica"),
            ("Minimal Home", "minimal-home", "Approved", "Hogar"),
            ("Runner Pro", "runner-pro", "Approved", "Deportes"),
        };

        var existingSlugs = new HashSet<string>(
            await db.Stores.AsNoTracking().Select(s => s.Slug).ToListAsync(ct),
            StringComparer.OrdinalIgnoreCase
        );

        var now = DateTime.UtcNow;

        foreach (var (name, slug, status, catName) in desiredStores)
        {
            if (existingSlugs.Contains(slug))
                continue;

            db.Stores.Add(new Store
            {
                Id = Guid.NewGuid(),
                Name = name,
                Slug = slug,
                Status = status,
                CategoryId = CatId(catName),
                CreatedAt = now
            });
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }
}
