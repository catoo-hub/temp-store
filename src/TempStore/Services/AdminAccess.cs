using Microsoft.AspNetCore.Identity;

namespace TempStore.Services;

public sealed class AdminAccess
{
    private readonly PasswordHasher<AdminAccess> hasher = new();
    private readonly string? passwordHash;

    public AdminAccess(IConfiguration configuration)
    {
        var password = configuration["Admin:Password"];
        if (!string.IsNullOrWhiteSpace(password))
        {
            passwordHash = hasher.HashPassword(this, password);
        }
    }

    public bool Configured => passwordHash is not null;
    public string SessionId { get; } = Guid.NewGuid().ToString("N");

    public bool VerifyPassword(string? password) => Configured
        && !string.IsNullOrEmpty(password)
        && password.Length <= 256
        && hasher.VerifyHashedPassword(this, passwordHash!, password) != PasswordVerificationResult.Failed;
}
