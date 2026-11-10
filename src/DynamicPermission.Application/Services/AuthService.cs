using System.Security.Claims;
using DynamicPermission.Application.Common.Interfaces;
using DynamicPermission.Application.Models.Auth;
using DynamicPermission.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DynamicPermission.Application.Services;

public class AuthService : IAuthService
{
    private readonly IApplicationDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly IPasswordHasherService _passwordHasher;
    private readonly ICurrentUserService _currentUserService;

    public AuthService(
        IApplicationDbContext context,
        ITokenService tokenService,
        IPasswordHasherService passwordHasher,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _tokenService = tokenService;
        _passwordHasher = passwordHasher;
        _currentUserService = currentUserService;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedInput = request.UsernameOrEmail.Trim().ToLowerInvariant();

        // 1. Find user by username or email
        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Username.ToLower() == normalizedInput || u.Email.ToLower() == normalizedInput, cancellationToken);

        if (user == null)
        {
            throw new UnauthorizedAccessException("Tên đăng nhập hoặc mật khẩu không chính xác.");
        }

        // 2. Verify password
        var isPasswordValid = _passwordHasher.VerifyPassword(user, request.Password, user.PasswordHash);
        if (!isPasswordValid)
        {
            throw new UnauthorizedAccessException("Tên đăng nhập hoặc mật khẩu không chính xác.");
        }

        // 3. Check User status
        if (!user.IsActive)
        {
            throw new UnauthorizedAccessException("Tài khoản đã bị vô hiệu hóa hoặc khóa tạm thời.");
        }

        // 4. Check Role status
        if (user.Role == null || !user.Role.IsActive)
        {
            throw new UnauthorizedAccessException("Vai trò của tài khoản hiện đang bị tạm ngừng.");
        }

        // 5. Generate Access Token (JWT - does NOT contain permissions)
        var (accessToken, expiresIn) = _tokenService.GenerateAccessToken(user, user.Role.Code);

