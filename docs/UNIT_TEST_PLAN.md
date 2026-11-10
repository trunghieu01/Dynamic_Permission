# KẾ HOẠCH TRIỂN KHAI UNIT TEST (KIỂM THỬ ĐƠN VỊ)
## DỰ ÁN DYNAMIC PERMISSION

---

## 1. MỤC TIÊU & PHẠM VI KIỂM THỬ

Kế hoạch này phác thảo chi tiết chiến lược, công nghệ và danh sách các kịch bản kiểm thử đơn vị (Unit Test) cho hệ thống `DynamicPermission`, tập trung vào việc **kiểm tra toàn bộ logic nghiệp vụ, các hàm xử lý cốt lõi và các API endpoint**.

### Mục tiêu:
1. **Kiểm tra tính hợp lệ của Input (Input / Model Validation):** Kiểm tra tất cả các quy tắc Data Annotations (`[Required]`, `[EmailAddress]`, `[StringLength]`, `[RegularExpression]`) của các DTOs (Login, Register, RefreshToken, ResetPassword) để ngăn chặn dữ liệu bẩn ngay từ cửa ngõ.
2. **Kiểm tra logic nghiệp vụ (Application Layer):** Đảm bảo `AuthService` xử lý đúng 100% các nhánh rẽ: thành công, dữ liệu không hợp lệ, tài khoản/vai trò bị khóa, lỗi token, v.v.
3. **Kiểm tra các function bảo mật (Infrastructure Layer):** Xác thực tính đúng đắn của việc sinh JWT (đúng claims, không chứa permission), băm và verify mật khẩu (`PasswordHasher`), sinh Refresh Token an toàn.
4. **Kiểm tra API Endpoints (API Layer):** Kiểm thử `AuthController` phản hồi đúng mã HTTP Status Code (`200`, `201`, `400`, `401`, `404`) khi nhận input hợp lệ cũng như khi input không hợp lệ.
5. **Cô lập kiểm thử (Isolation):** Sử dụng Mocking và EF Core InMemory DbContext, không phụ thuộc vào Database SQL Server thật, đảm bảo test chạy cực nhanh (mili-giây) và độc lập.

---

## 2. CÔNG NGHỆ & THƯ VIỆN SỬ DỤNG

* **Testing Framework:** `xUnit` (Chuẩn công nghiệp hiện đại cho .NET 7).
* **Mocking Framework:** `Moq` (hoặc `NSubstitute`) để giả lập hành vi các Interface (`ITokenService`, `IPasswordHasherService`, `ICurrentUserService`, `IAuthService`).
* **Assertion Library:** `FluentAssertions` (Giúp câu lệnh assert trực quan, dễ đọc: `result.Should().NotBeNull()`).
* **In-Memory Database:** `Microsoft.EntityFrameworkCore.InMemory` để tạo database giả lập trong RAM chạy test riêng cho từng test case của DbContext.
* **Test Runner:** `dotnet test`.

---

## 3. CẤU TRÚC DỰ ÁN TEST (TEST PROJECT STRUCTURE)

Dự án kiểm thử sẽ được đặt trong thư mục `tests/`:

```
DynamicPermission/
├── src/
│   ├── DynamicPermission.Domain/
│   ├── DynamicPermission.Application/
│   ├── DynamicPermission.Infrastructure/
│   └── DynamicPermission.API/
└── tests/
    └── DynamicPermission.UnitTests/
        ├── DynamicPermission.UnitTests.csproj
        ├── Common/
        │   ├── TestDbContextFactory.cs          # Helper tạo InMemory DbContext độc lập cho từng test
        │   ├── TestDataBuilder.cs               # Helper tạo dữ liệu mẫu (User, Role, RefreshToken)
        │   └── ModelValidationHelper.cs         # Helper validate DataAnnotations qua Validator.TryValidateObject
        ├── Validation/
        │   └── ModelValidationTests.cs          # Test chuyên biệt tính hợp lệ của Input (DTOs)
        ├── Application/
        │   └── Services/
        │       └── AuthServiceTests.cs          # Test logic nghiệp vụ AuthService
        ├── Infrastructure/
        │   └── Authentication/
        │       ├── TokenServiceTests.cs         # Test sinh JWT, RefreshToken, đọc expired token
        │       └── PasswordHasherServiceTests.cs # Test hash, verify, generate random password
        └── API/
            └── Controllers/
                └── AuthControllerTests.cs       # Test các API endpoints của AuthController
```

