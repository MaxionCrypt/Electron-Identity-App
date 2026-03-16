using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Identity;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/auth").WithTags("Identity");
        auth.MapPost("/register", Register).AllowAnonymous();
        auth.MapPost("/login", Login).AllowAnonymous();
        auth.MapGet("/me", Me).RequireAuthorization();

        var admin = endpoints.MapGroup("/api/admin").WithTags("Admin").RequireAuthorization(AuthPolicies.AdminOnly);
        admin.MapGet("/users", ListUsers);

        return endpoints;
    }

    private static async Task<IResult> Register(
        RegisterRequest request,
        IIdentityUserStore userStore,
        JwtTokenService jwtTokenService,
        CancellationToken cancellationToken
    )
    {
        var firstName = request.FirstName?.Trim() ?? string.Empty;
        var lastName = request.LastName?.Trim() ?? string.Empty;
        var email = request.Email?.Trim() ?? string.Empty;
        var password = request.Password ?? string.Empty;

        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            return Results.BadRequest(new { error = "First name and last name are required." });
        }

        if (!IsValidEmail(email))
        {
            return Results.BadRequest(new { error = "A valid email address is required." });
        }

        if (password.Length < 8)
        {
            return Results.BadRequest(new { error = "Password must contain at least 8 characters." });
        }

        var creation = await userStore.TryCreateAsync(
            new NewUserRegistration(firstName, lastName, email, password, RoleNames.User),
            cancellationToken
        );

        if (!creation.Created || creation.User is null)
        {
            return Results.Conflict(new { error = "An account with this email already exists." });
        }

        var token = jwtTokenService.CreateToken(creation.User);
        var response = ToAuthResponse(creation.User, token);
        return Results.Ok(response);
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        IIdentityUserStore userStore,
        PasswordHasher passwordHasher,
        JwtTokenService jwtTokenService,
        CancellationToken cancellationToken
    )
    {
        var email = request.Email?.Trim() ?? string.Empty;
        var password = request.Password ?? string.Empty;

        if (!IsValidEmail(email) || string.IsNullOrWhiteSpace(password))
        {
            return Results.BadRequest(new { error = "Email and password are required." });
        }

        var user = await userStore.FindByEmailAsync(email, cancellationToken);
        if (user is null ||
            !passwordHasher.VerifyPassword(password, user.PasswordHashBase64, user.PasswordSaltBase64))
        {
            return Results.Unauthorized();
        }

        var token = jwtTokenService.CreateToken(user);
        var response = ToAuthResponse(user, token);
        return Results.Ok(response);
    }

    private static async Task<IResult> Me(
        ClaimsPrincipal principal,
        IIdentityUserStore userStore,
        CancellationToken cancellationToken
    )
    {
        var email = principal.FindFirst(ClaimTypes.Email)?.Value;
        if (string.IsNullOrWhiteSpace(email))
        {
            return Results.Unauthorized();
        }

        var user = await userStore.FindByEmailAsync(email, cancellationToken);
        if (user is null)
        {
            return Results.NotFound(new { error = "User not found." });
        }

        return Results.Ok(ToUserDto(user));
    }

    private static async Task<IResult> ListUsers(IIdentityUserStore userStore, CancellationToken cancellationToken)
    {
        var users = await userStore.ListAsync(cancellationToken);
        var payload = users.Select(ToUserDto);
        return Results.Ok(payload);
    }

    private static AuthResponse ToAuthResponse(AppUser user, JwtTokenResult token)
    {
        return new AuthResponse(
            token.AccessToken,
            "Bearer",
            token.ExpiresAtUtc,
            ToUserDto(user)
        );
    }

    private static UserDto ToUserDto(AppUser user)
    {
        return new UserDto(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email,
            user.Role,
            user.CreatedAtUtc
        );
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        try
        {
            _ = new MailAddress(email);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
