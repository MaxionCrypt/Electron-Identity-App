using Microsoft.Extensions.Configuration;

namespace Identity;

public sealed record RegisterRequest(string FirstName, string LastName, string Email, string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record UserDto(Guid Id, string FirstName, string LastName, string Email, string Role, DateTimeOffset CreatedAtUtc);

public sealed record AuthResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAtUtc, UserDto User);

public static class RoleNames
{
    public const string User = "User";
    public const string Admin = "Admin";
}

internal static class AuthPolicies
{
    public const string AdminOnly = "AdminOnly";
}

public sealed class JwtOptions
{
    public string Secret { get; init; } = string.Empty;
    public string Issuer { get; init; } = "Electron.ModularMonolith";
    public string Audience { get; init; } = "Electron.DesktopClient";
    public int ExpiresMinutes { get; init; } = 60;

    public static JwtOptions FromConfiguration(IConfiguration configuration)
    {
        var secret = configuration["JWT_SECRET"] ?? configuration["Jwt:Secret"] ?? string.Empty;
        var issuer = configuration["JWT_ISSUER"] ?? configuration["Jwt:Issuer"] ?? "Electron.ModularMonolith";
        var audience = configuration["JWT_AUDIENCE"] ?? configuration["Jwt:Audience"] ?? "Electron.DesktopClient";
        var expiresRaw = configuration["JWT_EXPIRES_MINUTES"] ?? configuration["Jwt:ExpiresMinutes"];
        var expires = int.TryParse(expiresRaw, out var parsedExpires) && parsedExpires > 0 ? parsedExpires : 60;

        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
        {
            throw new InvalidOperationException(
                "JWT secret is missing or too short. Configure JWT_SECRET (minimum 32 chars)."
            );
        }

        return new JwtOptions
        {
            Secret = secret,
            Issuer = issuer,
            Audience = audience,
            ExpiresMinutes = expires
        };
    }
}

public sealed class IdentitySeedOptions
{
    public string AdminEmail { get; init; } = "admin@local.dev";
    public string AdminPassword { get; init; } = "Admin123!";
    public string AdminFirstName { get; init; } = "System";
    public string AdminLastName { get; init; } = "Administrator";

    public static IdentitySeedOptions FromConfiguration(IConfiguration configuration)
    {
        return new IdentitySeedOptions
        {
            AdminEmail = configuration["IDENTITY_SEED_ADMIN_EMAIL"] ?? "admin@local.dev",
            AdminPassword = configuration["IDENTITY_SEED_ADMIN_PASSWORD"] ?? "Admin123!",
            AdminFirstName = configuration["IDENTITY_SEED_ADMIN_FIRST_NAME"] ?? "System",
            AdminLastName = configuration["IDENTITY_SEED_ADMIN_LAST_NAME"] ?? "Administrator"
        };
    }
}

internal sealed record NewUserRegistration(string FirstName, string LastName, string Email, string Password, string Role);

internal sealed record PasswordHashResult(string HashBase64, string SaltBase64);

internal sealed record JwtTokenResult(string AccessToken, DateTimeOffset ExpiresAtUtc);

internal sealed record AppUser(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string NormalizedEmail,
    string PasswordHashBase64,
    string PasswordSaltBase64,
    string Role,
    DateTimeOffset CreatedAtUtc
);