        // 6. Generate Refresh Token
        var (refreshTokenString, refreshExpiresAt) = _tokenService.GenerateRefreshToken();
        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = refreshTokenString,
            ExpiresAt = refreshExpiresAt,
            IsRevoked = false,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = user.Username
        };

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync(cancellationToken);

        return new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshTokenString,
            ExpiresIn = expiresIn,
            TokenType = "Bearer",
            User = new CurrentUserDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                FullName = user.FullName,
                RoleId = user.RoleId,
                RoleCode = user.Role.Code,
                RoleName = user.Role.Name,
                IsActive = user.IsActive
            }
        };
    }

    public async Task<RefreshTokenResponse> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        // 1. Read claims from expired access token
        var principal = _tokenService.GetPrincipalFromExpiredToken(request.AccessToken);
        if (principal == null)
        {
            throw new UnauthorizedAccessException("Access Token không hợp lệ.");
        }

        var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("Access Token không chứa định danh người dùng hợp lệ.");
        }

        // 2. Validate Refresh Token in database
        var existingRefreshToken = await _context.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken && rt.UserId == userId, cancellationToken);

        if (existingRefreshToken == null || existingRefreshToken.IsRevoked || existingRefreshToken.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new UnauthorizedAccessException("Refresh Token không hợp lệ hoặc đã hết hạn.");
        }

        // 3. Check User status
        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null || !user.IsActive || user.Role == null || !user.Role.IsActive)
        {
            throw new UnauthorizedAccessException("Tài khoản hoặc vai trò đã bị khóa hoặc không tồn tại.");
        }

        // 4. Revoke old refresh token (Refresh Token Rotation)
        existingRefreshToken.IsRevoked = true;
        existingRefreshToken.RevokedAt = DateTimeOffset.UtcNow;

        // 5. Generate new pair of tokens
        var (newAccessToken, expiresIn) = _tokenService.GenerateAccessToken(user, user.Role.Code);
        var (newRefreshTokenString, refreshExpiresAt) = _tokenService.GenerateRefreshToken();

        var newRefreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = newRefreshTokenString,
            ExpiresAt = refreshExpiresAt,
            IsRevoked = false,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = user.Username
        };

        _context.RefreshTokens.Add(newRefreshToken);
        await _context.SaveChangesAsync(cancellationToken);

        return new RefreshTokenResponse
        {
            AccessToken = newAccessToken,
            RefreshToken = newRefreshTokenString,
            ExpiresIn = expiresIn,
            TokenType = "Bearer"
        };
    }

    public async Task<CurrentUserDto> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var currentUserId = _currentUserService.UserId;
        if (currentUserId == null)
        {
            throw new UnauthorizedAccessException("Không xác định được danh tính người dùng hiện tại.");
        }

        var user = await _context.Users
            .Include(u => u.Role)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == currentUserId.Value, cancellationToken);

        if (user == null)
        {
            throw new KeyNotFoundException("Không tìm thấy thông tin tài khoản người dùng.");
        }

        return new CurrentUserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            FullName = user.FullName,
            RoleId = user.RoleId,
            RoleCode = user.Role?.Code ?? string.Empty,
            RoleName = user.Role?.Name ?? string.Empty,
            IsActive = user.IsActive
        };
    }

    public async Task<RegisterUserResponse> RegisterUserAsync(RegisterUserRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedUsername = request.Username.Trim();
        var normalizedEmail = request.Email.Trim();

        // 1. Check duplicate username or email
        var isDuplicate = await _context.Users.AnyAsync(
            u => u.Username.ToLower() == normalizedUsername.ToLower() || u.Email.ToLower() == normalizedEmail.ToLower(),
            cancellationToken);

        if (isDuplicate)
        {
            throw new InvalidOperationException("Tên đăng nhập hoặc Email đã tồn tại trong hệ thống.");
        }

        // 2. Validate Role exists and active
        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Id == request.RoleId, cancellationToken);
        if (role == null)
        {
            throw new KeyNotFoundException("Vai trò (Role) được chỉ định không tồn tại.");
        }

        if (!role.IsActive)
        {
            throw new InvalidOperationException("Vai trò (Role) được chỉ định hiện đang bị tạm khóa.");
        }

        // 3. Determine password (use provided or generate a strong random one)
        var rawPassword = string.IsNullOrWhiteSpace(request.Password)
            ? _passwordHasher.GenerateRandomPassword(12)
            : request.Password;

        var user = new User
        {
            Id = Guid.NewGuid(),
            RoleId = role.Id,
            Username = normalizedUsername,
            Email = normalizedEmail,
            FullName = request.FullName.Trim(),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = _currentUserService.Username ?? "System"
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, rawPassword);

        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);

        return new RegisterUserResponse
        {
            UserId = user.Id,
            Username = user.Username,
            Email = user.Email,
            FullName = user.FullName,
            RoleId = role.Id,
            RoleCode = role.Code,
            InitialPassword = rawPassword,
            CreatedAt = user.CreatedAt
        };
    }

    public async Task<ResetPasswordResponse> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (user == null)
        {
            throw new KeyNotFoundException("Không tìm thấy người dùng cần reset mật khẩu.");
        }

        // Determine new password
        var newPassword = string.IsNullOrWhiteSpace(request.NewPassword)
            ? _passwordHasher.GenerateRandomPassword(12)
            : request.NewPassword;

        user.PasswordHash = _passwordHasher.HashPassword(user, newPassword);
        user.UpdatedAt = DateTimeOffset.UtcNow;
        user.UpdatedBy = _currentUserService.Username ?? "System";

        // Revoke all existing refresh tokens for this user
        var activeTokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == user.Id && !rt.IsRevoked)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.IsRevoked = true;
            token.RevokedAt = DateTimeOffset.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new ResetPasswordResponse
        {
            UserId = user.Id,
            Username = user.Username,
            NewPassword = newPassword,
            Message = "Mật khẩu đã được reset thành công và tất cả các phiên đăng nhập cũ đã được thu hồi."
        };
    }
}
