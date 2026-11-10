using System.Security.Claims;
using DynamicPermission.Application.Common.Interfaces;
using DynamicPermission.Application.Models.Auth;
using DynamicPermission.Application.Services;
using DynamicPermission.Domain.Entities;
using DynamicPermission.Infrastructure.Persistence;
using DynamicPermission.UnitTests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace DynamicPermission.UnitTests.Application.Services;

public class AuthServiceTests
{
    private readonly DynamicPermissionDbContext _context;
    private readonly Mock<ITokenService> _tokenServiceMock;
    private readonly Mock<IPasswordHasherService> _passwordHasherMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        _context = TestDbContextFactory.Create();
        _tokenServiceMock = new Mock<ITokenService>();
        _passwordHasherMock = new Mock<IPasswordHasherService>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();

        _authService = new AuthService(
            _context,
            _tokenServiceMock.Object,
            _passwordHasherMock.Object,
            _currentUserServiceMock.Object);
    }

    // ==========================================
    // 1. LoginAsync Tests
    // ==========================================

    [Fact]
    public async Task LoginAsync_ShouldSucceed_WhenUsernameAndPasswordAreCorrect()
    {
        // Arrange
        var role = TestDataBuilder.CreateRole("ADMIN", "Quản trị viên", isActive: true);
        var user = TestDataBuilder.CreateUser(role.Id, "admin", "admin@example.com", "hashed_pwd", isActive: true);
        user.Role = role;

        _context.Roles.Add(role);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _passwordHasherMock.Setup(p => p.VerifyPassword(It.IsAny<User>(), "Password123", "hashed_pwd")).Returns(true);
        _tokenServiceMock.Setup(t => t.GenerateAccessToken(It.IsAny<User>(), "ADMIN")).Returns(("access_token_123", 3600));
        _tokenServiceMock.Setup(t => t.GenerateRefreshToken()).Returns(("refresh_token_123", DateTimeOffset.UtcNow.AddDays(7)));

        var request = new LoginRequest { UsernameOrEmail = "admin", Password = "Password123" };

        // Act
        var result = await _authService.LoginAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.AccessToken.Should().Be("access_token_123");
        result.RefreshToken.Should().Be("refresh_token_123");
        result.ExpiresIn.Should().Be(3600);
        result.User.Username.Should().Be("admin");
        result.User.RoleCode.Should().Be("ADMIN");

        // Verify RefreshToken was stored in DB
        var savedRefreshToken = await _context.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == "refresh_token_123");
        savedRefreshToken.Should().NotBeNull();
        savedRefreshToken!.UserId.Should().Be(user.Id);
        savedRefreshToken.IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task LoginAsync_ShouldSucceed_WhenEmailIsUsed()
    {
        // Arrange
        var role = TestDataBuilder.CreateRole("STAFF", "Nhân viên", isActive: true);
        var user = TestDataBuilder.CreateUser(role.Id, "staff01", "staff01@example.com", "hashed_pwd", isActive: true);
        user.Role = role;

        _context.Roles.Add(role);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _passwordHasherMock.Setup(p => p.VerifyPassword(It.IsAny<User>(), "Password123", "hashed_pwd")).Returns(true);
        _tokenServiceMock.Setup(t => t.GenerateAccessToken(It.IsAny<User>(), "STAFF")).Returns(("access_token_abc", 3600));
        _tokenServiceMock.Setup(t => t.GenerateRefreshToken()).Returns(("refresh_token_abc", DateTimeOffset.UtcNow.AddDays(7)));

        var request = new LoginRequest { UsernameOrEmail = "staff01@example.com", Password = "Password123" };

        // Act
        var result = await _authService.LoginAsync(request);

        // Assert
        result.AccessToken.Should().Be("access_token_abc");
        result.User.Email.Should().Be("staff01@example.com");
    }

    [Fact]
    public async Task LoginAsync_ShouldThrowUnauthorized_WhenUserNotFound()
    {
        // Arrange
        var request = new LoginRequest { UsernameOrEmail = "nonexistent_user", Password = "Password123" };

        // Act
        var act = () => _authService.LoginAsync(request);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Tên đăng nhập hoặc mật khẩu không chính xác.");
    }

    [Fact]
    public async Task LoginAsync_ShouldThrowUnauthorized_WhenPasswordIsIncorrect()
    {
        // Arrange
        var role = TestDataBuilder.CreateRole();
        var user = TestDataBuilder.CreateUser(role.Id, "user1", "user1@example.com", "hashed_pwd");
        user.Role = role;

        _context.Roles.Add(role);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _passwordHasherMock.Setup(p => p.VerifyPassword(It.IsAny<User>(), "WrongPassword", "hashed_pwd")).Returns(false);

        var request = new LoginRequest { UsernameOrEmail = "user1", Password = "WrongPassword" };

        // Act
        var act = () => _authService.LoginAsync(request);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Tên đăng nhập hoặc mật khẩu không chính xác.");
    }

    [Fact]
    public async Task LoginAsync_ShouldThrowUnauthorized_WhenUserIsInactive()
    {
        // Arrange
        var role = TestDataBuilder.CreateRole(isActive: true);
        var user = TestDataBuilder.CreateUser(role.Id, "locked_user", "locked@example.com", "hashed_pwd", isActive: false);
        user.Role = role;

        _context.Roles.Add(role);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _passwordHasherMock.Setup(p => p.VerifyPassword(It.IsAny<User>(), "Password123", "hashed_pwd")).Returns(true);

        var request = new LoginRequest { UsernameOrEmail = "locked_user", Password = "Password123" };

        // Act
        var act = () => _authService.LoginAsync(request);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Tài khoản đã bị vô hiệu hóa hoặc khóa tạm thời.");
    }

    [Fact]
    public async Task LoginAsync_ShouldThrowUnauthorized_WhenRoleIsInactive()
    {
        // Arrange
        var role = TestDataBuilder.CreateRole("LOCKED_ROLE", "Vai trò bị khóa", isActive: false);
        var user = TestDataBuilder.CreateUser(role.Id, "active_user", "active@example.com", "hashed_pwd", isActive: true);
        user.Role = role;

        _context.Roles.Add(role);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _passwordHasherMock.Setup(p => p.VerifyPassword(It.IsAny<User>(), "Password123", "hashed_pwd")).Returns(true);

        var request = new LoginRequest { UsernameOrEmail = "active_user", Password = "Password123" };

        // Act
        var act = () => _authService.LoginAsync(request);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Vai trò của tài khoản hiện đang bị tạm ngừng.");
    }

    // ==========================================
    // 2. RefreshTokenAsync Tests
    // ==========================================

    [Fact]
    public async Task RefreshTokenAsync_ShouldSucceed_AndRotateToken_WhenInputsAreValid()
    {
        // Arrange
        var role = TestDataBuilder.CreateRole("ADMIN", "Quản trị viên", isActive: true);
        var user = TestDataBuilder.CreateUser(role.Id, "token_user", "token_user@example.com", isActive: true);
        user.Role = role;

        var existingRefreshToken = TestDataBuilder.CreateRefreshToken(user.Id, "old_refresh_token", isRevoked: false, daysValid: 3);

        _context.Roles.Add(role);
        _context.Users.Add(user);
        _context.RefreshTokens.Add(existingRefreshToken);
        await _context.SaveChangesAsync();

        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()) };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        _tokenServiceMock.Setup(t => t.GetPrincipalFromExpiredToken("old_access_token")).Returns(principal);
        _tokenServiceMock.Setup(t => t.GenerateAccessToken(It.IsAny<User>(), "ADMIN")).Returns(("new_access_token", 3600));
        _tokenServiceMock.Setup(t => t.GenerateRefreshToken()).Returns(("new_refresh_token", DateTimeOffset.UtcNow.AddDays(7)));

        var request = new RefreshTokenRequest
        {
            AccessToken = "old_access_token",
            RefreshToken = "old_refresh_token"
        };

        // Act
        var result = await _authService.RefreshTokenAsync(request);

        // Assert
        result.AccessToken.Should().Be("new_access_token");
        result.RefreshToken.Should().Be("new_refresh_token");

        // Old token must be revoked
        var oldTokenInDb = await _context.RefreshTokens.FirstAsync(rt => rt.Token == "old_refresh_token");
        oldTokenInDb.IsRevoked.Should().BeTrue();
        oldTokenInDb.RevokedAt.Should().NotBeNull();

        // New token must exist and not be revoked
        var newTokenInDb = await _context.RefreshTokens.FirstAsync(rt => rt.Token == "new_refresh_token");
        newTokenInDb.IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshTokenAsync_ShouldThrowUnauthorized_WhenAccessTokenIsInvalid()
    {
        // Arrange
        _tokenServiceMock.Setup(t => t.GetPrincipalFromExpiredToken("invalid_jwt")).Returns((ClaimsPrincipal?)null);

        var request = new RefreshTokenRequest
        {
            AccessToken = "invalid_jwt",
            RefreshToken = "some_refresh_token"
        };

        // Act
        var act = () => _authService.RefreshTokenAsync(request);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Access Token không hợp lệ.");
    }

    [Fact]
    public async Task RefreshTokenAsync_ShouldThrowUnauthorized_WhenRefreshTokenIsExpired()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        _tokenServiceMock.Setup(t => t.GetPrincipalFromExpiredToken("access_token")).Returns(principal);

        var expiredToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = "expired_token",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-10), // Expired
            IsRevoked = false
        };
        _context.RefreshTokens.Add(expiredToken);
        await _context.SaveChangesAsync();

        var request = new RefreshTokenRequest { AccessToken = "access_token", RefreshToken = "expired_token" };

        // Act
        var act = () => _authService.RefreshTokenAsync(request);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Refresh Token không hợp lệ hoặc đã hết hạn.");
    }

    [Fact]
    public async Task RefreshTokenAsync_ShouldThrowUnauthorized_WhenRefreshTokenIsRevoked()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims));

        _tokenServiceMock.Setup(t => t.GetPrincipalFromExpiredToken("access_token")).Returns(principal);

        var revokedToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = "revoked_token",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(5),
            IsRevoked = true // Already revoked
        };
        _context.RefreshTokens.Add(revokedToken);
        await _context.SaveChangesAsync();

        var request = new RefreshTokenRequest { AccessToken = "access_token", RefreshToken = "revoked_token" };

        // Act
        var act = () => _authService.RefreshTokenAsync(request);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Refresh Token không hợp lệ hoặc đã hết hạn.");
    }

    // ==========================================
    // 3. GetCurrentUserAsync Tests
    // ==========================================

    [Fact]
    public async Task GetCurrentUserAsync_ShouldSucceed_WhenUserIsAuthenticated()
    {
        // Arrange
        var role = TestDataBuilder.CreateRole("MANAGER", "Trưởng phòng");
        var user = TestDataBuilder.CreateUser(role.Id, "manager_user", "manager@example.com");
        user.Role = role;

        _context.Roles.Add(role);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _currentUserServiceMock.Setup(c => c.UserId).Returns(user.Id);

        // Act
        var result = await _authService.GetCurrentUserAsync();

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(user.Id);
        result.Username.Should().Be("manager_user");
        result.RoleCode.Should().Be("MANAGER");
    }

    [Fact]
    public async Task GetCurrentUserAsync_ShouldThrowUnauthorized_WhenUserIdIsNull()
    {
        // Arrange
        _currentUserServiceMock.Setup(c => c.UserId).Returns((Guid?)null);

        // Act
        var act = () => _authService.GetCurrentUserAsync();

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Không xác định được danh tính người dùng hiện tại.");
    }

    // ==========================================
    // 4. RegisterUserAsync Tests
    // ==========================================

    [Fact]
    public async Task RegisterUserAsync_ShouldSucceed_WithAutoGeneratedPassword_WhenPasswordIsNull()
    {
        // Arrange
        var role = TestDataBuilder.CreateRole("STAFF", "Nhân viên", isActive: true);
        _context.Roles.Add(role);
        await _context.SaveChangesAsync();

        _currentUserServiceMock.Setup(c => c.Username).Returns("admin_caller");
        _passwordHasherMock.Setup(p => p.GenerateRandomPassword(12)).Returns("Random@Pass123");
        _passwordHasherMock.Setup(p => p.HashPassword(It.IsAny<User>(), "Random@Pass123")).Returns("hashed_auto_pwd");

        var request = new RegisterUserRequest
        {
            Username = "new_staff",
            Email = "new_staff@example.com",
            FullName = "Nguyen Staff",
            RoleId = role.Id,
            Password = null // Auto-generate
        };

        // Act
        var result = await _authService.RegisterUserAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Username.Should().Be("new_staff");
        result.InitialPassword.Should().Be("Random@Pass123");
        result.RoleCode.Should().Be("STAFF");

        var savedUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == "new_staff");
        savedUser.Should().NotBeNull();
        savedUser!.PasswordHash.Should().Be("hashed_auto_pwd");
        savedUser.CreatedBy.Should().Be("admin_caller");
    }

    [Fact]
    public async Task RegisterUserAsync_ShouldThrowInvalidOperation_WhenUsernameOrEmailAlreadyExists()
    {
        // Arrange
        var role = TestDataBuilder.CreateRole();
        var existingUser = TestDataBuilder.CreateUser(role.Id, "existing_user", "existing@example.com");

        _context.Roles.Add(role);
        _context.Users.Add(existingUser);
        await _context.SaveChangesAsync();

        var request = new RegisterUserRequest
        {
            Username = "existing_user",
            Email = "another@example.com",
            FullName = "Duplicated User",
            RoleId = role.Id
        };

        // Act
        var act = () => _authService.RegisterUserAsync(request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Tên đăng nhập hoặc Email đã tồn tại trong hệ thống.");
    }

    [Fact]
    public async Task RegisterUserAsync_ShouldThrowKeyNotFound_WhenRoleDoesNotExist()
    {
        // Arrange
        var request = new RegisterUserRequest
        {
            Username = "user_no_role",
            Email = "user_no_role@example.com",
            FullName = "No Role User",
            RoleId = Guid.NewGuid() // Non-existent role
        };

        // Act
        var act = () => _authService.RegisterUserAsync(request);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("Vai trò (Role) được chỉ định không tồn tại.");
    }

    // ==========================================
    // 5. ResetPasswordAsync Tests
    // ==========================================

    [Fact]
    public async Task ResetPasswordAsync_ShouldSucceed_AndRevokeAllOldRefreshTokens()
    {
        // Arrange
        var role = TestDataBuilder.CreateRole();
        var user = TestDataBuilder.CreateUser(role.Id, "reset_user", "reset@example.com", "old_hash");
        var token1 = TestDataBuilder.CreateRefreshToken(user.Id, "tok1", isRevoked: false);
        var token2 = TestDataBuilder.CreateRefreshToken(user.Id, "tok2", isRevoked: false);

        _context.Roles.Add(role);
        _context.Users.Add(user);
        _context.RefreshTokens.AddRange(token1, token2);
        await _context.SaveChangesAsync();

        _currentUserServiceMock.Setup(c => c.Username).Returns("admin");
        _passwordHasherMock.Setup(p => p.GenerateRandomPassword(12)).Returns("NewGenerated@123");
        _passwordHasherMock.Setup(p => p.HashPassword(It.IsAny<User>(), "NewGenerated@123")).Returns("new_hashed_pwd");

        var request = new ResetPasswordRequest { UserId = user.Id, NewPassword = null };

        // Act
        var result = await _authService.ResetPasswordAsync(request);

        // Assert
        result.NewPassword.Should().Be("NewGenerated@123");

        var updatedUser = await _context.Users.FindAsync(user.Id);
        updatedUser!.PasswordHash.Should().Be("new_hashed_pwd");

        // Verify all refresh tokens are revoked
        var tokens = await _context.RefreshTokens.Where(rt => rt.UserId == user.Id).ToListAsync();
        tokens.Should().AllSatisfy(t => t.IsRevoked.Should().BeTrue());
    }

    [Fact]
    public async Task ResetPasswordAsync_ShouldThrowKeyNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        var request = new ResetPasswordRequest { UserId = Guid.NewGuid() };

        // Act
        var act = () => _authService.ResetPasswordAsync(request);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("Không tìm thấy người dùng cần reset mật khẩu.");
    }
}
