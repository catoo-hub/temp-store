using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;
using TempStore.Data;
using TempStore.Models;
using TempStore.Services;

var checks = 0;
Check(CatalogService.Money(1250.25m) == "1 250,25 ₽", "Prices display exact kopecks");
Check(CatalogService.Money(1250m) == "1 250 ₽", "Whole prices omit unnecessary zero kopecks");

void Check(bool condition, string name)
{
    if (!condition)
    {
        throw new Exception("FAIL: " + name);
    }

    checks++;
    Console.WriteLine("PASS: " + name);
}

async Task Reject(Func<Task> action, string name)
{
    try
    {
        await action();
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JSException)
    {
        Check(true, name);
        return;
    }

    throw new Exception("FAIL: " + name);
}

ProductEdit ProductDraft() => new()
{
    Name = "Худи 'Fresh'",
    Brand = "Nike",
    Category = "Худи",
    Price = 1234.56m,
    OldPrice = 1500.99m,
    Image = "/images/products/club-hoodie.webp",
    Color = "Серый",
    Sizes = "S, M, L",
    IsNew = true,
    Popular = true
};

RegistrationModel RegistrationDraft() => new()
{
    Name = "Анна",
    Email = "shopper@example.test",
    Password = "test-customer-password",
    Confirmation = "test-customer-password"
};

