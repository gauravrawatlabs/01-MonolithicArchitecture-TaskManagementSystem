using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace TaskManagementSystem.Helpers;

/// <summary>Generates and hashes opaque random tokens (used for refresh tokens).</summary>
public static class SecureTokenGenerator
{
    private const int TokenSizeInBytes = 64; // 512 bits of randomness — impossible to guess

    /// <summary>
    /// A cryptographically secure random token, Base64Url-encoded (URL/JSON safe).
    /// Uses RandomNumberGenerator — never System.Random, which is predictable.
    /// </summary>
    public static string GenerateToken() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenSizeInBytes));

    /// <summary>
    /// SHA-256 hash as 64 upper-case hex characters. Only this hash is stored in the database:
    /// if the database leaks, the attacker gets hashes that cannot be used as refresh tokens.
    /// (A fast hash is fine here — unlike passwords, the token is 512 random bits, so there is
    /// nothing to brute-force or look up in a dictionary.)
    /// </summary>
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
