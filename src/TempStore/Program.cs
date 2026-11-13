using TempStore.Components;
using TempStore.Data;
using TempStore.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddSingleton(new CatalogDatabase(Path.GetFullPath(
    builder.Configuration["Database:Path"] ?? "App_Data/store.db", builder.Environment.ContentRootPath)));
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<ToastService>();

var app = builder.Build();
app.Services.GetRequiredService<CatalogDatabase>().Initialize(
    File.ReadAllText(Path.Combine(app.Environment.ContentRootPath, "Data", "catalog.sql")));

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapGet("/api/catalog", (CatalogDatabase database) => database.ReadCatalog());

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
