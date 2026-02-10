namespace OnlineShop.Api.Models;

public sealed class CatalogProductsQuery
{
    public string? Q { get; init; }                 // q
    public int? CategoryId { get; init; }           // categoryId
    public decimal? MinPrice { get; init; }         // minPrice
    public decimal? MaxPrice { get; init; }         // maxPrice
    public string? Sort { get; init; }              // relevance|newest|price_asc|price_desc
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

