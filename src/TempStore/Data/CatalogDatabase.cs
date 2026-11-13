using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using TempStore.Models;

namespace TempStore.Data;

public sealed class CatalogDatabase(NpgsqlDataSource database)
{
    public void Initialize(string seedSql)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // Serialize first-time seeding when two instances start together.
        command.CommandText = "SELECT pg_advisory_xact_lock(1734001)";
        command.ExecuteNonQuery();
        command.CommandText = "SELECT to_regclass('products')::text";
        if (command.ExecuteScalar() is null or DBNull)
        {
            command.CommandText = seedSql;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public CatalogData ReadCatalog()
    {
        using var connection = database.OpenConnection();
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
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        var product = new Product(id ?? 0, edit.Name.Trim(), edit.Brand.Trim(), edit.Category.Trim(),
            edit.Price, edit.OldPrice, edit.Image, edit.Color.Trim(), edit.ParseSizes(), edit.IsNew, edit.Popular);
        command.CommandText = id is null
            ? """
              INSERT INTO Products (Name, Brand, Category, Price, OldPrice, Image, Color, Sizes, IsNew, Popular)
              VALUES (@name, @brand, @category, @price, @oldPrice, @image, @color, @sizes, @isNew, @popular)
              RETURNING Id;
              """
            : """
              UPDATE Products SET Name = @name, Brand = @brand, Category = @category,
                  Price = @price, OldPrice = @oldPrice, Image = @image, Color = @color,
                  Sizes = @sizes, IsNew = @isNew, Popular = @popular
              WHERE Id = @id RETURNING Id;
              """;
        if (id is not null)
            command.Parameters.AddWithValue("id", id.Value);
        command.Parameters.AddWithValue("name", product.Name);
        command.Parameters.AddWithValue("brand", product.Brand);
        command.Parameters.AddWithValue("category", product.Category);
        command.Parameters.AddWithValue("price", (long)(product.Price * 100));
        command.Parameters.AddWithValue("oldPrice", NpgsqlDbType.Bigint, product.OldPrice is decimal old ? (object)(long)(old * 100) : DBNull.Value);
        command.Parameters.AddWithValue("image", product.Image);
        command.Parameters.AddWithValue("color", product.Color);
        command.Parameters.AddWithValue("sizes", JsonSerializer.Serialize(product.Sizes));
        command.Parameters.AddWithValue("isNew", product.IsNew);
        command.Parameters.AddWithValue("popular", product.Popular);
        return command.ExecuteScalar() is int savedId ? product with { Id = savedId } : null;
    }

    public bool DeleteProduct(int id)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("id", id);
        command.CommandText = "DELETE FROM Reviews WHERE ProductId = @id";
        command.ExecuteNonQuery();
        command.CommandText = "DELETE FROM Products WHERE Id = @id";
        var deleted = command.ExecuteNonQuery() > 0;
        transaction.Commit();
        return deleted;
    }
}
