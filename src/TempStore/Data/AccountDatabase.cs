using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using TempStore.Models;

namespace TempStore.Data;

public sealed class AccountDatabase(string path)
{
    private readonly string connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = Path.GetFullPath(path)
    }.ToString();
    private readonly PasswordHasher<Customer> hasher = new();

    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Email TEXT NOT NULL UNIQUE COLLATE NOCASE,
                Phone TEXT NOT NULL DEFAULT '',
                PasswordHash TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public Customer Register(RegistrationModel registration)
    {
        var customer = new Customer(0, registration.Name.Trim(), registration.Email.Trim().ToLowerInvariant(), "");
        var passwordHash = hasher.HashPassword(customer, registration.Password);
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Users (Name, Email, PasswordHash)
            VALUES ($name, $email, $passwordHash)
            RETURNING Id;
            """;
        command.Parameters.AddWithValue("$name", customer.Name);
        command.Parameters.AddWithValue("$email", customer.Email);
        command.Parameters.AddWithValue("$passwordHash", passwordHash);
        return customer with { Id = Convert.ToInt32(command.ExecuteScalar()) };
    }

    public Customer? Find(int id)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Email, Phone FROM Users WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadCustomer(reader) : null;
    }

    public Customer? Login(string? email, string? password)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254 ||
            string.IsNullOrEmpty(password) || password.Length > 128)
        {
            return null;
        }

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Email, Phone, PasswordHash FROM Users WHERE Email = $email";
        command.Parameters.AddWithValue("$email", email.Trim().ToLowerInvariant());
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var customer = ReadCustomer(reader);
        var result = hasher.VerifyHashedPassword(customer, reader.GetString(4), password);
        return result == PasswordVerificationResult.Failed ? null : customer;
    }

    public Customer? UpdateProfile(int id, ProfileModel profile)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Users SET Name = $name, Phone = $phone WHERE Id = $id
            RETURNING Id, Name, Email, Phone;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", profile.Name.Trim());
        command.Parameters.AddWithValue("$phone", profile.Phone?.Trim() ?? "");
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadCustomer(reader) : null;
    }

    private static Customer ReadCustomer(SqliteDataReader reader) =>
        new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
}