---

## 4. MA TRẬN KỊCH BẢN KIỂM THỬ (TEST CASES SPECIFICATION)

### 4.0. Validation Layer: `ModelValidationTests` (Kiểm thử tính hợp lệ của Input DTOs)

Kiểm tra toàn diện tất cả các thuộc tính ràng buộc dữ liệu (`[Required]`, `[StringLength]`, `[EmailAddress]`, `[RegularExpression]`):

| Test Case ID | Đối tượng DTO | Kịch bản kiểm thử | Dữ liệu đầu vào | Kết quả mong đợi |
| :--- | :--- | :--- | :--- | :--- |
| `VAL_LOG_01` | `LoginRequest` | Thiếu `UsernameOrEmail` | `UsernameOrEmail = ""` | Không hợp lệ (`ErrorMessage: "Tên đăng nhập hoặc Email không được để trống"`) |
| `VAL_LOG_02` | `LoginRequest` | Thiếu `Password` | `Password = ""` | Không hợp lệ (`ErrorMessage: "Mật khẩu không được để trống"`) |
| `VAL_LOG_03` | `LoginRequest` | Dữ liệu đầy đủ và đúng định dạng | `UsernameOrEmail = "admin"`, `Password = "123456"` | Hợp lệ (`IsValid = true`, không có lỗi) |
| `VAL_REG_01` | `RegisterUserRequest` | Thiếu `Username` | `Username = ""` | Không hợp lệ (`ErrorMessage: "Tên đăng nhập không được để trống"`) |
| `VAL_REG_02` | `RegisterUserRequest` | `Username` quá ngắn (< 3 ký tự) | `Username = "ab"` | Không hợp lệ (`ErrorMessage: "Tên đăng nhập phải từ 3 đến 50 ký tự"`) |
| `VAL_REG_03` | `RegisterUserRequest` | `Username` quá dài (> 50 ký tự) | `Username = 51 ký tự` | Không hợp lệ (`ErrorMessage: "Tên đăng nhập phải từ 3 đến 50 ký tự"`) |
| `VAL_REG_04` | `RegisterUserRequest` | `Username` chứa ký tự đặc biệt hoặc khoảng trắng | `Username = "user@name #1"` | Không hợp lệ (`ErrorMessage: "Tên đăng nhập chỉ chứa chữ cái, số, dấu gạch dưới và dấu chấm"`) |
| `VAL_REG_05` | `RegisterUserRequest` | Thiếu `Email` | `Email = ""` | Không hợp lệ (`ErrorMessage: "Email không được để trống"`) |
| `VAL_REG_06` | `RegisterUserRequest` | `Email` sai định dạng | `Email = "not-an-email"`, `@test`, `test@` | Không hợp lệ (`ErrorMessage: "Email không đúng định dạng"`) |
| `VAL_REG_07` | `RegisterUserRequest` | Thiếu `FullName` | `FullName = ""` | Không hợp lệ (`ErrorMessage: "Họ và tên không được để trống"`) |
| `VAL_REG_08` | `RegisterUserRequest` | `Password` có nhập nhưng quá ngắn (< 6 ký tự) | `Password = "12345"` | Không hợp lệ (`ErrorMessage: "Mật khẩu nếu nhập phải từ 6 ký tự trở lên"`) |
| `VAL_REG_09` | `RegisterUserRequest` | `Password` để trống (cho phép để hệ thống tự sinh ngẫu nhiên) | `Password = null` | Hợp lệ (`IsValid = true`) |
| `VAL_REG_10` | `RegisterUserRequest` | Dữ liệu hoàn toàn hợp lệ | Đầy đủ username hợp lệ, email chuẩn, họ tên, roleId, password >= 6 ký tự | Hợp lệ (`IsValid = true`) |
| `VAL_REF_01` | `RefreshTokenRequest` | Thiếu `AccessToken` | `AccessToken = ""` | Không hợp lệ (`ErrorMessage: "AccessToken không được để trống"`) |
| `VAL_REF_02` | `RefreshTokenRequest` | Thiếu `RefreshToken` | `RefreshToken = ""` | Không hợp lệ (`ErrorMessage: "RefreshToken không được để trống"`) |
| `VAL_REF_03` | `RefreshTokenRequest` | Đầy đủ AccessToken và RefreshToken | Cả 2 chuỗi đều có giá trị | Hợp lệ (`IsValid = true`) |
| `VAL_RST_01` | `ResetPasswordRequest` | `NewPassword` có nhập nhưng quá ngắn (< 6 ký tự) | `NewPassword = "123"` | Không hợp lệ (`ErrorMessage: "Mật khẩu mới nếu nhập phải từ 6 ký tự trở lên"`) |
| `VAL_RST_02` | `ResetPasswordRequest` | `NewPassword` để trống (hệ thống tự sinh) | `NewPassword = null` | Hợp lệ (`IsValid = true`) |
| `VAL_RST_03` | `ResetPasswordRequest` | Dữ liệu reset hoàn toàn hợp lệ | `UserId = Guid.NewGuid()`, `NewPassword = "NewPass@123"` | Hợp lệ (`IsValid = true`) |

