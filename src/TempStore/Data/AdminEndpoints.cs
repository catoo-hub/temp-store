using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using TempStore.Models;
using TempStore.Services;

namespace TempStore.Data;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin");
        admin.AddEndpointFilter(async (context, next) =>
        {
            try
            {
                if (!HttpMethods.IsGet(context.HttpContext.Request.Method))
                {
                    var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
                    await antiforgery.ValidateRequestAsync(context.HttpContext);
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

        admin.MapGet("/session", (HttpContext context, AdminAccess access, IAntiforgery antiforgery,
            IWebHostEnvironment environment) =>
        {
            var imageDirectory = Path.Combine(environment.WebRootPath, "images", "products");
            var images = Directory.Exists(imageDirectory)
                ? Directory.EnumerateFiles(imageDirectory, "*.webp")
                    .Select(path => "/images/products/" + Path.GetFileName(path))
                    .Where(path => Regex.IsMatch(path, ProductEdit.ImagePattern))
                    .Order().ToArray()
                : [];

            return Results.Ok(new
            {
                authenticated = access.Configured && context.User.Identity?.IsAuthenticated == true,
                configured = access.Configured,
                token = antiforgery.GetAndStoreTokens(context).RequestToken,
                images
            });
        });

        admin.MapPost("/login", async (HttpContext context, AdminAccess access) =>
        {
            if (!access.Configured)
            {
                return Error(503, "Вход администратора пока не настроен");
            }

            if (!context.Request.HasJsonContentType())
            {
                return Error(400, "Отправьте данные в формате JSON");
            }

            var login = await context.Request.ReadFromJsonAsync<LoginRequest>();
            if (!access.VerifyPassword(login?.Password))
            {
                return Error(401, "Неверный пароль");
            }

            var identity = new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "admin"),
                new Claim("sessionId", access.SessionId)
            ],
                CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
            return Results.Ok(new
            {
            });
        }).RequireRateLimiting("admin-login");

        admin.MapPost("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new
            {
            });
        }).RequireAuthorization();

        admin.MapPost("/products", (HttpContext context, CatalogDatabase database,
            IWebHostEnvironment environment) => SaveProduct(context, database, environment, null))
            .RequireAuthorization();

        admin.MapPut("/products/{id:int}", (int id, HttpContext context, CatalogDatabase database,
            IWebHostEnvironment environment) => SaveProduct(context, database, environment, id))
            .RequireAuthorization();

        admin.MapDelete("/products/{id:int}", (int id, CatalogDatabase database) =>
            database.DeleteProduct(id) ? Results.NoContent() : Error(404, "Товар не найден"))
            .RequireAuthorization();
    }

    private static async Task<IResult> SaveProduct(HttpContext context, CatalogDatabase database,
        IWebHostEnvironment environment, int? id)
    {
        if (!context.Request.HasJsonContentType())
        {
            return Error(400, "Отправьте данные в формате JSON");
        }

        var product = await context.Request.ReadFromJsonAsync<ProductEdit>();
        if (product is null)
        {
            return Error(400, "Укажите данные товара");
        }

        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(product, new ValidationContext(product), errors, true))
        {
            return Error(400, string.Join(". ", errors.Select(error => error.ErrorMessage)));
        }

        if (!File.Exists(Path.Combine(environment.WebRootPath, product.Image.TrimStart('/'))))
        {
            return Error(400, "Изображение не найдено");
        }

        if (!database.ReadCatalog().Brands.Any(brand => brand.Name == product.Brand.Trim()))
        {
            return Error(400, "Выберите существующий бренд");
        }

        var saved = database.SaveProduct(product, id);
        if (saved is null)
        {
            return Error(404, "Товар не найден");
        }

        return id is null ? Results.Created($"/product/{saved.Id}", saved) : Results.Ok(saved);
    }

    private static IResult Error(int status, string message) => Results.Json(new { message }, statusCode: status);

    private sealed record LoginRequest(string? Password);
}
