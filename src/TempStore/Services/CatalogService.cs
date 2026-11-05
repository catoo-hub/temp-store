using TempStore.Models;

namespace TempStore.Services;

// Deliberately read-only fixtures for the UI milestone. Replace with a database repository next.
public sealed class CatalogService
{
    public IReadOnlyList<Brand> Brands { get; } = [
        new("Nike", "Футболки, худи, брюки и одежда для тренировок"),
        new("Adidas", "Футболки и легинсы")];
    public IReadOnlyList<string> Categories { get; } = ["Футболки", "Худи", "Брюки", "Шорты", "Легинсы", "Куртки"];
    public IReadOnlyList<string> Sizes { get; } = ["XS", "S", "M", "L", "XL", "XXL"];
    public IReadOnlyList<Product> Products { get; } = [
        new(1, "Худи Club Fleece", "Nike", "Худи", 6490, 7990, "/images/products/club-hoodie.webp", "Серый", ["S","M","L","XL"], true, true),
        new(2, "Футболка Essentials", "Adidas", "Футболки", 2990, null, "/images/products/adidas-shirt.webp", "Белый", ["XS","S","M","L","XL"], true, true),
        new(3, "Брюки Club Fleece", "Nike", "Брюки", 4990, 5990, "/images/products/club-joggers.webp", "Серый", ["S","M","L","XL","XXL"], true, true),
        new(4, "Легинсы One Dri-FIT", "Nike", "Легинсы", 5490, null, "/images/products/one-leggings.webp", "Серый", ["XS","S","M","L"], true, true),
        new(5, "Шорты Pro Dri-FIT", "Nike", "Шорты", 3490, 4490, "/images/products/pro-shorts.webp", "Чёрный", ["S","M","L","XL"], true),
        new(6, "Куртка Windrunner", "Nike", "Куртки", 8990, 10990, "/images/products/windrunner.webp", "Светло-серый", ["S","M","L","XL"], true),
        new(7, "Легинсы Optimé Stash", "Adidas", "Легинсы", 6290, 7490, "/images/products/adidas-leggings.webp", "Чёрный", ["XS","S","M","L"]),
        new(8, "Футболка Sportswear Club", "Nike", "Футболки", 3290, null, "/images/products/club-shirt.webp", "Белый", ["S","M","L","XL","XXL"])
    ];
    public IReadOnlyList<Review> Reviews { get; } = [
        new("Анна", "Легинсы Motion", 5, "Взяла для йоги. Ткань плотная, не просвечивает, пояс не сползает. Размер M подошёл.", new(2026,11,3)),
        new("Михаил", "Худи Club Fleece", 5, "Мягкое худи, удобно и на прогулке, и после тренировки. Ношу уже неделю.", new(2026,11,4)),
        new("Дарья", "Футболка Essential", 4, "Хорошая базовая футболка. Крой свободный, для более плотной посадки взяла бы на размер меньше.", new(2026,11,5))
    ];
    public IReadOnlyList<OrderRow> Orders { get; } = [
        new("ТМ-1024", "Анна Смирнова", new(2026,11,6), 9480, "Собирается"),
        new("ТМ-1023", "Михаил Орлов", new(2026,11,5), 6490, "В пути"),
        new("ТМ-1022", "Дарья Волкова", new(2026,11,4), 2990, "Получен"),
        new("ТМ-1021", "Иван Петров", new(2026,11,4), 11280, "Новый")
    ];
    public Product? Find(int id) => Products.FirstOrDefault(x => x.Id == id);
    public static string Money(decimal value) => value.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) + " ₽";
}
