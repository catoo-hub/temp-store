using System.ComponentModel.DataAnnotations;

namespace TempStore.Models;

public sealed class ProductEdit : IValidatableObject
{
    public const string ImagePattern = @"^/images/products/[A-Za-z0-9][A-Za-z0-9_-]*\.webp$";

    [Required(ErrorMessage = "Укажите название")]
    [StringLength(120, ErrorMessage = "Название должно быть не длиннее 120 символов")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Выберите бренд")]
    [StringLength(60, ErrorMessage = "Название бренда должно быть не длиннее 60 символов")]
    public string Brand { get; set; } = "";

    [Required(ErrorMessage = "Укажите категорию")]
    [StringLength(60, ErrorMessage = "Категория должна быть не длиннее 60 символов")]
    public string Category { get; set; } = "";

    public decimal Price { get; set; }
    public decimal? OldPrice { get; set; }

    [Required(ErrorMessage = "Выберите изображение")]
    [StringLength(180, ErrorMessage = "Путь изображения должен быть не длиннее 180 символов")]
    [RegularExpression(ImagePattern, ErrorMessage = "Выберите изображение из каталога")]
    public string Image { get; set; } = "";

    [Required(ErrorMessage = "Укажите цвет")]
    [StringLength(60, ErrorMessage = "Цвет должен быть не длиннее 60 символов")]
    public string Color { get; set; } = "";

    [Required(ErrorMessage = "Укажите размеры")]
    [StringLength(80, ErrorMessage = "Список размеров должен быть не длиннее 80 символов")]
    public string Sizes { get; set; } = "S, M, L";

    public bool IsNew { get; set; }
    public bool Popular { get; set; }

    public string[] ParseSizes() => (Sizes ?? "").Split(',',
        StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Price <= 0 || Price > 1_000_000 || decimal.Round(Price, 2) != Price)
        {
            yield return new("Цена должна быть от 0,01 до 1 000 000 ₽, не более двух знаков после запятой", [nameof(Price)]);
        }

        if (OldPrice is decimal old && (old <= Price || old > 1_000_000 || decimal.Round(old, 2) != old))
        {
            yield return new("Старая цена должна быть больше текущей, не выше 1 000 000 ₽ и с точностью до копеек", [nameof(OldPrice)]);
        }

        var sizes = ParseSizes();
        string[] allowedSizes = ["XS", "S", "M", "L", "XL", "XXL"];
        if (sizes.Length == 0 || sizes.Distinct().Count() != sizes.Length || sizes.Any(size => !allowedSizes.Contains(size)))
        {
            yield return new("Укажите размеры через запятую без повторов: XS, S, M, L, XL, XXL", [nameof(Sizes)]);
        }
    }
}
