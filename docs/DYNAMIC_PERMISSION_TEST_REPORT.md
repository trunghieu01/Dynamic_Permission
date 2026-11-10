# Báo cáo kiểm thử Dynamic Permission

## 1. Phạm vi và kết quả

Đã kiểm tra xác thực, JWT, đánh giá quyền, Authorization Pipeline của ASP.NET Core, cache quyền, vòng đời Dependency Injection, transaction quan hệ, số lượng truy vấn cơ sở dữ liệu và các API xác thực cần quyền quản trị.

- Unit test: **127 đạt, 0 thất bại**.
- Integration test: **16 đạt, 0 thất bại**.
- Tổng cộng: **143 test case đạt, 0 thất bại, 0 bỏ qua**.
- Build toàn solution: **thành công, 0 cảnh báo, 0 lỗi**.
- Các test HTTP dùng `TestServer`, JWT bearer handler, policy provider, authorization handler, application service và EF Core InMemory.
- Test transaction, khóa ngoại và số lượng truy vấn dùng SQLite in-memory. Chưa chạy migration hoặc kiểm thử kết nối với SQL Server thật.

## 2. Danh mục đầy đủ test case

Danh mục dưới đây ghi từng test method trong test project; các `[Theory]` được chạy nhiều lần theo từng bộ dữ liệu. Vì vậy tổng số lần chạy (143) lớn hơn số test method được liệt kê.

Các bộ dữ liệu `[Theory]` được liệt kê cụ thể dưới đây để phân biệt từng lần chạy:

- Ánh xạ exception: 404 Not Found, 400 Bad Request, 401 Unauthorized, 403 Forbidden.
- Độ dài mật khẩu ngẫu nhiên: 10, 12, 16 và 24 ký tự.
- Username/email đăng nhập rỗng: chuỗi rỗng và `null`.
- Password đăng nhập rỗng: chuỗi rỗng và `null`.
- Username đăng ký rỗng: chuỗi rỗng và `null`.
- Username đăng ký chứa ký tự không hợp lệ: `user name`, `user@name`, `user#123`, `user!`.
- Email đăng ký rỗng: chuỗi rỗng và `null`.
- Email sai định dạng: `not-an-email`, `plainaddress`, `@missingusername.com`, `missingdomain@`.
- FullName đăng ký rỗng: chuỗi rỗng và `null`.
- Access token rỗng: chuỗi rỗng và `null`.
- Refresh token rỗng: chuỗi rỗng và `null`.

### 2.1 Unit test

#### AuthController — 11 test method

1. Đăng nhập hợp lệ trả về HTTP 200.
2. Đăng nhập với ModelState không hợp lệ trả về HTTP 400.
3. Lỗi đăng nhập không hợp lệ được chuyển cho centralized exception handler.
4. Làm mới token hợp lệ trả về HTTP 200.
5. Lỗi refresh token được chuyển cho centralized exception handler.
6. Lấy người dùng hiện tại khi đã xác thực trả về HTTP 200.
7. Lỗi khi lấy người dùng hiện tại được chuyển cho centralized exception handler.
8. Đăng ký người dùng hợp lệ trả về HTTP 201.
9. Lỗi trùng dữ liệu khi đăng ký được chuyển cho centralized exception handler.
10. Đặt lại mật khẩu cho người dùng tồn tại trả về HTTP 200.
11. Không tìm thấy người dùng khi đặt lại mật khẩu được chuyển cho centralized exception handler.

#### ApiExceptionMiddleware — 6 test method

1. Conflict exception tạo Problem Details an toàn.
2. Các exception đã biết được ánh xạ đúng status code mà không lộ message nội bộ (404, 400, 401 và 403).
3. Exception không dự kiến không làm lộ chi tiết kỹ thuật.
4. Ghi log có cấu trúc, che dữ liệu nhạy cảm trong request/response và không tiêu thụ request body.
5. Bỏ qua body quá lớn và giữ nguyên response.
6. Request bị hủy không bị ghi nhận như lỗi ứng dụng.

#### AuthService — 17 test method

