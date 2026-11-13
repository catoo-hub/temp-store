using System.Threading.RateLimiting;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using TempStore.Components;
using TempStore.Data;
using TempStore.Services;

var builder = WebApplication.CreateBuilder(args);
var databaseUrl = builder.Configuration["DATABASE_URL"];
if (string.IsNullOrWhiteSpace(databaseUrl))
{
    throw new InvalidOperationException("Задайте DATABASE_URL для подключения к PostgreSQL.");
}

var connectionString = DatabaseConfiguration.ConnectionString(databaseUrl);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<CatalogDatabase>();
builder.Services.AddSingleton<AccountDatabase>();
builder.Services.AddSingleton<OrderDatabase>();
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddSingleton<AdminAccess>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "temp-store.admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = false;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return context.Response.WriteAsJsonAsync(new
            {
                message = "Войдите в кабинет администратора"
            });
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return context.Response.WriteAsJsonAsync(new
            {
                message = "Доступ запрещён"
            });
        };
        options.Events.OnValidatePrincipal = context =>
        {
            var access = context.HttpContext.RequestServices.GetRequiredService<AdminAccess>();
            if (!access.Configured || context.Principal?.FindFirst("sessionId")?.Value != access.SessionId)
            {
                context.RejectPrincipal();
            }

            return Task.CompletedTask;
        };
    })
    .AddCookie(AccountEndpoints.Scheme, options =>
    {
        options.Cookie.Name = "temp-store.customer";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = false;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return context.Response.WriteAsJsonAsync(new { message = "Войдите в личный кабинет" });
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return context.Response.WriteAsJsonAsync(new { message = "Доступ запрещён" });
        };
        options.Events.OnValidatePrincipal = context =>
        {
            var database = context.HttpContext.RequestServices.GetRequiredService<AccountDatabase>();
            if (!int.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
                || database.Find(id) is null)
            {
                context.RejectPrincipal();
            }

            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization(options => options.AddPolicy("customer",
    new AuthorizationPolicyBuilder(AccountEndpoints.Scheme).RequireAuthenticatedUser().Build()));
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.HttpOnly = true;
});
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("admin-login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    options.AddPolicy("account-access", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, cancellationToken) => new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(
        new
        {
            message = "Слишком много попыток. Повторите через минуту"
        }, cancellationToken));
});

var app = builder.Build();
app.Services.GetRequiredService<CatalogDatabase>().Initialize(
    File.ReadAllText(Path.Combine(app.Environment.ContentRootPath, "Data", "catalog.sql")));
app.Services.GetRequiredService<AccountDatabase>().Initialize();
app.Services.GetRequiredService<OrderDatabase>().Initialize();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapGet("/api/catalog", (CatalogDatabase database) => database.ReadCatalog());
app.MapAdminEndpoints();
app.MapAccountEndpoints();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
