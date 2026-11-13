using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TempStore.Models;

namespace TempStore.Data;

public sealed class OrderDatabase(string path)
{
    private readonly string connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = Path.GetFullPath(path),
        ForeignKeys = true
    }.ToString();

    public void Initialize()
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Orders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER NOT NULL REFERENCES Users(Id),
                RequestId TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                Name TEXT NOT NULL,
                Phone TEXT NOT NULL,
                Address TEXT NOT NULL,
                Status TEXT NOT NULL DEFAULT 'new'
                    CHECK (Status IN ('new', 'processing', 'shipped', 'completed', 'cancelled')),
                Paid INTEGER NOT NULL DEFAULT 0 CHECK (Paid IN (0, 1)),
                Total INTEGER NOT NULL CHECK (Total > 0),
                UNIQUE (CustomerId, RequestId)
            );
            CREATE TABLE IF NOT EXISTS OrderItems (
                Id INTEGER PRIMARY KEY,
                OrderId INTEGER NOT NULL REFERENCES Orders(Id) ON DELETE CASCADE,
                ProductId INTEGER NOT NULL,
                Name TEXT NOT NULL,
                Image TEXT NOT NULL,
                Size TEXT NOT NULL,
                Price INTEGER NOT NULL CHECK (Price > 0),
                Quantity INTEGER NOT NULL CHECK (Quantity BETWEEN 1 AND 10),
                UNIQUE (OrderId, ProductId, Size)
            );
            """;
        command.ExecuteNonQuery();
    }

    public StoreOrder[] GetForCustomer(int customerId) => ReadOrders(customerId);
    public StoreOrder[] GetAll() => ReadOrders();

    public StoreOrder Create(int customerId, CreateOrderModel model)
    {
        if (!Guid.TryParse(model.RequestId, out var requestId) || requestId == Guid.Empty)
        {
            throw new ArgumentException("Обновите страницу и повторите оформление");
        }

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM Users WHERE Id = $customerId";
        command.Parameters.AddWithValue("$customerId", customerId);
        if (Convert.ToInt32(command.ExecuteScalar()) == 0)
        {
            throw new ArgumentException("Пользователь не найден. Войдите снова");
        }

        command.CommandText = "SELECT Id FROM Orders WHERE CustomerId = $customerId AND RequestId = $requestId";
        command.Parameters.AddWithValue("$requestId", requestId.ToString("D"));
        if (command.ExecuteScalar() is long existingId)
        {
            transaction.Commit();
            return ReadOrders(customerId, (int)existingId).Single();
        }

        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(model, new ValidationContext(model), errors, true))
        {
            throw new ArgumentException(string.Join(". ", errors.Select(error => error.ErrorMessage)));
        }

        var items = new List<OrderItem>();
        long total = 0;
        command.CommandText = "SELECT Name, Image, Sizes, Price FROM Products WHERE Id = $id";
        command.Parameters.Clear();
        var productId = command.Parameters.Add("$id", SqliteType.Integer);
        foreach (var line in model.Items)
        {
            productId.Value = line.ProductId;
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                throw new ArgumentException("Один из товаров больше не доступен. Обновите корзину");
            }

            var sizes = JsonSerializer.Deserialize<string[]>(reader.GetString(2)) ?? [];
            if (!sizes.Contains(line.Size))
            {
                throw new ArgumentException($"Размер {line.Size} для товара «{reader.GetString(0)}» недоступен");
            }

            var price = reader.GetInt64(3);
            total = checked(total + price * line.Quantity);
            items.Add(new(line.ProductId, reader.GetString(0), reader.GetString(1),
                line.Size, price / 100m, line.Quantity));
        }

        var createdAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        command.CommandText = """
            INSERT INTO Orders (CustomerId, RequestId, CreatedAt, Name, Phone, Address, Total)
            VALUES ($customerId, $requestId, $createdAt, $name, $phone, $address, $total)
            RETURNING Id;
            """;
        command.Parameters.Clear();
        command.Parameters.AddWithValue("$customerId", customerId);
        command.Parameters.AddWithValue("$requestId", requestId.ToString("D"));
        command.Parameters.AddWithValue("$createdAt", createdAt);
        command.Parameters.AddWithValue("$name", model.Name.Trim());
        command.Parameters.AddWithValue("$phone", model.Phone.Trim());
        command.Parameters.AddWithValue("$address", model.Address.Trim());
        command.Parameters.AddWithValue("$total", total);
        var orderId = Convert.ToInt32(command.ExecuteScalar());

        command.CommandText = """
            INSERT INTO OrderItems (OrderId, ProductId, Name, Image, Size, Price, Quantity)
            VALUES ($orderId, $productId, $name, $image, $size, $price, $quantity);
            """;
        foreach (var item in items)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$orderId", orderId);
            command.Parameters.AddWithValue("$productId", item.ProductId);
            command.Parameters.AddWithValue("$name", item.Name);
            command.Parameters.AddWithValue("$image", item.Image);
            command.Parameters.AddWithValue("$size", item.Size);
            command.Parameters.AddWithValue("$price", (long)(item.Price * 100));
            command.Parameters.AddWithValue("$quantity", item.Quantity);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        return new(orderId, createdAt, model.Name.Trim(), model.Phone.Trim(), model.Address.Trim(),
            "new", false, total / 100m, items.ToArray());
    }

    public StoreOrder? Pay(int customerId, int orderId)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Status FROM Orders WHERE Id = $id AND CustomerId = $customerId";
        command.Parameters.AddWithValue("$id", orderId);
        command.Parameters.AddWithValue("$customerId", customerId);
        var status = command.ExecuteScalar() as string;
        if (status is null)
        {
            return null;
        }
        if (status == "cancelled")
        {
            throw new InvalidOperationException("Отменённый заказ нельзя оплатить");
        }

        // shortcut: payments are simulated; use provider confirmation for real charges.
        command.CommandText = "UPDATE Orders SET Paid = 1 WHERE Id = $id AND CustomerId = $customerId";
        command.ExecuteNonQuery();
        transaction.Commit();
        return ReadOrders(customerId, orderId).Single();
    }

    public StoreOrder? ChangeStatus(int orderId, string status)
    {
        if (status is not ("new" or "processing" or "shipped" or "completed" or "cancelled"))
        {
            throw new ArgumentException("Выберите доступный статус заказа");
        }

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Status, Paid FROM Orders WHERE Id = $id";
        command.Parameters.AddWithValue("$id", orderId);
        string current;
        bool paid;
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read())
            {
                return null;
            }
            current = reader.GetString(0);
            paid = reader.GetBoolean(1);
        }

        if (status != current)
        {
            if (!OrderStatus.Next(current).Contains(status))
            {
                throw new InvalidOperationException("Этот переход статуса недоступен");
            }
            if (status == "processing" && !paid)
            {
                throw new InvalidOperationException("Сначала заказ нужно оплатить");
            }

            command.CommandText = "UPDATE Orders SET Status = $status WHERE Id = $id";
            command.Parameters.AddWithValue("$status", status);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        return ReadOrders(orderId: orderId).Single();
    }

    private StoreOrder[] ReadOrders(int? customerId = null, int? orderId = null)
    {
        // shortcut: load full histories while order volume is small; paginate when it grows.
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, CreatedAt, Name, Phone, Address, Status, Paid, Total
            FROM Orders
            WHERE ($customerId IS NULL OR CustomerId = $customerId) AND ($orderId IS NULL OR Id = $orderId)
            ORDER BY Id DESC;
            """;
        command.Parameters.AddWithValue("$customerId", (object?)customerId ?? DBNull.Value);
        command.Parameters.AddWithValue("$orderId", (object?)orderId ?? DBNull.Value);
        var orders = new List<StoreOrder>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                orders.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.GetBoolean(6), reader.GetInt64(7) / 100m, []));
            }
        }

        command.CommandText = """
            SELECT ProductId, Name, Image, Size, Price, Quantity
            FROM OrderItems WHERE OrderId = $id ORDER BY Id;
            """;
        command.Parameters.Clear();
        var id = command.Parameters.Add("$id", SqliteType.Integer);
        for (var index = 0; index < orders.Count; index++)
        {
            id.Value = orders[index].Id;
            var items = new List<OrderItem>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetInt64(4) / 100m, reader.GetInt32(5)));
            }
            orders[index] = orders[index] with { Items = items.ToArray() };
        }

        return orders.ToArray();
    }
}
