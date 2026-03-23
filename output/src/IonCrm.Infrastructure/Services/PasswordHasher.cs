using IonCrm.Application.Common.Interfaces;

namespace IonCrm.Infrastructure.Services;

/// <summary>
/// BCrypt-based password hasher using cost factor 12.
/// NEVER log the input or the resulting hash.
/// </summary>
public class PasswordHasher : IPasswordHasher
{
    // BCrypt cost factor: 2^12 = 4096 iterations — strong against brute-force.
    private const int WorkFactor = 12;

    /// <inheritdoc />
    public string Hash(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password must not be empty.", nameof(password));

        return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
    }

    /// <inheritdoc />
    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(hash))
            return false;

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch
        {
            // Malformed hash — treat as mismatch
            return false;
        }
    }
}
