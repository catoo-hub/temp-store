using System.ComponentModel.DataAnnotations;

namespace TempStore.Models;

public record Product(
    int Id,
    string Name,
    string Brand,
    string Category,
    decimal Price,
    decimal? OldPrice,
    string Image,
    string Color,
    string[] Sizes,
    bool IsNew = false,
    bool Popular = false)
{
    public int Discount => OldPrice > Price ? (int)Math.Round((1 - Price / OldPrice.Value) * 100) : 0;
}
public record Brand(string Name, string Description);
public record CartLine(int ProductId, string Size, int Quantity);
public record Review(string Name, string Product, int Rating, string Text, DateOnly Date);

public class FeedbackModel
{
    [Required(ErrorMessage = "Укажите имя")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Укажите email")]
    [EmailAddress(ErrorMessage = "Проверьте адрес email")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Напишите сообщение")]
    [StringLength(2000)]
    public string Message { get; set; } = "";
}
