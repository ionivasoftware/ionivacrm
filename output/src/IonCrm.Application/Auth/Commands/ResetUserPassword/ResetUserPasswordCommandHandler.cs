using System.Security.Cryptography;
using IonCrm.Application.Common.Interfaces;
using IonCrm.Application.Common.Models;
using IonCrm.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IonCrm.Application.Auth.Commands.ResetUserPassword;

/// <summary>
/// Handles <see cref="ResetUserPasswordCommand"/> — sets a new BCrypt password hash for a user and
/// returns the effective plain-text password so the admin can relay it. NEVER logs the password.
/// </summary>
public class ResetUserPasswordCommandHandler
    : IRequestHandler<ResetUserPasswordCommand, Result<ResetUserPasswordResult>>
{
    // Unambiguous alphabets (no I/O/l/o/0/1) so a handed-over password is easy to read and type.
    private const string Upper  = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower  = "abcdefghijkmnpqrstuvwxyz";
    private const string Digits = "23456789";

    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<ResetUserPasswordCommandHandler> _logger;

    /// <summary>Initialises a new instance of <see cref="ResetUserPasswordCommandHandler"/>.</summary>
    public ResetUserPasswordCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        ILogger<ResetUserPasswordCommandHandler> logger)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _logger         = logger;
    }

    /// <inheritdoc />
    public async Task<Result<ResetUserPasswordResult>> Handle(
        ResetUserPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return Result<ResetUserPasswordResult>.Failure("Kullanıcı bulunamadı.");

        string password;
        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            password = GeneratePassword();
        }
        else
        {
            password = request.NewPassword;
            var invalid = ValidatePassword(password);
            if (invalid is not null)
                return Result<ResetUserPasswordResult>.Failure(invalid);
        }

        user.PasswordHash = _passwordHasher.Hash(password);
        await _userRepository.UpdateAsync(user, cancellationToken);

        // Log the action but NEVER the password.
        _logger.LogInformation("Password reset for user {UserId} ({Email}).", user.Id, user.Email);

        return Result<ResetUserPasswordResult>.Success(new ResetUserPasswordResult(password));
    }

    /// <summary>Mirrors the RegisterUser password rules: 8-128 chars with upper, lower and a digit.</summary>
    private static string? ValidatePassword(string password)
    {
        if (password.Length < 8)   return "Şifre en az 8 karakter olmalıdır.";
        if (password.Length > 128) return "Şifre en fazla 128 karakter olabilir.";
        if (!password.Any(char.IsUpper))  return "Şifre en az bir büyük harf içermelidir.";
        if (!password.Any(char.IsLower))  return "Şifre en az bir küçük harf içermelidir.";
        if (!password.Any(char.IsDigit))  return "Şifre en az bir rakam içermelidir.";
        return null;
    }

    /// <summary>Generates a 14-character cryptographically-random password guaranteed to satisfy the rules.</summary>
    private static string GeneratePassword()
    {
        const string all = Upper + Lower + Digits;
        var chars = new char[14];

        // Guarantee at least one of each required class.
        chars[0] = Upper[RandomNumberGenerator.GetInt32(Upper.Length)];
        chars[1] = Lower[RandomNumberGenerator.GetInt32(Lower.Length)];
        chars[2] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        for (var i = 3; i < chars.Length; i++)
            chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];

        // Fisher-Yates shuffle so the guaranteed characters aren't always in the first positions.
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }
}
