using Microsoft.AspNetCore.Identity;
using TaskManagementSystem.Constants;
using TaskManagementSystem.Data;
using TaskManagementSystem.Exceptions;
using TaskManagementSystem.Interfaces.Services;
using TaskManagementSystem.Mappings;
using TaskManagementSystem.Models.DTOs.Auth;
using TaskManagementSystem.Models.DTOs.Users;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Services;

/// <summary>
/// Registration and login on top of ASP.NET Core Identity:
/// - UserManager   → create users, hash passwords (PBKDF2 + salt), manage roles
/// - SignInManager → verify passwords WITH lockout handling (CheckPasswordSignInAsync; no cookies)
/// - TokenService  → issue the JWT
/// </summary>
public sealed class AuthService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ApplicationDbContext dbContext,
    ITokenService tokenService,
    TimeProvider timeProvider,
    ILogger<AuthService> logger) : IAuthService
{
    // One generic message for every login failure: never reveal whether the email exists,
    // the password was wrong, or the account is locked/inactive (prevents account enumeration).
    private const string InvalidCredentialsMessage = "Invalid email or password.";

    public async Task<UserProfileResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            CreatedAtUtc = UtcNow()
        };

        // Creating the user and assigning the role are two database writes. The transaction makes
        // them all-or-nothing: we never end up with a user who has no role.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var createResult = await userManager.CreateAsync(user, request.Password); // hashes the password
        if (!createResult.Succeeded)
        {
            throw MapIdentityErrors(createResult);
        }

        // The role is ALWAYS "User" — never taken from the request (privilege escalation risk).
        var roleResult = await userManager.AddToRoleAsync(user, Roles.User);
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not assign the '{Roles.User}' role: {DescribeErrors(roleResult)}");
        }

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("User {UserId} registered", user.Id);
        return user.ToProfileResponse([Roles.User]);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive)
        {
            logger.LogWarning("Login failed: unknown or inactive account");
            throw new UnauthorizedException(InvalidCredentialsMessage);
        }

        // lockoutOnFailure: true → after 5 wrong passwords the account is locked for 15 minutes.
        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            logger.LogWarning("Login failed: user {UserId} is locked out", user.Id);
            throw new UnauthorizedException(InvalidCredentialsMessage);
        }

        if (!result.Succeeded)
        {
            logger.LogWarning("Login failed: wrong password for user {UserId}", user.Id);
            throw new UnauthorizedException(InvalidCredentialsMessage);
        }

        user.LastLoginAtUtc = UtcNow();
        await userManager.UpdateAsync(user);

        var roles = await userManager.GetRolesAsync(user);
        var accessToken = tokenService.CreateAccessToken(user, roles);

        logger.LogInformation("User {UserId} logged in", user.Id);
        return new AuthResponse(accessToken.Token, "Bearer", accessToken.ExpiresAtUtc, user.ToProfileResponse(roles));
    }

    public async Task<UserProfileResponse> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        // The token may still be valid (up to 15 min) after the account was deactivated → re-check.
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive)
        {
            throw new UnauthorizedException("The user account no longer exists or is inactive.");
        }

        var roles = await userManager.GetRolesAsync(user);
        return user.ToProfileResponse(roles);
    }

    /// <summary>Turns Identity's error list into our API errors (409 for duplicates, 400 otherwise).</summary>
    private static AppException MapIdentityErrors(IdentityResult result)
    {
        if (result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName"))
        {
            return new ConflictException("An account with this email address already exists.");
        }

        var errors = result.Errors
            .GroupBy(e => e.Code switch
            {
                var code when code.StartsWith("Password", StringComparison.Ordinal) => "password",
                "InvalidEmail" or "InvalidUserName" => "email",
                _ => "general"
            })
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

        return new RequestValidationException(errors);
    }

    private static string DescribeErrors(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(e => e.Description));

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
