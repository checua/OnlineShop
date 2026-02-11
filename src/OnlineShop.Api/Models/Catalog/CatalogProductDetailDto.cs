namespace OnlineShop.Api.Models.Catalog;

public sealed class CatalogProductDetailDto
{
    public required Guid ProductId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    public int? CategoryId { get; init; }
    public string? CategoryName { get; init; }

    public decimal BasePrice { get; init; }
    public decimal MinPrice { get; init; }
    public decimal MaxPrice { get; init; }

    public DateTime CreatedAt { get; init; }

    public required IReadOnlyList<CatalogProductImageDto> Images { get; init; }
    public required IReadOnlyList<CatalogProductVariantDto> Variants { get; init; }
}

public sealed class CatalogProductImageDto
{
    public required string Url { get; init; }
    public int SortOrder { get; init; }
}

public sealed class CatalogProductVariantDto
{
    public required Guid VariantId { get; init; }
    public string? Sku { get; init; }
    public string? Size { get; init; }
    public string? Color { get; init; }

    public decimal PriceDelta { get; init; }
    public decimal Price { get; init; }
    public int Stock { get; init; }
}
