using Microsoft.AspNetCore.Identity;
using TaskManagementSystem.Constants;
using TaskManagementSystem.Data;
using TaskManagementSystem.Exceptions;
using TaskManagementSystem.Helpers;
using TaskManagementSystem.Interfaces.Repositories;
using TaskManagementSystem.Interfaces.Services;
using TaskManagementSystem.Mappings;
using TaskManagementSystem.Models.DTOs.Auth;
using TaskManagementSystem.Models.DTOs.Users;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Services;

/// <summary>
/// Session management on top of ASP.NET Core Identity:
/// - UserManager   → create users, hash passwords (PBKDF2 + salt), manage roles
/// - SignInManager → verify passwords WITH lockout handling (CheckPasswordSignInAsync; no cookies)
/// - TokenService  → issue JWT access tokens and refresh tokens
/// - RefreshTokenRepository → store / rotate / revoke refresh tokens (hashed)
/// </summary>
public sealed class AuthService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ApplicationDbContext dbContext,
    IRefreshTokenRepository refreshTokenRepository,
    ITokenService tokenService,
    IAccountEmailService accountEmailService,
    TimeProvider timeProvider,
    ILogger<AuthService> logger) : IAuthService
{
    // One generic message for every login failure: never reveal whether the email exists,
    // the password was wrong, or the account is locked/inactive (prevents account enumeration).
    private const string InvalidCredentialsMessage = "Invalid email or password.";
    private const string InvalidRefreshTokenMessage = "The refresh token is invalid or has expired. Please log in again.";

    private const string ReasonRotated = "Replaced by a new token (rotation)";
    private const string ReasonLogout = "Logged out";
    private const string ReasonLogoutAll = "Logged out from all devices";
    private const string ReasonReuseDetected = "Reuse of a revoked token detected";
    private const string ReasonInactiveUser = "User account inactive";

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
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
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
        }

        logger.LogInformation("User {UserId} registered", user.Id);

        // The account exists now. If the email can't be sent, registration must still succeed —
        // the user can request a new confirmation email (POST /api/account/resend-confirmation).
        try
        {
            await accountEmailService.SendEmailConfirmationAsync(user, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not send the confirmation email to user {UserId}", user.Id);
        }

        return user.ToProfileResponse([Roles.User]);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken)
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

        var issuedRefreshToken = tokenService.CreateRefreshToken(user.Id, ipAddress);
        refreshTokenRepository.Add(issuedRefreshToken.Entity);
        await refreshTokenRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("User {UserId} logged in", user.Id);
        return await BuildAuthResponseAsync(user, issuedRefreshToken);
    }

    /// <summary>
    /// Refresh-token ROTATION with REUSE DETECTION:
    /// every refresh token can be used exactly once. Using it returns a new pair and revokes the old
    /// token (pointing to its successor). If a token that was already rotated is presented again,
    /// someone has a copy — a thief or the real user — so ALL the user's sessions are revoked.
    /// </summary>
    public async Task<AuthResponse> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var existing = await refreshTokenRepository.GetByHashAsync(tokenService.HashRefreshToken(refreshToken), cancellationToken);

        if (existing is null)
        {
            logger.LogWarning("Refresh failed: unknown refresh token");
            throw new UnauthorizedException(InvalidRefreshTokenMessage);
        }

        if (existing.IsRevoked)
        {
            if (existing.ReplacedByTokenId is not null)
            {
                await RevokeAllBecauseOfReuseAsync(existing.UserId, ipAddress, now, cancellationToken);
            }
            else
            {
                logger.LogWarning("Refresh failed: token of user {UserId} was revoked ({Reason})",
                    existing.UserId, existing.RevokedReason);
            }
            throw new UnauthorizedException(InvalidRefreshTokenMessage);
        }

        if (!existing.IsActiveAt(now))
        {
            logger.LogInformation("Refresh failed: token of user {UserId} expired", existing.UserId);
            throw new UnauthorizedException(InvalidRefreshTokenMessage);
        }

        var user = existing.User;
        if (!user.IsActive)
        {
            await refreshTokenRepository.RevokeAllActiveForUserAsync(user.Id, now, ipAddress, ReasonInactiveUser, cancellationToken);
            throw new UnauthorizedException(InvalidRefreshTokenMessage);
        }

        var issuedRefreshToken = tokenService.CreateRefreshToken(user.Id, ipAddress);

        // Insert the new token and revoke the old one atomically. The revoke is a compare-and-set
        // (only if not yet revoked): if a parallel request already rotated this token, we roll back
        // our new token and treat it as reuse.
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            refreshTokenRepository.Add(issuedRefreshToken.Entity);
            await refreshTokenRepository.SaveChangesAsync(cancellationToken);

            var revoked = await refreshTokenRepository.TryRevokeAsync(
                existing.Id, now, ipAddress, ReasonRotated, issuedRefreshToken.Entity.Id, cancellationToken);

            if (!revoked)
            {
                await transaction.RollbackAsync(cancellationToken);
                await RevokeAllBecauseOfReuseAsync(user.Id, ipAddress, now, cancellationToken);
                throw new UnauthorizedException(InvalidRefreshTokenMessage);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        logger.LogInformation("Refresh token rotated for user {UserId}", user.Id);
        return await BuildAuthResponseAsync(user, issuedRefreshToken);
    }

    public async Task LogoutAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken)
    {
        var existing = await refreshTokenRepository.GetByHashAsync(tokenService.HashRefreshToken(refreshToken), cancellationToken);
        if (existing is null || existing.IsRevoked)
        {
            return; // idempotent: logging out twice (or with an unknown token) is not an error
        }

        await refreshTokenRepository.TryRevokeAsync(existing.Id, UtcNow(), ipAddress, ReasonLogout, null, cancellationToken);
        logger.LogInformation("User {UserId} logged out", existing.UserId);
    }

    public async Task LogoutAllAsync(Guid userId, string? ipAddress, CancellationToken cancellationToken)
    {
        var count = await refreshTokenRepository.RevokeAllActiveForUserAsync(
            userId, UtcNow(), ipAddress, ReasonLogoutAll, cancellationToken);
        logger.LogInformation("User {UserId} logged out from all devices ({Count} sessions revoked)", userId, count);
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

    private async Task RevokeAllBecauseOfReuseAsync(Guid userId, string? ipAddress, DateTime now, CancellationToken cancellationToken)
    {
        var count = await refreshTokenRepository.RevokeAllActiveForUserAsync(
            userId, now, ipAddress, ReasonReuseDetected, cancellationToken);

        // Security event: worth alerting on in production.
        logger.LogWarning(
            "SECURITY: reuse of a rotated refresh token for user {UserId} from {IpAddress}; {Count} active sessions revoked",
            userId, ipAddress, count);
    }

    /// <summary>
    /// Roles and claims are read from the DATABASE on every login/refresh, so changes
    /// (new role, email confirmed) appear in the next access token without logging in again.
    /// </summary>
    private async Task<AuthResponse> BuildAuthResponseAsync(ApplicationUser user, IssuedRefreshToken refreshToken)
    {
        var roles = await userManager.GetRolesAsync(user);
        var accessToken = tokenService.CreateAccessToken(user, roles);

        return new AuthResponse(
            accessToken.Token,
            "Bearer",
            accessToken.ExpiresAtUtc,
            refreshToken.RawToken,
            refreshToken.Entity.ExpiresAtUtc,
            user.ToProfileResponse(roles));
    }

    /// <summary>Turns Identity's error list into our API errors (409 for duplicates, 400 otherwise).</summary>
    private static AppException MapIdentityErrors(IdentityResult result)
    {
        if (result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName"))
        {
            return new ConflictException("An account with this email address already exists.");
        }

        return IdentityErrorMapper.ToValidationException(result, passwordField: "password");
    }

    private static string DescribeErrors(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(e => e.Description));

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
