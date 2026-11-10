using System.Security.Cryptography;
using DynamicPermission.Application.Common.Interfaces;
using DynamicPermission.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace DynamicPermission.Infrastructure.Authentication;

public class PasswordHasherService : IPasswordHasherService
{
    private readonly PasswordHasher<User> _passwordHasher = new();

    public string HashPassword(User user, string password)
    {
        return _passwordHasher.HashPassword(user, password);
    }

    public bool VerifyPassword(User user, string password, string passwordHash)
    {
        var result = _passwordHasher.VerifyHashedPassword(user, passwordHash, password);
        return result == PasswordVerificationResult.Success || result == PasswordVerificationResult.SuccessRehashNeeded;
    }

    public string GenerateRandomPassword(int length = 12)
    {
        const string upperChars = "ABCDEFGHJKLMNOPQRSTUVWXYZ";
        const string lowerChars = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "0123456789";
        const string specialChars = "!@$?_-*";

        var allChars = upperChars + lowerChars + digits + specialChars;
        var chars = new char[length];

        // Ensure at least one character from each set
        chars[0] = upperChars[RandomNumberGenerator.GetInt32(upperChars.Length)];
        chars[1] = lowerChars[RandomNumberGenerator.GetInt32(lowerChars.Length)];
        chars[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        chars[3] = specialChars[RandomNumberGenerator.GetInt32(specialChars.Length)];

        for (int i = 4; i < length; i++)
        {
            chars[i] = allChars[RandomNumberGenerator.GetInt32(allChars.Length)];
        }

        // Shuffle chars securely
        for (int i = length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }
}