var testDirectory = Path.Combine(Path.GetTempPath(), "temp-store-checks-" + Guid.NewGuid().ToString("N"));
var databasePath = Path.Combine(testDirectory, "catalog.db");
var accountDatabasePath = Path.Combine(testDirectory, "accounts.db");
CatalogData snapshot;
try
{
    var database = new CatalogDatabase(databasePath);
    var seed = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "catalog.sql"));
    database.Initialize(seed);
    snapshot = database.ReadCatalog();
    Check(snapshot.Products.Length == 8 && snapshot.Brands.Length == 2 && snapshot.Reviews.Length == 3,
        "New database contains products, brands and reviews");
    Check(snapshot.Reviews.All(r => snapshot.Products.Any(p => p.Name == r.Product)),
        "Review names come from their products");

    using (var connection = new SqliteConnection(
        new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString()))
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

    var draft = ProductDraft();
    var created = database.SaveProduct(draft);
    Check(created is not null && created.Id > 8 && created.Name == draft.Name,
        "Admin creates a product and preserves quoted names");
    var stored = database.ReadCatalog().Products.Single(p => p.Id == created!.Id);
    Check(stored.Price == 1234.56m && stored.OldPrice == 1500.99m && stored.Sizes.SequenceEqual(["S", "M", "L"]),
        "Created product persists exact prices and parsed sizes");

    draft.Name = "Обновлённое худи";
    draft.Brand = "Adidas";
    draft.Price = 1199.99m;
    draft.OldPrice = null;
    draft.Sizes = "M, XL";
    draft.IsNew = false;
    draft.Popular = false;
    var updated = database.SaveProduct(draft, created!.Id);
    Check(updated?.Id == created.Id && database.ReadCatalog().Products.Length == 9,
        "Admin update changes the existing product without adding a row");
    stored = database.ReadCatalog().Products.Single(p => p.Id == created.Id);
    Check(stored.Name == draft.Name && stored.Brand == "Adidas" && stored.Price == 1199.99m
        && stored.OldPrice is null && stored.Sizes.SequenceEqual(["M", "XL"]) && !stored.IsNew && !stored.Popular,
        "Admin update persists edited values and clears optional values");

    Check(database.SaveProduct(draft, 9999) is null && database.ReadCatalog().Products.Length == 9,
        "Updating an unknown product does not insert it");
    Check(!database.DeleteProduct(9999), "Deleting an unknown product reports no change");
    Check(database.DeleteProduct(created.Id) && database.ReadCatalog().Products.Length == 8,
        "Admin deletes the selected product");

    var replacement = database.SaveProduct(ProductDraft())!;
    database.DeleteProduct(replacement.Id);
    database.Initialize(seed);
    var afterRestart = new CatalogDatabase(databasePath).SaveProduct(ProductDraft())!;
    Check(replacement.Id > created.Id && afterRestart.Id > replacement.Id,
        "Deleted product IDs are not reused, including after reinitialization");
    Check(database.DeleteProduct(1) && database.ReadCatalog().Reviews.Length == 2,
        "Deleting a product also deletes its related reviews");

    var accounts = new AccountDatabase(accountDatabasePath);
    accounts.Initialize();
    var registration = RegistrationDraft();
    registration.Name = "  Анна O'Connor  ";
    registration.Email = "Shopper@Example.test";
    var customer = accounts.Register(registration);
    Check(customer.Id > 0 && customer.Name == "Анна O'Connor"
        && customer.Email == "shopper@example.test" && customer.Phone == "",
        "Registration stores normalized customer details");
    Check(accounts.Find(customer.Id) == customer, "Registered customer can be loaded by ID");
    Check(accounts.Login("  SHOPPER@EXAMPLE.TEST  ", registration.Password)?.Id == customer.Id,
        "Login ignores email case and surrounding spaces");
    Check(accounts.Login(customer.Email, "wrong-password") is null
        && accounts.Login("missing@example.test", registration.Password) is null
        && accounts.Login(null, registration.Password) is null
        && accounts.Login(customer.Email, null) is null
        && accounts.Login(customer.Email, new string('x', 129)) is null,
        "Incorrect and invalid customer credentials are rejected");

    using (var connection = new SqliteConnection(
        new SqliteConnectionStringBuilder { DataSource = accountDatabasePath }.ToString()))
    {
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT PasswordHash FROM Users WHERE Id = $id";
        command.Parameters.AddWithValue("$id", customer.Id);
        var passwordHash = (string)command.ExecuteScalar()!;
        Check(passwordHash != registration.Password && passwordHash.Length >= 60,
            "Customer password is stored as a hash instead of plaintext");
    }

    var duplicate = RegistrationDraft();
    duplicate.Email = "SHOPPER@EXAMPLE.TEST";
    var duplicateRejected = false;
    try
    {
        accounts.Register(duplicate);
    }
    catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
    {
        duplicateRejected = true;
    }
    Check(duplicateRejected, "Duplicate customer email is rejected regardless of case");

    var profile = new ProfileModel { Name = "  Мария 'Новая'  ", Phone = " +7 900 000 00 00 " };
    var savedProfile = accounts.UpdateProfile(customer.Id, profile);
    Check(savedProfile is not null && savedProfile.Name == "Мария 'Новая'"
        && savedProfile.Phone == "+7 900 000 00 00" && savedProfile.Email == customer.Email,
        "Profile update normalizes name and phone without changing email");
    Check(accounts.Find(customer.Id) == savedProfile, "Edited profile is persisted");
    Check(accounts.Find(999999) is null && accounts.UpdateProfile(999999, profile) is null,
        "Unknown customer cannot be loaded or updated");

    var reopenedAccounts = new AccountDatabase(accountDatabasePath);
    reopenedAccounts.Initialize();
    Check(reopenedAccounts.Find(customer.Id) == savedProfile
        && reopenedAccounts.Login(customer.Email, registration.Password)?.Id == customer.Id,
        "Initialization preserves customer profiles and passwords");
}
finally
{
    SqliteConnection.ClearAllPools();
    foreach (var file in new[] { databasePath, accountDatabasePath })
    {
        if (File.Exists(file))
        {
            File.Delete(file);
        }
    }

    if (Directory.Exists(testDirectory))
    {
        Directory.Delete(testDirectory);
    }
}

var api = new FakeJs { Catalog = snapshot, Delay = true };
var catalog = new CatalogService(api);
await Task.WhenAll(catalog.LoadAsync(), catalog.LoadAsync());
Check(catalog.IsLoaded && api.CatalogReads == 1 && catalog.Products.Count == 8,
    "Concurrent catalog loads make one API request");

