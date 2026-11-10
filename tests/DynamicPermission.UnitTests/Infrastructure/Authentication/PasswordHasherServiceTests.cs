using DynamicPermission.Domain.Entities;
using DynamicPermission.Infrastructure.Authentication;
using FluentAssertions;
using Xunit;

namespace DynamicPermission.UnitTests.Infrastructure.Authentication;

public class PasswordHasherServiceTests
{
    private readonly PasswordHasherService _hasherService;
    private readonly User _dummyUser;

    public PasswordHasherServiceTests()
    {
        _hasherService = new PasswordHasherService();
        _dummyUser = new User
        {
            Id = Guid.NewGuid(),
            Username = "user_hasher_test"
        };
    }

    [Fact]
    public void HashPassword_ShouldGenerateDifferentHash_ForSamePassword_DueToRandomSalt()
    {
        // Arrange
        const string rawPassword = "SamePassword@123";

        // Act
        var hash1 = _hasherService.HashPassword(_dummyUser, rawPassword);
        var hash2 = _hasherService.HashPassword(_dummyUser, rawPassword);

        // Assert
        hash1.Should().NotBeNullOrWhiteSpace();
        hash2.Should().NotBeNullOrWhiteSpace();
        hash1.Should().NotBe(hash2, "Each hash should use an independent random cryptographic salt");
    }

    [Fact]
    public void VerifyPassword_ShouldReturnTrue_WhenPasswordMatches()
    {
        // Arrange
        const string rawPassword = "CorrectPassword#2026";
        var hash = _hasherService.HashPassword(_dummyUser, rawPassword);

        // Act
        var result = _hasherService.VerifyPassword(_dummyUser, rawPassword, hash);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_ShouldReturnFalse_WhenPasswordDoesNotMatch()
    {
        // Arrange
        const string rawPassword = "CorrectPassword#2026";
        const string wrongPassword = "WrongPassword#2026";
        var hash = _hasherService.HashPassword(_dummyUser, rawPassword);

        // Act
        var result = _hasherService.VerifyPassword(_dummyUser, wrongPassword, hash);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(24)]
    public void GenerateRandomPassword_ShouldMeetComplexityAndLengthRequirements(int length)
    {
        // Act
        var password = _hasherService.GenerateRandomPassword(length);

        // Assert
        password.Length.Should().Be(length);
        password.Should().MatchRegex(@"[A-Z]", "Password must contain at least one uppercase letter");
        password.Should().MatchRegex(@"[a-z]", "Password must contain at least one lowercase letter");
        password.Should().MatchRegex(@"[0-9]", "Password must contain at least one digit");
        password.Should().MatchRegex(@"[!@$?_\-*]", "Password must contain at least one special character");
    }
}
