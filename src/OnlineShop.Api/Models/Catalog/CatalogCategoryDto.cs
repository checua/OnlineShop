namespace OnlineShop.Api.Models.Catalog;

public sealed class CatalogCategoryDto
{
    public required int CategoryId { get; init; }
    public required string Name { get; init; }
    public int SortOrder { get; init; }
    public int ProductCount { get; init; }
}
