namespace OnlineShop.Api.Models.Catalog;

public sealed class CatalogProductListItemDto
{
    public required Guid ProductId { get; init; }
    public required string Name { get; init; }
    public string? Summary { get; init; }           // derivado de Description
    public required decimal MinPrice { get; init; } // desde variants
    public required decimal MaxPrice { get; init; } // desde variants
    public string? MainImageUrl { get; init; }
    public required bool HasVariants { get; init; }
}