foreach (var bad in new CatalogData?[]
{
    null,
    new(null!, [], []),
    new([], null!, []),
    new([], [], null!),
    new([null!], [], []),
    new([snapshot.Products[0] with { Sizes = null! }], [], []),
    new([], [null!], []),
    new([], [], [null!])
})
{
    var invalidApi = new FakeJs { Catalog = bad };
    var invalidCatalog = new CatalogService(invalidApi);
    await Reject(invalidCatalog.LoadAsync, "Incomplete catalog response is rejected");
    Check(!invalidCatalog.IsLoaded && invalidCatalog.Products.Count == 0,
        "Invalid response is not stored as a loaded catalog");
    invalidApi.Catalog = snapshot;
    await invalidCatalog.LoadAsync();
    Check(invalidCatalog.IsLoaded && invalidApi.CatalogReads == 2, "Catalog can retry after an invalid response");
}

var savedCart = JsonSerializer.Serialize(new[] { new CartLine(1, "M", 2) });
var unavailableApi = new FakeJs { Catalog = snapshot, CatalogFailures = 1, Value = savedCart };
var recoveringCatalog = new CatalogService(unavailableApi);
var recoveringCart = new CartService(recoveringCatalog, unavailableApi);
await Reject(recoveringCart.LoadAsync, "Catalog API failure is returned to the caller");
Check(!recoveringCart.IsLoaded && !recoveringCart.StorageUnavailable
    && unavailableApi.Reads == 0 && unavailableApi.Writes == 0 && unavailableApi.Value == savedCart,
    "API failure leaves the saved basket untouched");
await recoveringCart.LoadAsync();
Check(recoveringCart.Count == 2 && recoveringCart.Total == 12980m
    && unavailableApi.CatalogReads == 2 && unavailableApi.Reads == 1,
    "Retry restores the saved basket after API recovery");

var freshApi = new FakeJs { Catalog = snapshot };
var freshCart = new CartService(new CatalogService(freshApi), freshApi);
await freshCart.AddAsync(1, "M");
Check(freshCart.Count == 1 && freshApi.CatalogReads == 1, "Adding a product loads its catalog before validation");

var filter = new CatalogFilter();
filter.Categories.Add("Футболки");
filter.Brands.Add("Adidas");
filter.Sizes.Add("XS");
Check(catalog.Products.Where(filter.Matches).Select(x => x.Id).SequenceEqual([2]),
    "Category, brand and size filters intersect");

filter.MaxPrice = 2000;
Check(!catalog.Products.Any(filter.Matches), "Price ceiling excludes more expensive items");

filter.Clear();
filter.DiscountOnly = true;
Check(catalog.Products.Where(filter.Matches).All(x => x.OldPrice > x.Price),
    "Discount filter returns only discounted products");

filter.Clear();
Check(catalog.Products.All(filter.Matches) && filter.ActiveCount == 0, "Reset restores all products");

var storage = new FakeJs();
var cart = new CartService(catalog, storage);
await cart.AddAsync(1, "M");
await cart.AddAsync(1, "M");
await cart.AddAsync(1, "L");
Check(cart.Lines.Count == 2 && cart.Count == 3, "Same SKU and size merge; different sizes stay separate");
Check(cart.Total == 19470m && cart.Savings == 4500m && cart.Subtotal == 23970m,
    "Totals and discounts use catalog prices");

var restored = new CartService(catalog, storage);
await restored.LoadAsync();
Check(restored.Count == 3 && restored.Total == cart.Total, "Cart survives a fresh service instance");