1. Đăng nhập thành công bằng username và mật khẩu đúng.
2. Đăng nhập thành công bằng email.
3. Đăng nhập thất bại với thông tin xác thực không tồn tại.
4. Đăng nhập thất bại khi mật khẩu sai.
5. Từ chối đăng nhập khi User bị vô hiệu hóa.
6. Từ chối đăng nhập khi Role bị vô hiệu hóa.
7. Refresh token hợp lệ được xoay vòng.
8. Từ chối refresh khi access token không hợp lệ.
9. Từ chối refresh token hết hạn.
10. Từ chối refresh token đã bị thu hồi.
11. Lấy thông tin User hiện tại khi đã xác thực thành công.
12. Từ chối lấy User hiện tại khi thiếu UserId.
13. Đăng ký thành công và tự sinh mật khẩu khi request không cung cấp mật khẩu.
14. Từ chối đăng ký khi username hoặc email đã tồn tại.
15. Từ chối đăng ký khi Role không tồn tại.
16. Đặt lại mật khẩu thành công và thu hồi các refresh token cũ.
17. Báo không tìm thấy User khi đặt lại mật khẩu cho User không tồn tại.

#### PermissionService — 12 test method

1. Trả các quyền từ Role khi User và Role đang hoạt động.
2. Trả danh sách quyền rỗng khi User bị vô hiệu hóa.
3. Không trả quyền từ Role khi Role bị vô hiệu hóa.
4. Không áp dụng direct grant khi Role của User bị vô hiệu hóa.
5. Direct grant bổ sung quyền chưa được Role cấp.
6. Direct deny loại quyền dù Role có cấp quyền đó.
7. Loại quyền Permission đã bị vô hiệu hóa.
8. Làm mới quyền đã cache sau khi dữ liệu trong database thay đổi.
9. Làm mới quyền sau khi direct override thay đổi.
10. Làm mới quyền sau khi User bị vô hiệu hóa.
11. Làm mới quyền sau khi Role bị vô hiệu hóa.
12. Khử trùng quyền trùng mã được gán từ các Role khác nhau.

#### PermissionManagementService — 4 test method

1. Từ chối tạo Permission có code trùng, không phân biệt hoa thường.
2. Lọc danh sách Permission theo Module và không mặc định chỉ lấy Permission đang hoạt động.
3. Cập nhật trạng thái Permission.
4. Từ chối trường sắp xếp không được hỗ trợ.

#### RoleManagementService — 6 test method

1. Từ chối tạo Role có code trùng, không phân biệt hoa thường.
2. Vô hiệu hóa Role nhưng vẫn giữ bản ghi Role.
3. Cho phép kích hoạt lại Role.
4. Từ chối gán Permission trùng cho cùng Role.
5. Từ chối gán Permission đang bị vô hiệu hóa.
6. Lọc và phân trang danh sách Permission được gán cho Role.

#### UserManagementService — 6 test method

1. Tạo User có hash mật khẩu và không trả PasswordHash ra response.
2. Từ chối tạo User có username trùng, không phân biệt hoa thường.
3. Từ chối tạo User với Role đang bị vô hiệu hóa.
4. Áp dụng các bộ lọc tùy chọn và phân trang khi lấy danh sách User.
5. Thay Role cho User nhưng không cho phép User không có Role.
6. Lọc danh sách Role theo User, không trả Role của User khác.

#### PasswordHasherService — 4 test method

1. Cùng mật khẩu tạo ra hash khác nhau do salt ngẫu nhiên.
2. Xác minh thành công khi mật khẩu khớp.
3. Xác minh thất bại khi mật khẩu không khớp.
4. Mật khẩu ngẫu nhiên đáp ứng độ dài và độ phức tạp yêu cầu với độ dài 10, 12, 16 và 24 ký tự.

#### TokenService — 4 test method

1. Access token là JWT hợp lệ, có các claim mong đợi và không chứa Permission.
2. Refresh token là Base64 hợp lệ và có thời hạn chính xác.
3. Trích xuất claim từ access token đã hết hạn.
4. Trả về null với token bị sửa đổi hoặc không hợp lệ.

#### MemoryPermissionCache — 12 test method

1. Tải một lần và trả lại tập quyền đã cache.
2. Cache cả tập quyền rỗng.
3. Invalidate riêng cache của một User.
4. Invalidate cache của các User thuộc Role được chỉ định.
5. Invalidate toàn bộ cache.
6. Gộp các cache miss đồng thời của cùng một User thành một lần tải.
7. Cho phép tải đồng thời cho các User khác nhau.
8. Không cache snapshot đang tải dở nếu Role bị invalidate.
9. Giải phóng single-flight lock khi loader ném exception.
10. Giải phóng waiter khi request của waiter bị hủy.
11. Cache entry hết hạn theo cấu hình.
12. Trả bản sao để caller không thể sửa tập quyền đang cache.

#### PermissionAuthorizationHandler — 5 test method

1. Thành công khi User có Permission yêu cầu.
2. Không thành công khi User thiếu Permission.
3. Không thành công khi người gọi chưa xác thực.
4. Không thành công khi thiếu claim UserId.
5. Từ chối mặc định (fail closed) khi PermissionService ném exception.

