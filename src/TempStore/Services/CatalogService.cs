using Microsoft.JSInterop;
using TempStore.Models;

namespace TempStore.Services;

public sealed class CatalogService(IJSRuntime js)
{
    // shortcut: one snapshot per tab fits this small catalog; load pages when inventory grows.
    private Task? loadTask;
    private CatalogData data = new([], [], []);
    public bool IsLoaded { get; private set; }
    public IReadOnlyList<Product> Products => data.Products;
    public IReadOnlyList<Brand> Brands => data.Brands;
    public IReadOnlyList<Review> Reviews => data.Reviews;
    public IReadOnlyList<string> Categories { get; private set; } = [];
    public IReadOnlyList<string> Sizes { get; private set; } = [];
    public IReadOnlyList<OrderRow> Orders => [];

    public async Task LoadAsync()
    {
        try { await (loadTask ??= LoadCoreAsync()); }
        catch { loadTask = null; throw; }
    }
    private async Task LoadCoreAsync()
    {
        var result = await js.InvokeAsync<CatalogData?>("tempUi.getCatalog");
        if (result?.Products is null || result.Brands is null || result.Reviews is null ||
            result.Products.Any(p => p is null || p.Sizes is null) ||
            result.Brands.Any(b => b is null) || result.Reviews.Any(r => r is null))
            throw new InvalidOperationException("Не удалось загрузить товары.");
        data = result;
        Categories = Products.Select(p => p.Category).Distinct().ToArray();
        string[] sizeOrder = ["XS", "S", "M", "L", "XL", "XXL"];
        Sizes = sizeOrder.Where(size => Products.Any(p => p.Sizes.Contains(size))).ToArray();
        IsLoaded = true;
    }

    public Product? Find(int id) => Products.FirstOrDefault(x => x.Id == id);
    public static string Money(decimal value) => value.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) + " ₽";
}
