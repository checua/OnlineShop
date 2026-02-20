using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Api.Domain;

namespace OnlineShop.Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(OnlineShopDbContext db, CancellationToken ct = default)
    {
        // Limpia tracking por si acaso
        db.ChangeTracker.Clear();

        try
        {
            // Fuerza conexión (serverless puede estar pausada/resumiendo)
            // Si no está disponible, saltamos sin tumbar la app.
            await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);

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

            // TRACKED para actualizar fácil (son poquitas filas)
            var catsTracked = await db.StoreCategories.ToListAsync(ct);

            bool catsChanged = false;

            StoreCategory? FindCat(string name) =>
                catsTracked
                    .Where(c => !string.IsNullOrWhiteSpace(c.Name) &&
                                c.Name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
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
                    catsChanged = true;
                }
                else
                {
                    if (cat.SortOrder != sort) { cat.SortOrder = sort; catsChanged = true; }
                    if (!string.Equals(cat.Name, name, StringComparison.Ordinal)) { cat.Name = name; catsChanged = true; }
                }
            }

            if (catsChanged)
                await db.SaveChangesAsync(ct);

            db.ChangeTracker.Clear();

            // Relee para mapear Ids (sin tracking)
            var cats = await db.StoreCategories
                .AsNoTracking()
                .Select(c => new { c.Id, c.Name })
                .ToListAsync(ct);

            int? CatId(string name) =>
                cats
                    .Where(c => !string.IsNullOrWhiteSpace(c.Name) &&
                                c.Name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
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
                await db.Stores.AsNoTracking()
                    .Where(s => s.Slug != null)
                    .Select(s => s.Slug!)
                    .ToListAsync(ct),
                StringComparer.OrdinalIgnoreCase
            );

            var now = DateTime.UtcNow;
            bool storesChanged = false;

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

                storesChanged = true;
            }

            if (storesChanged)
                await db.SaveChangesAsync(ct);

            db.ChangeTracker.Clear();
        }
        catch (SqlException ex) when (
            ex.Message.Contains("not currently available", StringComparison.OrdinalIgnoreCase) ||
            ex.Number is 40613 or 40197 or 40501 or 10928 or 10929
        )
        {
            // Serverless pausada / resumiendo / transient: no tumbar el host
            Console.WriteLine($"[SEED] Skipped (transient/serverless). SqlErr={ex.Number}. {ex.Message}");
        }
    }
}