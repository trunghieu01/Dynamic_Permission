using DynamicPermission.Application.Models.Auth;
using DynamicPermission.UnitTests.Common;
using FluentAssertions;
using Xunit;

namespace DynamicPermission.UnitTests.Validation;

public class ModelValidationTests
{
    // ==========================================
    // 1. LoginRequest Validation Tests
    // ==========================================

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void LoginRequest_ShouldBeInvalid_WhenUsernameOrEmailIsEmpty(string? usernameOrEmail)
    {
        var request = new LoginRequest
        {
            UsernameOrEmail = usernameOrEmail!,
            Password = "ValidPassword123"
        };

        var results = ModelValidationHelper.Validate(request);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(LoginRequest.UsernameOrEmail)));
        results.Should().Contain(r => r.ErrorMessage == "Tên đăng nhập hoặc Email không được để trống.");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void LoginRequest_ShouldBeInvalid_WhenPasswordIsEmpty(string? password)
    {
        var request = new LoginRequest
        {
            UsernameOrEmail = "admin",
            Password = password!
        };

        var results = ModelValidationHelper.Validate(request);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(LoginRequest.Password)));
        results.Should().Contain(r => r.ErrorMessage == "Mật khẩu không được để trống.");
    }

    [Fact]
    public void LoginRequest_ShouldBeValid_WhenAllFieldsAreValid()
    {
        var request = new LoginRequest
        {
            UsernameOrEmail = "admin",
            Password = "Password123"
        };

        var results = ModelValidationHelper.Validate(request);

        results.Should().BeEmpty();
    }

    // ==========================================
    // 2. RegisterUserRequest Validation Tests
    // ==========================================

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void RegisterUserRequest_ShouldBeInvalid_WhenUsernameIsEmpty(string? username)
    {
        var request = new RegisterUserRequest
        {
            Username = username!,
            Email = "user@example.com",
            FullName = "Nguyen Van A",
            RoleId = Guid.NewGuid()
        };

        var results = ModelValidationHelper.Validate(request);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(RegisterUserRequest.Username)));
    }

    [Fact]
    public void RegisterUserRequest_ShouldBeInvalid_WhenUsernameIsTooShort()
    {
        var request = new RegisterUserRequest
        {
            Username = "ab", // < 3 characters
            Email = "user@example.com",
            FullName = "Nguyen Van A",
            RoleId = Guid.NewGuid()
        };

        var results = ModelValidationHelper.Validate(request);

        results.Should().Contain(r => r.ErrorMessage == "Tên đăng nhập phải từ 3 đến 50 ký tự.");
    }

    [Fact]
    public void RegisterUserRequest_ShouldBeInvalid_WhenUsernameIsTooLong()
    {
        var request = new RegisterUserRequest
        {
            Username = new string('a', 51), // > 50 characters
            Email = "user@example.com",
            FullName = "Nguyen Van A",
            RoleId = Guid.NewGuid()
        };

        var results = ModelValidationHelper.Validate(request);

        results.Should().Contain(r => r.ErrorMessage == "Tên đăng nhập phải từ 3 đến 50 ký tự.");
    }

    [Theory]
    [InlineData("user name")]
    [InlineData("user@name")]
    [InlineData("user#123")]
    [InlineData("user!")]
    public void RegisterUserRequest_ShouldBeInvalid_WhenUsernameContainsSpecialCharactersOrSpaces(string invalidUsername)
    {
        var request = new RegisterUserRequest
        {
            Username = invalidUsername,
            Email = "user@example.com",
            FullName = "Nguyen Van A",
            RoleId = Guid.NewGuid()
        };

        var results = ModelValidationHelper.Validate(request);

        results.Should().Contain(r => r.ErrorMessage == "Tên đăng nhập chỉ chứa chữ cái, số, dấu gạch dưới và dấu chấm.");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void RegisterUserRequest_ShouldBeInvalid_WhenEmailIsEmpty(string? email)
    {
        var request = new RegisterUserRequest
        {
            Username = "validuser",
            Email = email!,
            FullName = "Nguyen Van A",
            RoleId = Guid.NewGuid()
        };

        var results = ModelValidationHelper.Validate(request);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(RegisterUserRequest.Email)));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("plainaddress")]
    [InlineData("@missingusername.com")]
    [InlineData("missingdomain@")]
    public void RegisterUserRequest_ShouldBeInvalid_WhenEmailFormatIsInvalid(string invalidEmail)
    {
        var request = new RegisterUserRequest
        {
            Username = "validuser",
            Email = invalidEmail,
            FullName = "Nguyen Van A",
            RoleId = Guid.NewGuid()
        };

        var results = ModelValidationHelper.Validate(request);

        results.Should().Contain(r => r.ErrorMessage == "Email không đúng định dạng.");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void RegisterUserRequest_ShouldBeInvalid_WhenFullNameIsEmpty(string? fullName)
    {
        var request = new RegisterUserRequest
        {
            Username = "validuser",
            Email = "user@example.com",
            FullName = fullName!,
            RoleId = Guid.NewGuid()
        };

        var results = ModelValidationHelper.Validate(request);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(RegisterUserRequest.FullName)));
        results.Should().Contain(r => r.ErrorMessage == "Họ và tên không được để trống.");
    }

    [Fact]
    public void RegisterUserRequest_ShouldBeInvalid_WhenPasswordIsProvidedButTooShort()
    {
        var request = new RegisterUserRequest
        {
            Username = "validuser",
            Email = "user@example.com",
            FullName = "Nguyen Van A",
            RoleId = Guid.NewGuid(),
            Password = "12345" // < 6 characters
        };

        var results = ModelValidationHelper.Validate(request);

        results.Should().Contain(r => r.ErrorMessage == "Mật khẩu nếu nhập phải từ 6 ký tự trở lên.");
    }

    [Fact]
    public void RegisterUserRequest_ShouldBeValid_WhenPasswordIsNull()
    {
        var request = new RegisterUserRequest
        {
            Username = "validuser",
            Email = "user@example.com",
            FullName = "Nguyen Van A",
            RoleId = Guid.NewGuid(),
            Password = null // Allowed for auto-generated password
        };

        var results = ModelValidationHelper.Validate(request);

        results.Should().BeEmpty();
    }

    [Fact]
    public void RegisterUserRequest_ShouldBeValid_WhenAllFieldsAreValid()
    {
        var request = new RegisterUserRequest
        {
            Username = "valid_user.01",
            Email = "valid.user@example.com",
            FullName = "Nguyen Van A",
            RoleId = Guid.NewGuid(),
            Password = "ValidPassword123"
        };

        var results = ModelValidationHelper.Validate(request);

        results.Should().BeEmpty();
    }

    // ==========================================
    // 3. RefreshTokenRequest Validation Tests
    // ==========================================

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void RefreshTokenRequest_ShouldBeInvalid_WhenAccessTokenIsEmpty(string? accessToken)
    {
        var request = new RefreshTokenRequest
        {
            AccessToken = accessToken!,
            RefreshToken = "valid_refresh_token"
        };

        var results = ModelValidationHelper.Validate(request);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(RefreshTokenRequest.AccessToken)));
        results.Should().Contain(r => r.ErrorMessage == "AccessToken không được để trống.");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void RefreshTokenRequest_ShouldBeInvalid_WhenRefreshTokenIsEmpty(string? refreshToken)
    {
        var request = new RefreshTokenRequest
        {
            AccessToken = "valid_access_token",
            RefreshToken = refreshToken!
        };

        var results = ModelValidationHelper.Validate(request);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(RefreshTokenRequest.RefreshToken)));
        results.Should().Contain(r => r.ErrorMessage == "RefreshToken không được để trống.");
    }

    [Fact]
    public void RefreshTokenRequest_ShouldBeValid_WhenBothTokensAreProvided()
    {
        var request = new RefreshTokenRequest
        {
            AccessToken = "valid_access_token_jwt",
            RefreshToken = "valid_refresh_token_base64"
        };

        var results = ModelValidationHelper.Validate(request);

        results.Should().BeEmpty();
    }

    // ==========================================
    // 4. ResetPasswordRequest Validation Tests
    // ==========================================

    [Fact]
    public void ResetPasswordRequest_ShouldBeInvalid_WhenNewPasswordIsTooShort()
    {
        var request = new ResetPasswordRequest
        {
            UserId = Guid.NewGuid(),
            NewPassword = "123" // < 6 characters
        };

        var results = ModelValidationHelper.Validate(request);

        results.Should().Contain(r => r.ErrorMessage == "Mật khẩu mới nếu nhập phải từ 6 ký tự trở lên.");
    }

    [Fact]
    public void ResetPasswordRequest_ShouldBeValid_WhenNewPasswordIsNull()
    {
        var request = new ResetPasswordRequest
        {
            UserId = Guid.NewGuid(),
            NewPassword = null // Allowed for auto-generation
        };

        var results = ModelValidationHelper.Validate(request);

        results.Should().BeEmpty();
    }

    [Fact]
    public void ResetPasswordRequest_ShouldBeValid_WhenAllFieldsAreValid()
    {
        var request = new ResetPasswordRequest
        {
            UserId = Guid.NewGuid(),
            NewPassword = "SecureNewPassword@123"
        };

        var results = ModelValidationHelper.Validate(request);

        results.Should().BeEmpty();
    }
}
