namespace TempStore.Models;

public sealed class CatalogFilter
{
    public HashSet<string> Categories { get; } = [];
    public HashSet<string> Brands { get; } = [];
    public HashSet<string> Sizes { get; } = [];
    public int MaxPrice { get; set; } = 15000;
    public bool DiscountOnly { get; set; }
    public bool Matches(Product product) =>
        (Categories.Count == 0 || Categories.Contains(product.Category))
        && (Brands.Count == 0 || Brands.Contains(product.Brand))
        && (Sizes.Count == 0 || product.Sizes.Any(Sizes.Contains))
        && product.Price <= MaxPrice
        && (!DiscountOnly || product.Discount > 0);
    public int ActiveCount => Categories.Count + Brands.Count + Sizes.Count + (MaxPrice < 15000 ? 1 : 0) + (DiscountOnly ? 1 : 0);
    public void Clear()
    {
        Categories.Clear();
        Brands.Clear();
        Sizes.Clear();
        MaxPrice = 15000;
        DiscountOnly = false;
    }
}
