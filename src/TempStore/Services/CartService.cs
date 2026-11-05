using System.Text.Json;
using Microsoft.JSInterop;
using TempStore.Models;

namespace TempStore.Services;

public sealed class CartService(CatalogService catalog, IJSRuntime js)
{
    private const string StorageKey = "temp-store.cart.v1";
    private readonly List<CartLine> lines = [];
    private Task? loadTask;
    public IReadOnlyList<CartLine> Lines => lines.AsReadOnly();
    public bool IsLoaded { get; private set; }
    public bool StorageUnavailable { get; private set; }
    public event Action? Changed;
    public int Count => lines.Sum(x => x.Quantity);
    public decimal Total => lines.Sum(x => catalog.Find(x.ProductId)!.Price * x.Quantity);
    public decimal Subtotal => lines.Sum(x => (catalog.Find(x.ProductId)!.OldPrice ?? catalog.Find(x.ProductId)!.Price) * x.Quantity);
    public decimal Savings => Subtotal - Total;

    public Task LoadAsync() => loadTask ??= LoadCoreAsync();
    private async Task LoadCoreAsync()
    {
        if (IsLoaded) return;
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            var saved = string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<List<CartLine?>>(json) ?? [];
            foreach (var line in saved.Take(100))
            {
                if (line is null) continue;
                var product = catalog.Find(line.ProductId);
                if (product is null || !product.Sizes.Contains(line.Size) || line.Quantity <= 0) continue;
                var existing = lines.FindIndex(x => x.ProductId == line.ProductId && x.Size == line.Size);
                if (existing < 0) lines.Add(line with { Quantity = Math.Min(10, line.Quantity) });
                else lines[existing] = lines[existing] with { Quantity = Math.Min(10, lines[existing].Quantity + Math.Min(10, line.Quantity)) };
            }
        }
        catch (JsonException) { /* Discard corrupt browser data. */ }
        catch (JSException) { StorageUnavailable = true; }
        IsLoaded = true;
        Changed?.Invoke();
    }
    public async Task AddAsync(int productId, string size)
    {
        var product = catalog.Find(productId) ?? throw new ArgumentException("Товар не найден");
        if (!product.Sizes.Contains(size)) throw new ArgumentException("Выберите доступный размер");
        await LoadAsync();
        var index = lines.FindIndex(x => x.ProductId == productId && x.Size == size);
        if (index >= 0)
        {
            if (lines[index].Quantity >= 10) throw new InvalidOperationException("Максимум 10 единиц одного размера");
            lines[index] = lines[index] with { Quantity = lines[index].Quantity + 1 };
        }
        else lines.Add(new(productId, size, 1));
        await PersistAsync();
    }
    public async Task SetQuantityAsync(int productId, string size, int quantity)
    {
        var index = lines.FindIndex(x => x.ProductId == productId && x.Size == size);
        if (index < 0) return;
        if (quantity <= 0) lines.RemoveAt(index);
        else lines[index] = lines[index] with { Quantity = Math.Clamp(quantity, 1, 10) };
        await PersistAsync();
    }
    public async Task ClearAsync() { lines.Clear(); await PersistAsync(); }
    private async Task PersistAsync()
    {
        Changed?.Invoke();
        try { await js.InvokeVoidAsync("localStorage.setItem", StorageKey, JsonSerializer.Serialize(lines)); }
        catch (JSException) { StorageUnavailable = true; Changed?.Invoke(); }
    }
}
