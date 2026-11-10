using DynamicPermission.API.Controllers;
using DynamicPermission.Application.Models.Auth;
using DynamicPermission.Application.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace DynamicPermission.UnitTests.API.Controllers;

public class AuthControllerTests
{
    private readonly Mock<IAuthService> _authServiceMock;
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        _authServiceMock = new Mock<IAuthService>();
        _controller = new AuthController(_authServiceMock.Object);
    }

    // ==========================================
    // 1. Login Endpoint Tests
    // ==========================================

    [Fact]
    public async Task Login_ShouldReturn200Ok_WhenCredentialsAreValid()
    {
        // Arrange
        var request = new LoginRequest { UsernameOrEmail = "admin", Password = "Password123" };
        var expectedResponse = new LoginResponse
        {
            AccessToken = "jwt_token_valid",
            RefreshToken = "refresh_token_valid",
            ExpiresIn = 3600
        };

        _authServiceMock.Setup(s => s.LoginAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.Login(request, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(StatusCodes.Status200OK);
        okResult.Value.Should().BeEquivalentTo(expectedResponse);
    }

    [Fact]
    public async Task Login_ShouldReturn400BadRequest_WhenModelStateIsInvalid()
    {
        // Arrange
        _controller.ModelState.AddModelError("UsernameOrEmail", "Required");
        var request = new LoginRequest { UsernameOrEmail = "", Password = "" };

        // Act
        var result = await _controller.Login(request, CancellationToken.None);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task Login_ShouldReturn401Unauthorized_WhenAuthServiceThrowsUnauthorized()
    {
        // Arrange
        var request = new LoginRequest { UsernameOrEmail = "admin", Password = "WrongPassword" };
        _authServiceMock.Setup(s => s.LoginAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Tên đăng nhập hoặc mật khẩu không chính xác."));

        // Act
        var result = await _controller.Login(request, CancellationToken.None);

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    // ==========================================
    // 2. RefreshToken Endpoint Tests
    // ==========================================

    [Fact]
    public async Task RefreshToken_ShouldReturn200Ok_WhenTokenIsValid()
    {
        // Arrange
        var request = new RefreshTokenRequest { AccessToken = "old_token", RefreshToken = "old_refresh" };
        var expectedResponse = new RefreshTokenResponse { AccessToken = "new_token", RefreshToken = "new_refresh" };

        _authServiceMock.Setup(s => s.RefreshTokenAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.RefreshToken(request, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(StatusCodes.Status200OK);
        okResult.Value.Should().BeEquivalentTo(expectedResponse);
    }

    [Fact]
    public async Task RefreshToken_ShouldReturn401Unauthorized_WhenTokenIsExpiredOrRevoked()
    {
        // Arrange
        var request = new RefreshTokenRequest { AccessToken = "old_token", RefreshToken = "revoked_refresh" };
        _authServiceMock.Setup(s => s.RefreshTokenAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Refresh Token không hợp lệ hoặc đã hết hạn."));

        // Act
        var result = await _controller.RefreshToken(request, CancellationToken.None);

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    // ==========================================
    // 3. GetCurrentUser Endpoint Tests
    // ==========================================

    [Fact]
    public async Task GetCurrentUser_ShouldReturn200Ok_WhenUserIsAuthenticated()
    {
        // Arrange
        var expectedUser = new CurrentUserDto
        {
            Id = Guid.NewGuid(),
            Username = "current_user",
            Email = "current@example.com",
            RoleCode = "ADMIN"
        };
        _authServiceMock.Setup(s => s.GetCurrentUserAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedUser);

        // Act
        var result = await _controller.GetCurrentUser(CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(StatusCodes.Status200OK);
        okResult.Value.Should().BeEquivalentTo(expectedUser);
    }

    [Fact]
    public async Task GetCurrentUser_ShouldReturn401Unauthorized_WhenUserIsNotAuthenticated()
    {
        // Arrange
        _authServiceMock.Setup(s => s.GetCurrentUserAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Không xác định được danh tính người dùng hiện tại."));

        // Act
        var result = await _controller.GetCurrentUser(CancellationToken.None);

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    // ==========================================
    // 4. RegisterUser Endpoint Tests
    // ==========================================

    [Fact]
    public async Task RegisterUser_ShouldReturn201Created_WhenInputIsValid()
    {
        // Arrange
        var request = new RegisterUserRequest
        {
            Username = "newbie",
            Email = "newbie@example.com",
            FullName = "Newbie User",
            RoleId = Guid.NewGuid()
        };
        var expectedResponse = new RegisterUserResponse
        {
            UserId = Guid.NewGuid(),
            Username = "newbie",
            Email = "newbie@example.com",
            InitialPassword = "GeneratedPassword123"
        };

        _authServiceMock.Setup(s => s.RegisterUserAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.RegisterUser(request, CancellationToken.None);

        // Assert
        var statusResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(StatusCodes.Status201Created);
        statusResult.Value.Should().BeEquivalentTo(expectedResponse);
    }

    [Fact]
    public async Task RegisterUser_ShouldReturn400BadRequest_WhenUsernameOrEmailAlreadyExists()
    {
        // Arrange
        var request = new RegisterUserRequest
        {
            Username = "existing",
            Email = "existing@example.com",
            FullName = "Existing",
            RoleId = Guid.NewGuid()
        };
        _authServiceMock.Setup(s => s.RegisterUserAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Tên đăng nhập hoặc Email đã tồn tại trong hệ thống."));

        // Act
        var result = await _controller.RegisterUser(request, CancellationToken.None);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    // ==========================================
    // 5. ResetPassword Endpoint Tests
    // ==========================================

    [Fact]
    public async Task ResetPassword_ShouldReturn200Ok_WhenUserExists()
    {
        // Arrange
        var request = new ResetPasswordRequest { UserId = Guid.NewGuid() };
        var expectedResponse = new ResetPasswordResponse
        {
            UserId = request.UserId,
            Username = "reset_user",
            NewPassword = "NewlyResetPassword123"
        };

        _authServiceMock.Setup(s => s.ResetPasswordAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.ResetPassword(request, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(StatusCodes.Status200OK);
        okResult.Value.Should().BeEquivalentTo(expectedResponse);
    }

    [Fact]
    public async Task ResetPassword_ShouldReturn404NotFound_WhenUserDoesNotExist()
    {
        // Arrange
        var request = new ResetPasswordRequest { UserId = Guid.NewGuid() };
        _authServiceMock.Setup(s => s.ResetPasswordAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Không tìm thấy người dùng cần reset mật khẩu."));

        // Act
        var result = await _controller.ResetPassword(request, CancellationToken.None);

        // Assert
        var notFoundResult = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFoundResult.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }
}
