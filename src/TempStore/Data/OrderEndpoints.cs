using System.Security.Claims;
using TempStore.Models;

namespace TempStore.Data;

public static class OrderEndpoints
{
    public static void MapAccountOrders(this RouteGroupBuilder account)
    {
        var orders = account.MapGroup("/orders").RequireAuthorization("customer");
        AddOrderErrors(orders);

        orders.MapGet("", (HttpContext context, OrderDatabase database) =>
            database.GetForCustomer(CustomerId(context)));

        orders.MapPost("", async (HttpContext context, OrderDatabase database) =>
        {
            var model = await context.Request.ReadFromJsonAsync<CreateOrderModel>();
            return model is null
                ? Error(400, "Заполните данные заказа")
                : Results.Ok(database.Create(CustomerId(context), model));
        });

        orders.MapPost("/{id:int}/pay", (int id, HttpContext context, OrderDatabase database) =>
        {
            var order = database.Pay(CustomerId(context), id);
            return order is null ? Error(404, "Заказ не найден") : Results.Ok(order);
        });
    }

    public static void MapAdminOrders(this RouteGroupBuilder admin)
    {
        var orders = admin.MapGroup("/orders").RequireAuthorization();
        AddOrderErrors(orders);

        orders.MapGet("", (OrderDatabase database) => database.GetAll());

        orders.MapPut("/{id:int}/status", async (int id, HttpContext context, OrderDatabase database) =>
        {
            if (!context.Request.HasJsonContentType())
            {
                return Error(400, "Выберите статус заказа");
            }

            var model = await context.Request.ReadFromJsonAsync<StatusRequest>();
            if (string.IsNullOrWhiteSpace(model?.Status))
            {
                return Error(400, "Выберите статус заказа");
            }

            var order = database.ChangeStatus(id, model.Status);
            return order is null ? Error(404, "Заказ не найден") : Results.Ok(order);
        });
    }

    private static void AddOrderErrors(RouteGroupBuilder orders)
    {
        orders.AddEndpointFilter(async (context, next) =>
        {
            try
            {
                return await next(context);
            }
            catch (ArgumentException exception)
            {
                return Error(400, exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return Error(409, exception.Message);
            }
        });
    }

    private static int CustomerId(HttpContext context) =>
        int.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static IResult Error(int status, string message) => Results.Json(new { message }, statusCode: status);

    private sealed record StatusRequest(string Status);
}
