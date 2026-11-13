using System.ComponentModel.DataAnnotations;

namespace TempStore.Models;

public sealed class CreateOrderModel : IValidatableObject
{
    [Required(ErrorMessage = "Обновите страницу и повторите оформление")]
    public string RequestId { get; set; } = "";

    [Required(ErrorMessage = "Укажите имя получателя")]
    [StringLength(80, ErrorMessage = "Имя должно быть не длиннее 80 символов")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Укажите телефон")]
    [Phone(ErrorMessage = "Проверьте номер телефона")]
    [StringLength(40, ErrorMessage = "Номер телефона слишком длинный")]
    public string Phone { get; set; } = "";

    [Required(ErrorMessage = "Укажите адрес доставки")]
    [StringLength(300, ErrorMessage = "Адрес должен быть не длиннее 300 символов")]
    public string Address { get; set; } = "";

    [Required(ErrorMessage = "Добавьте товары в корзину")]
    public CartLine[] Items { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Guid.TryParse(RequestId, out var id) || id == Guid.Empty)
        {
            yield return new("Обновите страницу и повторите оформление", [nameof(RequestId)]);
        }

        if (Items is null || Items.Length is < 1 or > 100)
        {
            yield return new("В заказе должно быть от 1 до 100 позиций", [nameof(Items)]);
            yield break;
        }

        var seen = new HashSet<(int, string)>();
        foreach (var item in Items)
        {
            if (item is null || item.ProductId <= 0 || item.Quantity is < 1 or > 10 ||
                string.IsNullOrWhiteSpace(item.Size) || item.Size.Length > 10)
            {
                yield return new("Проверьте товары, размеры и количество в корзине", [nameof(Items)]);
            }
            else if (!seen.Add((item.ProductId, item.Size)))
            {
                yield return new("В корзине повторяется товар одного размера", [nameof(Items)]);
            }
        }
    }
}

public record StoreOrder(
    int Id,
    string CreatedAt,
    string Name,
    string Phone,
    string Address,
    string Status,
    bool Paid,
    decimal Total,
    OrderItem[] Items);

public record OrderItem(int ProductId, string Name, string Image, string Size, decimal Price, int Quantity);

public static class OrderStatus
{
    public static string Label(string status) => status switch
    {
        "new" => "Новый",
        "processing" => "В обработке",
        "shipped" => "Отправлен",
        "completed" => "Завершён",
        "cancelled" => "Отменён",
        _ => "Неизвестный статус"
    };

    public static string[] Next(string status) => status switch
    {
        "new" => ["processing", "cancelled"],
        "processing" => ["shipped", "cancelled"],
        "shipped" => ["completed"],
        _ => []
    };
}