#### PermissionAuthorizationPolicyProvider — 2 test method

1. Tạo policy có Permission requirement theo Permission code.
2. Dùng policy provider mặc định với policy không phải policy Permission.

#### Model validation — 19 test method

1. LoginRequest không hợp lệ khi username/email là chuỗi rỗng hoặc `null`.
2. LoginRequest không hợp lệ khi password là chuỗi rỗng hoặc `null`.
3. LoginRequest hợp lệ khi các trường có giá trị hợp lệ.
4. RegisterUserRequest không hợp lệ khi username là chuỗi rỗng hoặc `null`.
5. RegisterUserRequest không hợp lệ khi username quá ngắn.
6. RegisterUserRequest không hợp lệ khi username quá dài.
7. RegisterUserRequest không hợp lệ với username `user name`, `user@name`, `user#123` hoặc `user!`.
8. RegisterUserRequest không hợp lệ khi email là chuỗi rỗng hoặc `null`.
9. RegisterUserRequest không hợp lệ với email `not-an-email`, `plainaddress`, `@missingusername.com` hoặc `missingdomain@`.
10. RegisterUserRequest không hợp lệ khi FullName là chuỗi rỗng hoặc `null`.
11. RegisterUserRequest không hợp lệ khi mật khẩu được cung cấp nhưng quá ngắn.
12. RegisterUserRequest hợp lệ khi mật khẩu không được cung cấp.
13. RegisterUserRequest hợp lệ khi mọi trường có giá trị hợp lệ.
14. RefreshTokenRequest không hợp lệ khi access token là chuỗi rỗng hoặc `null`.
15. RefreshTokenRequest không hợp lệ khi refresh token là chuỗi rỗng hoặc `null`.
16. RefreshTokenRequest hợp lệ khi có cả hai token.
17. ResetPasswordRequest không hợp lệ khi mật khẩu mới quá ngắn.
18. ResetPasswordRequest hợp lệ khi không cung cấp mật khẩu mới.
19. ResetPasswordRequest hợp lệ khi mọi trường có giá trị hợp lệ.

### 2.2 Integration test

#### Authorization Pipeline — 12 test method

1. Đăng ký rồi đăng nhập, JWT nhận được sử dụng được và response không lộ PasswordHash.
2. Token thiếu/sai nhận HTTP 401; token hợp lệ nhưng thiếu quyền nhận HTTP 403.
3. Khóa User làm JWT hiện tại mất quyền truy cập dù token vẫn còn hiệu lực.
4. Vô hiệu hóa Role thu hồi cả direct grant đối với User đã xác thực.
5. Thu hồi Permission có hiệu lực với JWT hiện tại mà không cần cấp token mới.
6. Cập nhật Permission của Role làm mới quyền hiệu lực: quyền cũ bị thu hồi, quyền mới được cấp.
7. Đổi Role của User làm mới quyền với JWT hiện tại.
8. User thường không thể đăng ký User hoặc đặt lại mật khẩu nếu thiếu quyền quản trị.
9. Claim UserId giả mạo nhưng không có chữ ký hợp lệ không xác thực được.
10. Nhiều Permission cùng mã qua các Role không tạo quyền hiệu lực trùng lặp.
11. Kiểm tra lifetime DI: cache singleton; PermissionService, DbContext và authorization handler scoped.
12. Cache hết hạn sẽ tải lại snapshot Permission từ database.

#### Database và transaction — 4 test method

1. Commit transaction mới áp dụng invalidation Permission cache.
2. Rollback transaction loại bỏ invalidation đang chờ.
3. Cache hit không phát sinh thêm truy vấn database.
4. Quan hệ User-Role yêu cầu User có một Role tồn tại.

## 3. Đối chiếu các tình huống yêu cầu