await restored.SetQuantityAsync(1, "M", 999);
Check(restored.Lines.Single(x => x.Size == "M").Quantity == 10, "Quantity is capped at ten");
await Reject(() => restored.AddAsync(1, "M"), "Adding an eleventh item is rejected");
await Reject(() => restored.AddAsync(999, "M"), "Unknown product is rejected");
await Reject(() => restored.AddAsync(1, "invalid"), "Unavailable size is rejected");
await restored.SetQuantityAsync(1, "L", 0);
Check(restored.Lines.Count == 1, "Zero quantity removes a cart line");
await restored.SetQuantityAsync(1, "M", 0);
Check(restored.Count == 0 && restored.Total == 0, "Removing the last item empties totals and lines");

storage.Value = "broken json";
var corrupt = new CartService(catalog, storage);
await corrupt.LoadAsync();
Check(corrupt.IsLoaded && corrupt.Count == 0, "Corrupt browser data recovers to an empty cart");

storage.Value = """
    [
        null,
        { "ProductId": 999, "Size": "M", "Quantity": 2 },
        { "ProductId": 1, "Size": "bad", "Quantity": 2 },
        { "ProductId": 1, "Size": "M", "Quantity": -1 }
    ]
    """;
var invalid = new CartService(catalog, storage);
await invalid.LoadAsync();
Check(invalid.Count == 0, "Null, stale and invalid cart entries are discarded");

storage.Value = JsonSerializer.Serialize(new[]
{
    new CartLine(1, "M", int.MaxValue),
    new CartLine(1, "M", int.MaxValue)
});
var overflow = new CartService(catalog, storage);
await overflow.LoadAsync();
Check(overflow.Count == 10, "Tampered quantities cannot overflow during merge");

var denied = new CartService(catalog, new FakeJs { Deny = true });
await denied.AddAsync(1, "M");
Check(denied.StorageUnavailable && denied.Count == 1, "Blocked localStorage keeps in-memory shopping usable");

var slowStorage = new FakeJs
{
    Delay = true,
    Value = JsonSerializer.Serialize(new[] { new CartLine(1, "M", 2) })
};
var concurrent = new CartService(catalog, slowStorage);
await Task.WhenAll(concurrent.LoadAsync(), concurrent.LoadAsync());
Check(concurrent.Count == 2 && slowStorage.Reads == 1, "Concurrent loads read and merge saved cart once");

var validation = new List<ValidationResult>();
var feedback = new FeedbackModel { Email = "bad-email" };
Check(!Validator.TryValidateObject(feedback, new ValidationContext(feedback), validation, true)
    && validation.Count == 3,
    "Feedback requires a name, valid email and message");
feedback = new FeedbackModel
{
    Name = "Test",
    Email = "test@example.test",
    Message = "Есть размер M?"
};
Check(Validator.TryValidateObject(feedback, new ValidationContext(feedback), [], true),
    "Valid feedback passes validation");

var validDraft = ProductDraft();
Check(Validator.TryValidateObject(validDraft, new ValidationContext(validDraft), [], true),
    "Valid admin product passes validation");

foreach (var (change, name) in new (Action<ProductEdit> Change, string Name)[]
{
    (p => p.Price = 0, "Zero product price is rejected"),
    (p => p.Price = -1, "Negative product price is rejected"),
    (p => p.Price = 1_000_000.01m, "Excessive product price is rejected"),
    (p => p.Price = 1234.567m, "Sub-kopeck product price is rejected"),
    (p => p.OldPrice = p.Price, "Old price equal to current price is rejected"),
    (p => p.OldPrice = p.Price - 1, "Old price below current price is rejected"),
    (p => p.OldPrice = 1_000_000.01m, "Excessive old price is rejected"),
    (p => p.OldPrice = 1500.999m, "Sub-kopeck old price is rejected"),
    (p => p.Sizes = "", "Empty product sizes are rejected"),
    (p => p.Sizes = " , ", "Product sizes require at least one value"),
    (p => p.Sizes = "M, unknown", "Unknown product sizes are rejected"),
    (p => p.Sizes = "M, M", "Duplicate product sizes are rejected"),
    (p => p.Image = "/images/products/../../secret.webp", "Image path traversal is rejected")
})
{
    var draft = ProductDraft();
    change(draft);
    Check(!Validator.TryValidateObject(draft, new ValidationContext(draft), [], true), name);
}

