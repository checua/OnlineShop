namespace OnlineShop.Api.Domain;

public class ProductCategory
{
    public int Id { get; set; }
    public Guid StoreId { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }

    // navegación opcional (solo una)
    public Store? Store { get; set; }
}