| Tình huống | Kết quả kiểm thử |
|---|---|
| Đăng ký, đăng nhập | Đăng ký và đăng nhập qua API; kiểm tra response không chứa `PasswordHash`. |
| JWT hợp lệ/không hợp lệ | JWT hợp lệ truy cập API được cấp quyền; token thiếu, sai hoặc bị giả mạo nhận 401. |
| User bị khóa | JWT còn hạn nhưng User bị khóa nhận 403 ở API yêu cầu Permission. |
| User không có Role | Schema yêu cầu đúng một Role; test SQLite xác nhận khóa ngoại không cho lưu User tham chiếu Role không tồn tại. |
| User có nhiều Role | Không thể biểu diễn trong schema hiện tại. Có kiểm thử nhiều Permission và mã Permission trùng giữa các Role riêng; không thay đổi kiến trúc/schema. |
| Role có nhiều Permission | Unit và integration test xác nhận Permission hiệu lực được đánh giá qua authorization pipeline. |
| Permission trùng giữa hai Role | Kiểm tra tập quyền hiệu lực không tạo trùng lặp. |
| Có/không có Permission | Có quyền trả 200; thiếu quyền trả 403. |
| Thu hồi quyền khi User đang đăng nhập | JWT hiện tại bị từ chối ngay sau khi Permission bị gỡ khỏi Role. |
| Cập nhật quyền Role / đổi Role User | JWT hiện tại nhận tập quyền cập nhật sau thay đổi. |
| Cache hết hạn và invalidation | Kiểm tra hết hạn, thay đổi User/Role/Permission và direct override. |
| Giả mạo thông tin xác thực | Token có claim giả mạo nhưng chữ ký không hợp lệ nhận 401. |
| Transaction | Invalidation chỉ có hiệu lực sau commit và bị hủy khi rollback. |
| Hiệu năng truy vấn Permission | Cache miss gồm 2 truy vấn (tìm Role và tải snapshot); cache hit không tạo truy vấn bổ sung. |
| Dependency Injection lifetime | Cache singleton; PermissionService, DbContext và authorization handler scoped. |

## 4. Lỗi phát hiện và đã sửa

1. **Mức độ cao — API đăng ký thiếu phân quyền theo Permission.** Người dùng đã xác thực có thể gọi `/api/auth/register` và tự chọn `RoleId`, có khả năng tạo tài khoản đặc quyền. API hiện yêu cầu `user.create`; integration test xác nhận người dùng thường nhận 403.
2. **Mức độ cao — API đặt lại mật khẩu thiếu phân quyền theo Permission.** Người dùng đã xác thực có thể đặt lại mật khẩu của User khác. API hiện yêu cầu `user.update`; integration test xác nhận người dùng thường nhận 403.
3. **Mức độ cao — direct grant vẫn hiệu lực khi Role bị vô hiệu hóa.** Hiện tập quyền hiệu lực rỗng nếu User hoặc Role không hoạt động; đã có unit và integration test.
4. **Rò rỉ thông tin — AuthController trả exception message trực tiếp.** Lỗi được chuyển cho middleware tập trung, trả Problem Details an toàn.
5. **API cập nhật trạng thái/gán Role trả 500 trên .NET 7.** Validation metadata trên property được sinh từ positional record không được MVC hỗ trợ trong trường hợp này. DTO đã chuyển sang `init` property có validation attribute trực tiếp; integration test gọi các API thật để xác nhận.

## 5. Giới hạn và ghi chú kiến trúc

- Mô hình dữ liệu quy định mỗi User có đúng một Role bắt buộc. User không Role hoặc có nhiều Role cần thay đổi schema, migration và nghiệp vụ; phạm vi kiểm thử này không tự ý thay đổi kiến trúc.
- Nhiều Role có thể cấp cùng một Permission code; tập quyền hiệu lực xử lý theo tập hợp, không phân biệt hoa thường.
- Agent chuyên trách security review không báo cáo phát hiện có độ tin cậy cao. Kiểm thử end-to-end sau đó phát hiện các lỗi ở API quản trị xác thực và direct grant khi Role inactive; các lỗi này đã được sửa và kiểm thử lại.

## 6. Build và lệnh kiểm thử

```text
dotnet build DynamicPermission.sln --no-restore
Kết quả: thành công, 0 cảnh báo, 0 lỗi.

dotnet test DynamicPermission.sln --no-restore
Kết quả: 127 unit test và 16 integration test đạt; tổng 143, 0 thất bại, 0 bỏ qua.
```

## 7. Đề xuất cải thiện

- Nếu cần nhiều Role/User hoặc User không Role, lập kế hoạch migration riêng và cập nhật API gán quyền, cache invalidation, kiểm thử và sơ đồ ERD đồng bộ.
- Bổ sung kiểm thử migration và transaction trên SQL Server dùng trong production thông qua môi trường CI dùng một lần; hiện relational tests chạy với SQLite.
- Đặt baseline hiệu năng với quy mô User/Role/Permission thực tế và ngưỡng query-count/latency trong CI.
- Kiểm thử thêm việc vô hiệu hóa Permission và thay đổi direct grant/deny bằng provider SQL Server thực tế.
- Trước khi chạy nhiều API instance, cân nhắc distributed cache/invalidation; `IMemoryCache` chỉ nằm trên từng instance.
- Tiếp tục rà soát mọi API quản trị mới để đảm bảo có Permission phù hợp; duy trì quyền riêng cho đăng ký và đặt lại mật khẩu.
