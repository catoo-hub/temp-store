using System.Text.Json;
using Microsoft.Data.Sqlite;
using TempStore.Models;

namespace TempStore.Data;

public sealed class CatalogDatabase(string path)
{
    private readonly string connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = Path.GetFullPath(path), ForeignKeys = true
    }.ToString();

    public void Initialize(string seedSql)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version";
        if (Convert.ToInt32(command.ExecuteScalar()) != 0) return;
        command.CommandText = seedSql;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public CatalogData ReadCatalog()
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Brand, Category, Price, OldPrice, Image, Color, Sizes, IsNew, Popular FROM Products ORDER BY Id";
        var products = new List<Product>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
                products.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetInt64(4) / 100m, reader.IsDBNull(5) ? null : reader.GetInt64(5) / 100m,
                    reader.GetString(6), reader.GetString(7), JsonSerializer.Deserialize<string[]>(reader.GetString(8))!,
                    reader.GetBoolean(9), reader.GetBoolean(10)));
        }
        command.CommandText = "SELECT Name, Description FROM Brands ORDER BY Name";
        var brands = new List<Brand>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read()) brands.Add(new(reader.GetString(0), reader.GetString(1)));
        }
        command.CommandText = "SELECT r.Name, p.Name, r.Rating, r.Text, r.Date FROM Reviews r JOIN Products p ON p.Id = r.ProductId ORDER BY r.Date DESC, r.Id DESC";
        var reviews = new List<Review>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read()) reviews.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2),
                reader.GetString(3), DateOnly.ParseExact(reader.GetString(4), "yyyy-MM-dd")));
        }
        return new(products.ToArray(), brands.ToArray(), reviews.ToArray());
    }
}