---

### 4.1. Application Layer: `AuthServiceTests` (Kiểm thử logic nghiệp vụ)

#### Chức năng 1: `LoginAsync`
| Test Case ID | Tên kịch bản | Dữ liệu đầu vào | Kết quả mong đợi |
| :--- | :--- | :--- | :--- |
| `AUTH_LOG_01` | Đăng nhập thành công bằng Username | Username đúng + Password đúng | Trả về `LoginResponse` (AccessToken, RefreshToken, UserDto); bản ghi RefreshToken được lưu vào DB |
| `AUTH_LOG_02` | Đăng nhập thành công bằng Email | Email đúng + Password đúng | Trả về `LoginResponse` hợp lệ |
| `AUTH_LOG_03` | Đăng nhập thất bại do User không tồn tại | Username/Email không có trong DB | Ném `UnauthorizedAccessException` ("Tên đăng nhập hoặc mật khẩu không chính xác") |
| `AUTH_LOG_04` | Đăng nhập thất bại do sai mật khẩu | User tồn tại + Password sai | Ném `UnauthorizedAccessException` ("Tên đăng nhập hoặc mật khẩu không chính xác") |
| `AUTH_LOG_05` | Đăng nhập thất bại do User bị khóa | `User.IsActive = false` | Ném `UnauthorizedAccessException` ("Tài khoản đã bị vô hiệu hóa...") |
| `AUTH_LOG_06` | Đăng nhập thất bại do Role bị khóa | `Role.IsActive = false` | Ném `UnauthorizedAccessException` ("Vai trò của tài khoản hiện đang bị tạm ngừng") |

---

#### Chức năng 2: `RefreshTokenAsync`
| Test Case ID | Tên kịch bản | Dữ liệu đầu vào | Kết quả mong đợi |
| :--- | :--- | :--- | :--- |
| `AUTH_REF_01` | Làm mới token thành công | AccessToken cũ hợp lệ + RefreshToken hợp lệ trong DB | Trả về `RefreshTokenResponse` mới; RefreshToken cũ bị thu hồi (`IsRevoked = true`), RefreshToken mới được lưu |
| `AUTH_REF_02` | Thất bại do AccessToken không hợp lệ | Chuỗi token rác hoặc sai cấu trúc | Ném `UnauthorizedAccessException` ("Access Token không hợp lệ") |
| `AUTH_REF_03` | Thất bại do RefreshToken đã hết hạn | `ExpiresAt < UtcNow` | Ném `UnauthorizedAccessException` ("Refresh Token không hợp lệ hoặc đã hết hạn") |
| `AUTH_REF_04` | Thất bại do RefreshToken đã bị thu hồi | `IsRevoked = true` | Ném `UnauthorizedAccessException` ("Refresh Token không hợp lệ hoặc đã hết hạn") |
| `AUTH_REF_05` | Thất bại do User hoặc Role bị khóa | Token hợp lệ nhưng `User.IsActive = false` | Ném `UnauthorizedAccessException` ("Tài khoản hoặc vai trò đã bị khóa...") |

---

