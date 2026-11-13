using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Data.Sqlite;
using TempStore.Models;

namespace TempStore.Data;

public static class AccountEndpoints
{
    public const string Scheme = "Customer";

    public static void MapAccountEndpoints(this WebApplication app)
    {
        var account = app.MapGroup("/api/account");
        account.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var authentication = await http.AuthenticateAsync(Scheme);
            // Admin and customer cookies must produce separate identities and CSRF tokens.
            http.User = authentication.Principal ?? new ClaimsPrincipal(new ClaimsIdentity());
            try
            {
                if (!HttpMethods.IsGet(http.Request.Method))
                {
                    var antiforgery = http.RequestServices.GetRequiredService<IAntiforgery>();
                    await antiforgery.ValidateRequestAsync(http);
                    var path = http.Request.Path.Value!.TrimEnd('/');
                    if (!path.EndsWith("/logout") && !path.EndsWith("/pay") && !http.Request.HasJsonContentType())
                    {
                        return Error(400, "Проверьте данные запроса");
                    }
                }

                return await next(context);
            }
            catch (AntiforgeryValidationException)
            {
                return Error(400, "Обновите страницу и повторите действие");
            }
            catch (Exception exception) when (exception is JsonException or BadHttpRequestException)
            {
                return Error(400, "Проверьте данные запроса");
            }
        });

        account.MapGet("/session", (HttpContext context, AccountDatabase database, IAntiforgery antiforgery) =>
        {
            var customer = context.User.Identity?.IsAuthenticated == true
                ? database.Find(CustomerId(context))
                : null;
            return Results.Ok(new
            {
                authenticated = customer is not null,
                customer,
                token = antiforgery.GetAndStoreTokens(context).RequestToken
            });
        });

        account.MapPost("/register", async (HttpContext context, AccountDatabase database) =>
        {
            var registration = await context.Request.ReadFromJsonAsync<RegistrationModel>();
            if (registration is not null)
            {
                registration.Email = registration.Email?.Trim() ?? "";
            }

            var error = ValidationMessage(registration);
            if (error is not null)
            {
                return Error(400, error);
            }

            Customer customer;
            try
            {
                customer = database.Register(registration!);
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
            {
                return Error(409, "Аккаунт с таким email уже существует");
            }

            await SignIn(context, customer);
            return Results.Ok(new { });
        }).RequireRateLimiting("account-access");

        account.MapPost("/login", async (HttpContext context, AccountDatabase database) =>
        {
            var login = await context.Request.ReadFromJsonAsync<LoginModel>();
            if (login is not null)
            {
                login.Email = login.Email?.Trim() ?? "";
            }

            var error = ValidationMessage(login);
            if (error is not null)
            {
                return Error(400, error);
            }

            var customer = database.Login(login!.Email, login.Password);
            if (customer is null)
            {
                return Error(401, "Неверный email или пароль");
            }

            await SignIn(context, customer);
            return Results.Ok(new { });
        }).RequireRateLimiting("account-access");

        account.MapPost("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(Scheme);
            return Results.Ok(new { });
        }).RequireAuthorization("customer");

        account.MapPut("/profile", async (HttpContext context, AccountDatabase database) =>
        {
            var profile = await context.Request.ReadFromJsonAsync<ProfileModel>();
            var error = ValidationMessage(profile);
            if (error is not null)
            {
                return Error(400, error);
            }

            var customer = database.UpdateProfile(CustomerId(context), profile!);
            return customer is null ? Error(401, "Войдите в личный кабинет") : Results.Ok(customer);
        }).RequireAuthorization("customer");

        account.MapAccountOrders();
    }

    private static Task SignIn(HttpContext context, Customer customer)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, customer.Id.ToString())], Scheme);
        return context.SignInAsync(Scheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }

    private static int CustomerId(HttpContext context) =>
        int.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static string? ValidationMessage(object? model)
    {
        if (model is null)
        {
            return "Заполните форму";
        }

        var errors = new List<ValidationResult>();
        return Validator.TryValidateObject(model, new ValidationContext(model), errors, true)
            ? null
            : string.Join(". ", errors.Select(error => error.ErrorMessage));
    }

    private static IResult Error(int status, string message) => Results.Json(new { message }, statusCode: status);
}
