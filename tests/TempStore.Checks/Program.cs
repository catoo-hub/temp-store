using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.JSInterop;
using TempStore.Data;
using TempStore.Models;
using TempStore.Services;

var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS: " + name); }
async Task Reject(Func<Task> action, string name) { try { await action(); } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JSException) { Check(true, name); return; } throw new Exception("FAIL: " + name); }
var testDirectory = Path.Combine(Path.GetTempPath(), "temp-store-checks-" + Guid.NewGuid().ToString("N"));
var databasePath = Path.Combine(testDirectory, "catalog.db");
CatalogData snapshot;
try
{
    var database = new CatalogDatabase(databasePath);
    var seed = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "catalog.sql"));
    database.Initialize(seed);
    snapshot = database.ReadCatalog();
    Check(snapshot.Products.Length == 8 && snapshot.Brands.Length == 2 && snapshot.Reviews.Length == 3,
        "New database contains products, brands and reviews");
    Check(snapshot.Reviews.All(r => snapshot.Products.Any(p => p.Name == r.Product)), "Review names come from their products");
    using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString()))
    {
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Products SET Price = 610001 WHERE Id = 1";
        command.ExecuteNonQuery();
    }
    database.Initialize(seed);
    var persisted = new CatalogDatabase(databasePath).ReadCatalog();
    Check(persisted.Products.Length == 8 && persisted.Brands.Length == 2 && persisted.Reviews.Length == 3,
        "Repeated initialization does not duplicate seed rows");
    Check(persisted.Products.Single(p => p.Id == 1).Price == 6100.01m,
        "Database edits survive initialization and preserve kopecks");
}
finally
{
    SqliteConnection.ClearAllPools();
    if (File.Exists(databasePath)) File.Delete(databasePath);
    if (Directory.Exists(testDirectory)) Directory.Delete(testDirectory);
}
var api = new FakeJs { Catalog = snapshot, Delay = true };
var catalog = new CatalogService(api);
await Task.WhenAll(catalog.LoadAsync(), catalog.LoadAsync());
Check(catalog.IsLoaded && api.CatalogReads == 1 && catalog.Products.Count == 8, "Concurrent catalog loads make one API request");
foreach (var bad in new CatalogData?[] { null, new(null!, [], []), new([], null!, []), new([], [], null!),
    new([null!], [], []), new([snapshot.Products[0] with { Sizes = null! }], [], []), new([], [null!], []), new([], [], [null!]) })
{
    var invalidApi = new FakeJs { Catalog = bad };
    var invalidCatalog = new CatalogService(invalidApi);
    await Reject(invalidCatalog.LoadAsync, "Incomplete catalog response is rejected");
    Check(!invalidCatalog.IsLoaded && invalidCatalog.Products.Count == 0, "Invalid response is not stored as a loaded catalog");
    invalidApi.Catalog = snapshot;
    await invalidCatalog.LoadAsync();
    Check(invalidCatalog.IsLoaded && invalidApi.CatalogReads == 2, "Catalog can retry after an invalid response");
}
var savedCart = JsonSerializer.Serialize(new[] { new CartLine(1, "M", 2) });
var unavailableApi = new FakeJs { Catalog = snapshot, CatalogFailures = 1, Value = savedCart };
var recoveringCatalog = new CatalogService(unavailableApi);
var recoveringCart = new CartService(recoveringCatalog, unavailableApi);
await Reject(recoveringCart.LoadAsync, "Catalog API failure is returned to the caller");
Check(!recoveringCart.IsLoaded && !recoveringCart.StorageUnavailable && unavailableApi.Reads == 0 && unavailableApi.Writes == 0 && unavailableApi.Value == savedCart,
    "API failure leaves the saved basket untouched");
await recoveringCart.LoadAsync();
Check(recoveringCart.Count == 2 && recoveringCart.Total == 12980m && unavailableApi.CatalogReads == 2 && unavailableApi.Reads == 1,
    "Retry restores the saved basket after API recovery");
var freshApi = new FakeJs { Catalog = snapshot };
var freshCart = new CartService(new CatalogService(freshApi), freshApi);
await freshCart.AddAsync(1, "M");
Check(freshCart.Count == 1 && freshApi.CatalogReads == 1, "Adding a product loads its catalog before validation");
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
    public CatalogData? Catalog { get; set; }
    public int CatalogFailures { get; set; }
    public int CatalogReads { get; private set; }
    public string? Value { get; set; }
    public bool Deny { get; set; }
    public bool Delay { get; set; }
    public int Reads { get; private set; }
    public int Writes { get; private set; }
    public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, CancellationToken.None, args);
    public async ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        if (identifier == "tempUi.getCatalog")
        {
            CatalogReads++;
            if (Delay) await Task.Delay(20, cancellationToken);
            if (CatalogFailures > 0) { CatalogFailures--; throw new JSException("Catalog unavailable"); }
            return (T)(object?)Catalog!;
        }
        if (Deny) throw new JSException("Storage unavailable");
        if (identifier == "localStorage.getItem") { Reads++; if (Delay) await Task.Delay(20, cancellationToken); return (T)(object?)Value!; }
        if (identifier == "localStorage.setItem") { Writes++; Value = (string?)args![1]; return default!; }
        throw new NotSupportedException(identifier);
    }
}
