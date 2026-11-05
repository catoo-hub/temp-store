using System.ComponentModel.DataAnnotations;

namespace TempStore.Models;

public record Product(int Id, string Name, string Brand, string Category, decimal Price,
    decimal? OldPrice, string Image, string Color, string[] Sizes, bool IsNew = false, bool Popular = false)
{
    public int Discount => OldPrice > Price ? (int)Math.Round((1 - Price / OldPrice.Value) * 100) : 0;
}
public record Brand(string Name, string Description);
public record CartLine(int ProductId, string Size, int Quantity);
public record OrderRow(string Number, string Customer, DateOnly Date, decimal Total, string Status);
public record Review(string Name, string Product, int Rating, string Text, DateOnly Date);
public record TabOption(string Id, string Label);

public class CheckoutModel
{
    [Required(ErrorMessage = "Укажите имя"), StringLength(80)] public string Name { get; set; } = "";
    [Required(ErrorMessage = "Укажите email"), EmailAddress(ErrorMessage = "Проверьте адрес email")] public string Email { get; set; } = "";
    [Required(ErrorMessage = "Укажите телефон"), Phone(ErrorMessage = "Проверьте номер телефона")] public string Phone { get; set; } = "";
    [Required(ErrorMessage = "Укажите адрес доставки"), StringLength(250)] public string Address { get; set; } = "";
    [StringLength(500)] public string Comment { get; set; } = "";
}
public class LoginModel
{
    [Required(ErrorMessage = "Укажите email"), EmailAddress(ErrorMessage = "Проверьте адрес email")] public string Email { get; set; } = "";
    [Required(ErrorMessage = "Укажите пароль"), MinLength(8, ErrorMessage = "Не менее 8 символов")] public string Password { get; set; } = "";
}
public class RegisterModel : LoginModel
{
    [Required(ErrorMessage = "Укажите имя"), StringLength(80)] public string Name { get; set; } = "";
    [Compare(nameof(Password), ErrorMessage = "Пароли не совпадают")] public string Confirmation { get; set; } = "";
}
public class ProductEditModel
{
    [Required(ErrorMessage = "Введите название"), StringLength(120)] public string Name { get; set; } = "";
    [Required(ErrorMessage = "Выберите бренд")] public string Brand { get; set; } = "";
    [Required(ErrorMessage = "Выберите категорию")] public string Category { get; set; } = "";
    [Range(1, 1000000, ErrorMessage = "Цена от 1 до 1 000 000 ₽")] public decimal Price { get; set; } = 2990;
    public bool IsNew { get; set; } = true;
}
public class FeedbackModel
{
    [Required(ErrorMessage = "Укажите имя")] public string Name { get; set; } = "";
    [Required(ErrorMessage = "Укажите email"), EmailAddress(ErrorMessage = "Проверьте адрес email")] public string Email { get; set; } = "";
    [Required(ErrorMessage = "Напишите сообщение"), StringLength(2000)] public string Message { get; set; } = "";
}
