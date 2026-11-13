using System.ComponentModel.DataAnnotations;

namespace TempStore.Models;

public record Customer(int Id, string Name, string Email, string Phone);

public sealed class RegistrationModel
{
    [Required(ErrorMessage = "Укажите имя")]
    [StringLength(80, ErrorMessage = "Имя должно быть не длиннее 80 символов")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Укажите email")]
    [EmailAddress(ErrorMessage = "Проверьте адрес email")]
    [StringLength(254, ErrorMessage = "Адрес email слишком длинный")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Укажите пароль")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Пароль должен содержать от 8 до 128 символов")]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Повторите пароль")]
    [Compare(nameof(Password), ErrorMessage = "Пароли не совпадают")]
    public string Confirmation { get; set; } = "";
}

public sealed class LoginModel
{
    [Required(ErrorMessage = "Укажите email")]
    [EmailAddress(ErrorMessage = "Проверьте адрес email")]
    [StringLength(254, ErrorMessage = "Адрес email слишком длинный")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Укажите пароль")]
    [StringLength(128, ErrorMessage = "Пароль слишком длинный")]
    public string Password { get; set; } = "";
}

public sealed class ProfileModel
{
    private string? phone;

    [Required(ErrorMessage = "Укажите имя")]
    [StringLength(80, ErrorMessage = "Имя должно быть не длиннее 80 символов")]
    public string Name { get; set; } = "";

    [Phone(ErrorMessage = "Проверьте номер телефона")]
    [StringLength(40, ErrorMessage = "Номер телефона слишком длинный")]
    public string? Phone
    {
        get => phone;
        set => phone = string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
