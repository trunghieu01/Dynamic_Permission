using DynamicPermission.Domain.Entities;

namespace DynamicPermission.Application.Common.Interfaces;

public interface IPasswordHasherService
{
    string HashPassword(User user, string password);
    bool VerifyPassword(User user, string password, string passwordHash);
    string GenerateRandomPassword(int length = 12);
}
