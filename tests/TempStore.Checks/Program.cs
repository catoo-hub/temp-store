using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.JSInterop;
using TempStore.Models;
using TempStore.Services;

var catalog = new CatalogService();
var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS: " + name); }
async Task Reject(Func<Task> action, string name) { try { await action(); } catch (ArgumentException) { Check(true, name); return; } catch (InvalidOperationException) { Check(true, name); return; } throw new Exception("FAIL: " + name); }
var filter = new CatalogFilter();
filter.Categories.Add("Футболки"); filter.Brands.Add("Adidas"); filter.Sizes.Add("XS");
Check(catalog.Products.Where(filter.Matches).Select(x => x.Id).SequenceEqual([2]), "Category, brand and size filters intersect");
filter.MaxPrice = 2000;
Check(!catalog.Products.Any(filter.Matches), "Price ceiling excludes more expensive items");
filter.Clear(); filter.DiscountOnly = true;
Check(catalog.Products.Where(filter.Matches).All(x => x.OldPrice > x.Price), "Discount filter returns only discounted products");
filter.Clear(); Check(catalog.Products.All(filter.Matches) && filter.ActiveCount == 0, "Reset restores all products");
var storage = new FakeJs(); var cart = new CartService(catalog, storage);
await cart.AddAsync(1, "M"); await cart.AddAsync(1, "M"); await cart.AddAsync(1, "L");
Check(cart.Lines.Count == 2 && cart.Count == 3, "Same SKU and size merge; different sizes stay separate");
Check(cart.Total == 19470m && cart.Savings == 4500m && cart.Subtotal == 23970m, "Totals and discounts use catalog prices");
var restored = new CartService(catalog, storage); await restored.LoadAsync();
Check(restored.Count == 3 && restored.Total == cart.Total, "Cart survives a fresh service instance");
await restored.SetQuantityAsync(1, "M", 999);
Check(restored.Lines.Single(x => x.Size == "M").Quantity == 10, "Quantity is capped at ten");
await Reject(() => restored.AddAsync(1, "M"), "Adding an eleventh item is rejected");
await Reject(() => restored.AddAsync(999, "M"), "Unknown product is rejected");
await Reject(() => restored.AddAsync(1, "invalid"), "Unavailable size is rejected");
await restored.SetQuantityAsync(1, "L", 0);
Check(restored.Lines.Count == 1, "Zero quantity removes a cart line");
await restored.ClearAsync(); Check(restored.Count == 0 && restored.Total == 0, "Clear empties totals and lines");
storage.Value = "broken json"; var corrupt = new CartService(catalog, storage); await corrupt.LoadAsync();
Check(corrupt.IsLoaded && corrupt.Count == 0, "Corrupt browser data recovers to an empty cart");
storage.Value = "[null,{\"ProductId\":999,\"Size\":\"M\",\"Quantity\":2},{\"ProductId\":1,\"Size\":\"bad\",\"Quantity\":2},{\"ProductId\":1,\"Size\":\"M\",\"Quantity\":-1}]";
var invalid = new CartService(catalog, storage); await invalid.LoadAsync(); Check(invalid.Count == 0, "Null, stale and invalid cart entries are discarded");
storage.Value = JsonSerializer.Serialize(new[] { new CartLine(1, "M", int.MaxValue), new CartLine(1, "M", int.MaxValue) });
var overflow = new CartService(catalog, storage); await overflow.LoadAsync(); Check(overflow.Count == 10, "Tampered quantities cannot overflow during merge");
var denied = new CartService(catalog, new FakeJs { Deny = true }); await denied.AddAsync(1, "M");
Check(denied.StorageUnavailable && denied.Count == 1, "Blocked localStorage keeps in-memory shopping usable");
var slowStorage = new FakeJs { Delay = true, Value = JsonSerializer.Serialize(new[] { new CartLine(1, "M", 2) }) };
var concurrent = new CartService(catalog, slowStorage); await Task.WhenAll(concurrent.LoadAsync(), concurrent.LoadAsync());
Check(concurrent.Count == 2 && slowStorage.Reads == 1, "Concurrent loads read and merge saved cart once");
var validation = new List<ValidationResult>();
var registration = new RegisterModel { Name = "Test", Email = "bad-email", Password = "123", Confirmation = "different" };
Check(!Validator.TryValidateObject(registration, new ValidationContext(registration), validation, true) && validation.Count >= 3, "Registration validates email, password length and matching passwords");
var checkout = new CheckoutModel { Name = "Test", Email = "test@example.test", Phone = "+7 900 000 00 00", Address = "Учебный адрес" };
Check(Validator.TryValidateObject(checkout, new ValidationContext(checkout), [], true), "Valid checkout data passes validation");
Console.WriteLine($"{checks} checks passed.");

sealed class FakeJs : IJSRuntime
{
    public string? Value { get; set; }
    public bool Deny { get; set; }
    public bool Delay { get; set; }
    public int Reads { get; private set; }
    public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, CancellationToken.None, args);
    public async ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        if (Deny) throw new JSException("Storage unavailable");
        if (identifier == "localStorage.getItem") { Reads++; if (Delay) await Task.Delay(20, cancellationToken); return (T)(object?)Value!; }
        if (identifier == "localStorage.setItem") { Value = (string?)args![1]; return default!; }
        throw new NotSupportedException(identifier);
    }
}