var unconfiguredAdmin = new AdminAccess(new ConfigurationBuilder().Build());
Check(!unconfiguredAdmin.Configured && !unconfiguredAdmin.VerifyPassword("test-admin-only"),
    "Admin access is closed when no password is configured");
var configuredAdmin = new AdminAccess(new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:Password"] = "test-admin-only" })
    .Build());
Check(configuredAdmin.Configured && configuredAdmin.VerifyPassword("test-admin-only"),
    "Configured admin password is accepted");
Check(!configuredAdmin.VerifyPassword("wrong-password") && !configuredAdmin.VerifyPassword(null)
    && !configuredAdmin.VerifyPassword("") && !configuredAdmin.VerifyPassword(new string('x', 257)),
    "Incorrect, empty and oversized admin passwords are rejected");

var validRegistration = RegistrationDraft();
Check(Validator.TryValidateObject(validRegistration, new ValidationContext(validRegistration), [], true),
    "Valid customer registration passes validation");

foreach (var (change, name) in new (Action<RegistrationModel> Change, string Name)[]
{
    (r => r.Password = r.Confirmation = "short", "Short customer password is rejected"),
    (r => r.Password = r.Confirmation = new string('x', 129), "Oversized customer password is rejected"),
    (r => r.Confirmation = "different-password", "Password confirmation must match"),
    (r => r.Email = "invalid-email", "Registration requires a valid email"),
    (r => r.Name = " ", "Registration requires a name"),
    (r => r.Name = new string('x', 81), "Oversized customer name is rejected")
})
{
    var registration = RegistrationDraft();
    change(registration);
    Check(!Validator.TryValidateObject(registration, new ValidationContext(registration), [], true), name);
}

var login = new LoginModel { Email = validRegistration.Email, Password = validRegistration.Password };
Check(Validator.TryValidateObject(login, new ValidationContext(login), [], true),
    "Valid customer login passes validation");
login.Email = "invalid-email";
Check(!Validator.TryValidateObject(login, new ValidationContext(login), [], true),
    "Login requires a valid email");
login.Email = validRegistration.Email;
login.Password = "";
Check(!Validator.TryValidateObject(login, new ValidationContext(login), [], true),
    "Login requires a password");

var validProfile = new ProfileModel { Name = "Анна", Phone = null };
Check(Validator.TryValidateObject(validProfile, new ValidationContext(validProfile), [], true),
    "Profile permits an omitted phone number");
foreach (var (change, name) in new (Action<ProfileModel> Change, string Name)[]
{
    (p => p.Name = " ", "Profile requires a name"),
    (p => p.Name = new string('x', 81), "Profile rejects an oversized name"),
    (p => p.Phone = "not-a-phone", "Profile requires a valid phone when provided")
})
{
    var profile = new ProfileModel { Name = "Анна", Phone = "+7 900 000 00 00" };
    change(profile);
    Check(!Validator.TryValidateObject(profile, new ValidationContext(profile), [], true), name);
}

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

    public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) =>
        InvokeAsync<T>(identifier, CancellationToken.None, args);

    public async ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        if (identifier == "tempUi.getCatalog")
        {
            CatalogReads++;
            if (Delay)
            {
                await Task.Delay(20, cancellationToken);
            }

            if (CatalogFailures > 0)
            {
                CatalogFailures--;
                throw new JSException("Catalog unavailable");
            }

            return (T)(object?)Catalog!;
        }

        if (Deny)
        {
            throw new JSException("Storage unavailable");
        }

        if (identifier == "localStorage.getItem")
        {
            Reads++;
            if (Delay)
            {
                await Task.Delay(20, cancellationToken);
            }

            return (T)(object?)Value!;
        }

        if (identifier == "localStorage.setItem")
        {
            Writes++;
            Value = (string?)args![1];
            return default!;
        }

        throw new NotSupportedException(identifier);
    }
}
