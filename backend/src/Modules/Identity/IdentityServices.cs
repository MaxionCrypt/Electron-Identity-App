using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Identity;

internal interface IIdentityUserStore
{
    Task<(bool Created, AppUser? User)> TryCreateAsync(NewUserRegistration registration, CancellationToken cancellationToken = default);
    Task<AppUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<AppUser>> ListAsync(CancellationToken cancellationToken = default);
}

internal sealed class PasswordHasher
{
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int Iterations = 120_000;

    public PasswordHashResult HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password is required.", nameof(password));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSizeBytes
        );

        return new PasswordHashResult(
            Convert.ToBase64String(hash),
            Convert.ToBase64String(salt)
        );
    }

    public bool VerifyPassword(string password, string hashBase64, string saltBase64)
    {
        if (string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(hashBase64) ||
            string.IsNullOrWhiteSpace(saltBase64))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(saltBase64);
            var expectedHash = Convert.FromBase64String(hashBase64);
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                expectedHash.Length
            );

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

internal sealed class InMemoryIdentityUserStore : IIdentityUserStore
{
    private readonly ConcurrentDictionary<string, AppUser> _usersByEmail = new(StringComparer.Ordinal);
    private readonly PasswordHasher _passwordHasher;

    public InMemoryIdentityUserStore(PasswordHasher passwordHasher, IdentitySeedOptions seedOptions)
    {
        _passwordHasher = passwordHasher;
        SeedAdmin(seedOptions);
    }

    public Task<(bool Created, AppUser? User)> TryCreateAsync(
        NewUserRegistration registration,
        CancellationToken cancellationToken = default
    )
    {
        _ = cancellationToken;

        var email = registration.Email.Trim();
        var normalizedEmail = NormalizeEmail(email);
        var hashedPassword = _passwordHasher.HashPassword(registration.Password);
        var user = new AppUser(
            Guid.NewGuid(),
            registration.FirstName.Trim(),
            registration.LastName.Trim(),
            email,
            normalizedEmail,
            hashedPassword.HashBase64,
            hashedPassword.SaltBase64,
            registration.Role,
            DateTimeOffset.UtcNow
        );

        var created = _usersByEmail.TryAdd(normalizedEmail, user);
        return Task.FromResult((created, created ? user : null));
    }

    public Task<AppUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        if (string.IsNullOrWhiteSpace(email))
        {
            return Task.FromResult<AppUser?>(null);
        }

        var normalizedEmail = NormalizeEmail(email);
        _usersByEmail.TryGetValue(normalizedEmail, out var user);
        return Task.FromResult(user);
    }

    public Task<IReadOnlyCollection<AppUser>> ListAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        IReadOnlyCollection<AppUser> users = _usersByEmail.Values
            .OrderByDescending(user => user.CreatedAtUtc)
            .ToArray();
        return Task.FromResult(users);
    }

    private void SeedAdmin(IdentitySeedOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AdminPassword) || options.AdminPassword.Length < 8)
        {
            throw new InvalidOperationException(
                "IDENTITY_SEED_ADMIN_PASSWORD must be set and contain at least 8 characters."
            );
        }

        var registration = new NewUserRegistration(
            options.AdminFirstName,
            options.AdminLastName,
            options.AdminEmail,
            options.AdminPassword,
            RoleNames.Admin
        );

        var created = TryCreateAsync(registration).GetAwaiter().GetResult();
        if (!created.Created)
        {
            throw new InvalidOperationException("Failed to seed admin user.");
        }
    }

    private static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();
}

internal sealed class JwtTokenService
{
    private readonly JwtOptions _jwtOptions;
    private readonly SigningCredentials _signingCredentials;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    public JwtTokenService(JwtOptions jwtOptions)
    {
        _jwtOptions = jwtOptions;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.Secret));
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public JwtTokenResult CreateToken(AppUser user)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAtUtc = now.AddMinutes(_jwtOptions.ExpiresMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, $"{user.FirstName} {user.LastName}".Trim()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role)
        };

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAtUtc.UtcDateTime,
            signingCredentials: _signingCredentials
        );

        return new JwtTokenResult(
            _tokenHandler.WriteToken(token),
            expiresAtUtc
        );
    }
}
