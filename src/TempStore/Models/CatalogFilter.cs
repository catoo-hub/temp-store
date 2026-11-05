namespace TempStore.Models;
public sealed class CatalogFilter
{
    public HashSet<string> Categories { get; } = [];
    public HashSet<string> Brands { get; } = [];
    public HashSet<string> Sizes { get; } = [];
    public int MaxPrice { get; set; } = 15000;
    public bool DiscountOnly { get; set; }
    public bool Matches(Product p) => (Categories.Count == 0 || Categories.Contains(p.Category))
        && (Brands.Count == 0 || Brands.Contains(p.Brand)) && (Sizes.Count == 0 || p.Sizes.Any(Sizes.Contains))
        && p.Price <= MaxPrice && (!DiscountOnly || p.Discount > 0);
    public int ActiveCount => Categories.Count + Brands.Count + Sizes.Count + (MaxPrice < 15000 ? 1 : 0) + (DiscountOnly ? 1 : 0);
    public void Clear() { Categories.Clear(); Brands.Clear(); Sizes.Clear(); MaxPrice = 15000; DiscountOnly = false; }
}
