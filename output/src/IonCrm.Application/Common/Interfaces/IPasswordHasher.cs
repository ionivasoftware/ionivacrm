namespace IonCrm.Application.Common.Interfaces;

/// <summary>
/// Abstracts password hashing so the Application layer stays free of BCrypt infrastructure concerns.
/// Implementation uses BCrypt with cost factor 12 (see IonCrm.Infrastructure.Services.PasswordHasher).
/// NEVER log the plain-text password or the resulting hash.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Creates a BCrypt hash of the plain-text password using cost factor 12.
    /// Safe to store directly in the database.
    /// </summary>
    string Hash(string password);

    /// <summary>
    /// Verifies a plain-text password against a previously stored BCrypt hash.
    /// Returns true if the password matches; false otherwise.
    /// </summary>
    bool Verify(string password, string hash);
}