#### Chức năng 3: `GetCurrentUserAsync`
| Test Case ID | Tên kịch bản | Dữ liệu đầu vào | Kết quả mong đợi |
| :--- | :--- | :--- | :--- |
| `AUTH_ME_01` | Lấy thông tin thành công | `UserId` từ `ICurrentUserService` tồn tại trong DB | Trả về `CurrentUserDto` đầy đủ `Id, Username, Email, RoleCode` |
| `AUTH_ME_02` | Thất bại do chưa đăng nhập | `ICurrentUserService.UserId = null` | Ném `UnauthorizedAccessException` ("Không xác định được danh tính...") |
| `AUTH_ME_03` | Thất bại do UserId không có trong DB | `UserId` không khớp bản ghi nào | Ném `KeyNotFoundException` ("Không tìm thấy thông tin tài khoản...") |

---

#### Chức năng 4: `RegisterUserAsync`
| Test Case ID | Tên kịch bản | Dữ liệu đầu vào | Kết quả mong đợi |
| :--- | :--- | :--- | :--- |
| `AUTH_REG_01` | Đăng ký thành công với mật khẩu tự sinh | Không truyền password, Role hợp lệ | Tạo User mới, mật khẩu tự sinh 12 ký tự, hash đúng, lưu DB, trả về `RegisterUserResponse` kèm InitialPassword |
| `AUTH_REG_02` | Đăng ký thành công với mật khẩu chỉ định | Có truyền password cụ thể | Tạo User mới, hash đúng password chỉ định |
| `AUTH_REG_03` | Thất bại do trùng Username | Username đã tồn tại trong DB | Ném `InvalidOperationException` ("Tên đăng nhập hoặc Email đã tồn tại...") |
| `AUTH_REG_04` | Thất bại do trùng Email | Email đã tồn tại trong DB | Ném `InvalidOperationException` ("Tên đăng nhập hoặc Email đã tồn tại...") |
| `AUTH_REG_05` | Thất bại do Role không tồn tại | `RoleId` không có trong DB | Ném `KeyNotFoundException` ("Vai trò không tồn tại") |
| `AUTH_REG_06` | Thất bại do Role bị khóa | `Role.IsActive = false` | Ném `InvalidOperationException` ("Vai trò hiện đang bị tạm khóa") |

---

#### Chức năng 5: `ResetPasswordAsync`
| Test Case ID | Tên kịch bản | Dữ liệu đầu vào | Kết quả mong đợi |
| :--- | :--- | :--- | :--- |
| `AUTH_RST_01` | Reset mật khẩu thành công | `UserId` hợp lệ, không truyền pass mới | Cập nhật `PasswordHash` mới, **thu hồi toàn bộ RefreshToken cũ của User**, trả về `ResetPasswordResponse` |
| `AUTH_RST_02` | Reset mật khẩu với pass chỉ định | `UserId` hợp lệ, có truyền pass mới | Cập nhật đúng hash của pass chỉ định |
| `AUTH_RST_03` | Thất bại do User không tồn tại | `UserId` không có trong DB | Ném `KeyNotFoundException` ("Không tìm thấy người dùng...") |

---

### 4.2. Infrastructure Layer: `TokenServiceTests` & `PasswordHasherServiceTests`

#### `TokenServiceTests`:
1. `GenerateAccessToken_ShouldReturnValidJwt_WithExpectedClaims`:
   * Kiểm tra có đủ các claim: `sub`, `nameid`, `unique_name`, `email`, `role`, `jti`.
   * **Kiểm tra TUYỆT ĐỐI KHÔNG chứa claim Permission**.
   * Kiểm tra `Issuer` và `Audience` khớp cấu hình.
2. `GenerateRefreshToken_ShouldReturnCryptographicallySecureString`:
   * Kiểm tra chuỗi trả về không rỗng, đúng định dạng Base64.
   * `ExpiresAt` đúng số ngày cấu hình trong tương lai.
3. `GetPrincipalFromExpiredToken_ShouldExtractClaims_EvenWhenExpired`:
   * Tạo một token đã hết hạn trong quá khứ $\rightarrow$ hàm vẫn trích xuất thành công `ClaimsPrincipal` và đọc được `UserId`.
