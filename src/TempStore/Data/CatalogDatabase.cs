using System.Text.Json;
using Microsoft.Data.Sqlite;
using TempStore.Models;

namespace TempStore.Data;

public sealed class CatalogDatabase(string path)
{
    private readonly string connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = Path.GetFullPath(path),
        ForeignKeys = true
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
        if (Convert.ToInt32(command.ExecuteScalar()) == 0)
        {
            command.CommandText = seedSql;
            command.ExecuteNonQuery();
        }

        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ProductSequence (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                NextId INTEGER NOT NULL
            );
            INSERT OR IGNORE INTO ProductSequence (Id, NextId)
                SELECT 1, COALESCE(MAX(Id), 0) + 1 FROM Products;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public CatalogData ReadCatalog()
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Brand, Category, Price, OldPrice, Image, Color, Sizes, IsNew, Popular
            FROM Products ORDER BY Id
            """;
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
            while (reader.Read())
                brands.Add(new(reader.GetString(0), reader.GetString(1)));
        }
        command.CommandText = """
            SELECT r.Name, p.Name, r.Rating, r.Text, r.Date
            FROM Reviews r JOIN Products p ON p.Id = r.ProductId
            ORDER BY r.Date DESC, r.Id DESC
            """;
        var reviews = new List<Review>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
                reviews.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2),
                reader.GetString(3), DateOnly.ParseExact(reader.GetString(4), "yyyy-MM-dd")));
        }
        return new(products.ToArray(), brands.ToArray(), reviews.ToArray());
    }

    public Product? SaveProduct(ProductEdit edit, int? id = null)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var productId = id;
        if (productId is null)
        {
            command.CommandText = "UPDATE ProductSequence SET NextId = NextId + 1 WHERE Id = 1 RETURNING NextId - 1";
            productId = Convert.ToInt32(command.ExecuteScalar());
        }

        var product = new Product(productId.Value, edit.Name.Trim(), edit.Brand.Trim(), edit.Category.Trim(),
            edit.Price, edit.OldPrice, edit.Image, edit.Color.Trim(), edit.ParseSizes(), edit.IsNew, edit.Popular);
        command.CommandText = id is null
            ? """
              INSERT INTO Products (Id, Name, Brand, Category, Price, OldPrice, Image, Color, Sizes, IsNew, Popular)
              VALUES ($id, $name, $brand, $category, $price, $oldPrice, $image, $color, $sizes, $isNew, $popular);
              """
            : """
              UPDATE Products SET Name = $name, Brand = $brand, Category = $category,
                  Price = $price, OldPrice = $oldPrice, Image = $image, Color = $color,
                  Sizes = $sizes, IsNew = $isNew, Popular = $popular
              WHERE Id = $id;
              """;
        command.Parameters.AddWithValue("$id", product.Id);
        command.Parameters.AddWithValue("$name", product.Name);
        command.Parameters.AddWithValue("$brand", product.Brand);
        command.Parameters.AddWithValue("$category", product.Category);
        command.Parameters.AddWithValue("$price", (long)(product.Price * 100));
        command.Parameters.AddWithValue("$oldPrice", product.OldPrice is decimal old ? (object)(long)(old * 100) : DBNull.Value);
        command.Parameters.AddWithValue("$image", product.Image);
        command.Parameters.AddWithValue("$color", product.Color);
        command.Parameters.AddWithValue("$sizes", JsonSerializer.Serialize(product.Sizes));
        command.Parameters.AddWithValue("$isNew", product.IsNew);
        command.Parameters.AddWithValue("$popular", product.Popular);
        var changed = command.ExecuteNonQuery() > 0;
        transaction.Commit();
        return changed ? product : null;
    }

    public bool DeleteProduct(int id)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$id", id);
        command.CommandText = "DELETE FROM Reviews WHERE ProductId = $id";
        command.ExecuteNonQuery();
        command.CommandText = "DELETE FROM Products WHERE Id = $id";
        var deleted = command.ExecuteNonQuery() > 0;
        transaction.Commit();
        return deleted;
    }
}