4. `GetPrincipalFromExpiredToken_ShouldReturnNull_WhenTokenInvalidOrTampered`:
   * Token bị chỉnh sửa chữ ký hoặc chuỗi không phải JWT $\rightarrow$ trả về `null`.

#### `PasswordHasherServiceTests`:
1. `HashPassword_ShouldGenerateDifferentHash_ForSamePassword`:
   * Kiểm tra 2 lần băm cùng 1 mật khẩu phải ra 2 chuỗi khác nhau (do salt ngẫu nhiên).
2. `VerifyPassword_ShouldReturnTrue_WhenPasswordMatches`:
   * Mật khẩu đúng $\rightarrow$ trả về `true`.
3. `VerifyPassword_ShouldReturnFalse_WhenPasswordIncorrect`:
   * Mật khẩu sai $\rightarrow$ trả về `false`.
4. `GenerateRandomPassword_ShouldMeetComplexityRequirements`:
   * Độ dài đúng tham số truyền vào (mặc định 12).
   * Chứa ít nhất 1 chữ hoa, 1 chữ thường, 1 chữ số, 1 ký tự đặc biệt.

---

### 4.3. API Layer: `AuthControllerTests` (Kiểm thử API Endpoint)

Sử dụng Mock `IAuthService` để kiểm thử phản hồi HTTP Status Code của Controller:

1. **`POST /api/auth/login`:**
   * Dữ liệu hợp lệ $\rightarrow$ Trả về `200 OK` kèm `LoginResponse`.
   * `ModelState` không hợp lệ $\rightarrow$ Trả về `400 Bad Request`.
   * `IAuthService` ném `UnauthorizedAccessException` $\rightarrow$ Trả về `401 Unauthorized` kèm message lỗi.
2. **`POST /api/auth/refresh-token`:**
   * Dữ liệu hợp lệ $\rightarrow$ Trả về `200 OK` kèm `RefreshTokenResponse`.
   * `ModelState` không hợp lệ $\rightarrow$ Trả về `400 Bad Request`.
   * Token sai $\rightarrow$ Trả về `401 Unauthorized`.
3. **`GET /api/auth/me`:**
   * User đăng nhập hợp lệ $\rightarrow$ Trả về `200 OK` kèm `CurrentUserDto`.
   * Không xác định được User $\rightarrow$ Trả về `401 Unauthorized`.
   * Không tìm thấy User $\rightarrow$ Trả về `404 Not Found`.
4. **`POST /api/auth/register`:**
   * Dữ liệu hợp lệ $\rightarrow$ Trả về `201 Created` kèm `RegisterUserResponse`.
   * Trùng tài khoản (`InvalidOperationException`) $\rightarrow$ Trả về `400 Bad Request`.
   * Role không tồn tại (`KeyNotFoundException`) $\rightarrow$ Trả về `404 Not Found`.
5. **`POST /api/auth/reset-password`:**
   * Hợp lệ $\rightarrow$ Trả về `200 OK` kèm `ResetPasswordResponse`.
   * User không tồn tại $\rightarrow$ Trả về `404 Not Found`.

---

## 5. CÁC BƯỚC THỰC HIỆN TRIỂN KHAI

Sau khi kế hoạch được phê duyệt:
1. **Bước 1:** Khởi tạo project test `tests/DynamicPermission.UnitTests` (.NET 7) và add vào `DynamicPermission.sln`.
2. **Bước 2:** Cài đặt các package cần thiết: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Moq`, `FluentAssertions`, `Microsoft.EntityFrameworkCore.InMemory`.
3. **Bước 3:** Thêm reference từ test project tới `DynamicPermission.Domain`, `DynamicPermission.Application`, `DynamicPermission.Infrastructure`, `DynamicPermission.API`.
4. **Bước 4:** Xây dựng `TestDbContextFactory` để cấp phát DB in-memory riêng cho từng test.
5. **Bước 5:** Viết toàn bộ test cases cho `AuthServiceTests`.
6. **Bước 6:** Viết toàn bộ test cases cho `TokenServiceTests` và `PasswordHasherServiceTests`.
7. **Bước 7:** Viết toàn bộ test cases cho `AuthControllerTests`.
8. **Bước 8:** Chạy lệnh `dotnet test` và báo cáo kết quả (đảm bảo 100% tests Passed, không có lỗi).
