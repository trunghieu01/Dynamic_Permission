# Kế hoạch xây dựng API quản lý User, Role và Permission

## 1. Mục tiêu

Xây dựng các API quản trị User, Role, Permission và các quan hệ assignment theo Clean Architecture hiện tại. API phải dùng DTO riêng, kiểm tra đầu vào, chống dữ liệu trùng, phân trang/lọc danh sách, bảo vệ bằng Dynamic Permission và không làm lộ password hash.

Phạm vi gồm:

- **User Management:** tạo, cập nhật, khóa/mở khóa, liệt kê và xem chi tiết User.
- **Role Management:** tạo, cập nhật, xóa hoặc vô hiệu hóa, liệt kê và xem chi tiết Role.
- **Permission Management:** tạo, cập nhật, vô hiệu hóa, liệt kê và tìm theo Module.
- **Assignment Management:** gán/gỡ Role cho User; gán/gỡ Permission cho Role; truy vấn Role của User, Permission của Role và Permission hiệu lực của User.

Tài liệu này chỉ mô tả kế hoạch; chưa triển khai các API.

## 2. Hiện trạng và nguyên tắc thiết kế

- Dự án hiện dùng ASP.NET Core Controllers, .NET 7, EF Core và các tầng Domain, Application, Infrastructure, API.
- Các entity `User`, `Role`, `Permission`, `RolePermission`, `UserPermission` và `IApplicationDbContext` đã có.
- `User` hiện có một `RoleId` không nullable, tức mô hình hiện tại là mỗi User có đúng một Role.
- Đã có `IPermissionService` để tính quyền hiệu lực, cơ chế Dynamic Permission và `[HasPermission]`.
- Controller chỉ xử lý HTTP, binding/validation và chuyển kết quả; business logic nằm trong Application Services.
- Request/response dùng DTO riêng, không nhận/trả trực tiếp entity EF.
- Mọi response User đều loại trừ `PasswordHash`.
- Filter do client gửi qua query parameters, là tùy chọn và có thể kết hợp; không đặt điều kiện nghiệp vụ cố định cho endpoint danh sách.
- Dữ liệu permission hiệu lực luôn được tính phía server từ database/cache, không tin quyền do client cung cấp.

## 3. Mô hình dữ liệu và quyết định nghiệp vụ

### 3.1 User và Role

Mặc định giữ mô hình hiện tại: một User có tối đa một Role. Endpoint xem Role của User sẽ trả danh sách tương thích, với tối đa một phần tử.

**Quyết định:** User luôn phải có Role. Không cung cấp thao tác gỡ Role độc lập; `PUT /api/admin/users/{id}/role` thay thế Role hiện tại bằng Role mới trong một lần cập nhật. Không cần migration thay đổi nullability của `User.RoleId`.

### 3.2 Trạng thái hoạt động

- Khóa/mở khóa User dùng `User.IsActive`.
- Vô hiệu hóa Role dùng `Role.IsActive`. Xóa Role mặc định nên được thực hiện như soft delete/vô hiệu hóa để không làm hỏng liên kết hiện có.
- Permission hiện chưa có trạng thái hoạt động. Cần thêm `Permission.IsActive`, cập nhật EF configuration và tạo migration. Tính quyền hiệu lực phải bỏ qua Permission đã vô hiệu hóa.

### 3.3 Chống dữ liệu trùng

- User: Username và Email không trùng, không phân biệt hoa/thường theo quy tắc thống nhất với database.
- Role: Code không trùng.
- Permission: Code không trùng.
- Assignment: không tạo liên kết trùng; tận dụng composite primary key hiện có.
- Kiểm tra trước khi ghi để trả lỗi thân thiện, đồng thời xử lý vi phạm unique constraint để bảo vệ trước các request đồng thời.

### 3.4 Xóa và trạng thái tham chiếu

- **Quyết định:** Role/Permission chỉ được vô hiệu hóa, không xóa vật lý. `DELETE /api/admin/roles/{id}` thực hiện soft delete bằng cách đặt `IsActive = false`.
- Không gán Role/Permission đã vô hiệu hóa.

## 4. API dự kiến

Base path gợi ý: `/api/admin`. Tên route có thể điều chỉnh để phù hợp quy ước hiện tại, nhưng permission cần được áp dụng theo từng thao tác.

### 4.1 User Management

| Method | Route | Mô tả | Permission gợi ý |
|---|---|---|---|
| `POST` | `/api/admin/users` | Tạo User | `user.create` |
| `PUT` | `/api/admin/users/{id}` | Cập nhật thông tin User | `user.update` |
| `PUT` | `/api/admin/users/{id}/status` | Khóa hoặc mở khóa User | `user.lock` |
| `GET` | `/api/admin/users` | Lấy danh sách User | `user.view` |
| `GET` | `/api/admin/users/{id}` | Xem chi tiết User | `user.view` |
| `PUT` | `/api/admin/users/{id}/role` | Gán/thay Role cho User | `user.role.assign` |
| `DELETE` | `/api/admin/users/{id}/role` | Gỡ Role khỏi User nếu nghiệp vụ/schema cho phép | `user.role.assign` |
| `GET` | `/api/admin/users/{id}/roles` | Lấy Role của User | `user.view` |
| `GET` | `/api/admin/users/{id}/effective-permissions` | Lấy Permission hiệu lực của User | `user.permissions.view` |

Filter danh sách dự kiến: `search` (username/email/full name), `isActive`, `roleId`, `page`, `pageSize`, `sortBy`, `sortDirection`. Tất cả đều tùy chọn; không có filter ngầm định trạng thái.

### 4.2 Role Management

| Method | Route | Mô tả | Permission gợi ý |
|---|---|---|---|
| `POST` | `/api/admin/roles` | Tạo Role | `role.create` |
| `PUT` | `/api/admin/roles/{id}` | Cập nhật Role | `role.update` |
| `DELETE` | `/api/admin/roles/{id}` | Xóa/vô hiệu hóa Role theo quy tắc tham chiếu | `role.deactivate` |
| `PUT` | `/api/admin/roles/{id}/status` | Kích hoạt/vô hiệu hóa Role | `role.deactivate` |
| `GET` | `/api/admin/roles` | Lấy danh sách Role | `role.view` |
| `GET` | `/api/admin/roles/{id}` | Xem chi tiết Role | `role.view` |
| `PUT` | `/api/admin/roles/{id}/permissions/{permissionId}` | Gán Permission cho Role | `role.permission.assign` |
| `DELETE` | `/api/admin/roles/{id}/permissions/{permissionId}` | Gỡ Permission khỏi Role | `role.permission.assign` |
| `GET` | `/api/admin/roles/{id}/permissions` | Lấy Permission của Role | `role.view` |

Filter danh sách dự kiến: `search` (code/name), `isActive`, `page`, `pageSize`, `sortBy`, `sortDirection`. Tất cả filter là tùy chọn.

### 4.3 Permission Management

| Method | Route | Mô tả | Permission gợi ý |
|---|---|---|---|
| `POST` | `/api/admin/permissions` | Tạo Permission | `permission.create` |
| `PUT` | `/api/admin/permissions/{id}` | Cập nhật Permission | `permission.update` |
| `PUT` | `/api/admin/permissions/{id}/status` | Kích hoạt/vô hiệu hóa Permission | `permission.deactivate` |
| `GET` | `/api/admin/permissions` | Lấy danh sách Permission | `permission.view` |
| `GET` | `/api/admin/permissions/{id}` | Xem chi tiết Permission | `permission.view` |

Filter danh sách dự kiến: `search` (code/name), `module`, `action`, `isActive`, `page`, `pageSize`, `sortBy`, `sortDirection`. Tất cả filter là tùy chọn; tìm theo Module là filter, không phải giá trị cố định.

## 5. DTO, validation và HTTP behavior

- Tạo Request/Response DTO riêng cho create, update, trạng thái và assignment.
- DTO không chứa trường điều khiển quyền của caller; các quyền hiệu lực không nhận từ body/query/header.
- Validate bắt buộc, độ dài, định dạng, giá trị trạng thái và Guid trước khi gọi service.
- Tạo User nhận password dạng input; hash bằng `IPasswordHasherService`, không lưu plain text và không đưa hash vào response/log.
- Response User chỉ chứa thông tin cần thiết như Id, username, email, full name, trạng thái, Role và thời điểm audit phù hợp.
- Dùng status code nhất quán:
  - `200 OK`: truy vấn/cập nhật thành công.
  - `201 Created`: tạo mới thành công, kèm `Location`.
  - `204 No Content`: gỡ assignment/xóa thành công nếu endpoint không cần response body.
  - `400 Bad Request`: request không hợp lệ.
  - `401 Unauthorized`: chưa xác thực/token không hợp lệ.
  - `403 Forbidden`: đã xác thực nhưng thiếu permission.
  - `404 Not Found`: không tìm thấy entity.
  - `409 Conflict`: trùng dữ liệu hoặc trạng thái không thể thực hiện do liên kết.
- `ApiExceptionMiddleware` chuyển lỗi nghiệp vụ sang RFC 7807 Problem Details; lỗi bất ngờ chỉ trả thông báo chung và được ghi log.

## 6. Pagination, filtering và sorting

- Tạo query DTO riêng cho từng endpoint list hoặc dùng abstraction chung chỉ cho phần pagination/filter thực sự dùng chung.
- `page` và `pageSize` là tham số do caller cung cấp, có validation và giới hạn `pageSize` tối đa để bảo vệ tài nguyên.
- Nếu không truyền filter, trả tất cả bản ghi phù hợp với pagination, không tự thêm điều kiện như chỉ lấy active.
- Chỉ cho phép sort theo danh sách field được hỗ trợ; không ghép trực tiếp tên cột từ input vào SQL.
- Response phân trang thống nhất: `items`, `page`, `pageSize`, `totalCount` (và `totalPages` nếu phù hợp).
- Tính `totalCount` dựa trên toàn bộ filter đã áp dụng nhưng trước khi skip/take.

## 7. Clean Architecture và transaction

### API

- Tạo controllers theo resource: Users, Roles, Permissions và Assignments (có thể gom assignment vào controller resource nếu route rõ ràng).
- Controller chịu trách nhiệm HTTP semantics, model validation, cancellation token và mapping kết quả service thành status code.
- Bảo vệ endpoint quản trị bằng `[HasPermission("...")]` trên từng action.

### Application

- Tạo service interface/implementation cho User, Role, Permission và assignment.
- Tập trung validation nghiệp vụ, chống trùng, kiểm tra tham chiếu và mapping DTO/entity ở Application.
- Sử dụng `IApplicationDbContext` thay vì inject DbContext vào Controller.
- Tái sử dụng `IPermissionService` cho quyền hiệu lực; không nhân đôi thuật toán tính quyền.

### Infrastructure / Domain

- Dùng cấu hình EF Core hiện có, bổ sung trạng thái Permission và migration.
- Giữ unique index/composite key ở database để bảo vệ tính duy nhất.
- Tạo transaction tường minh khi thao tác gồm nhiều lần ghi cần atomic; thao tác đơn lẻ chỉ cần một `SaveChanges`.
- Invalidate permission cache khi thay đổi trạng thái User/Role/Permission, gán Role cho User hoặc gán Permission cho Role. Xác minh cache invalidation bao phủ mọi đường ghi dữ liệu.

### Cập nhật database

Migration `AddPermissionStatusAndSeedAdminPermissions` thêm `Permissions.IsActive`, seed các permission code quản trị và Role `ADMIN`, đồng thời gán các permission đó cho Role. Migration không gán bất kỳ User nào vào Role.

Trước khi chạy API trên database hiện hữu, áp dụng migration bằng:

```powershell
dotnet ef database update --project src\DynamicPermission.Infrastructure\DynamicPermission.Infrastructure.csproj --startup-project src\DynamicPermission.API\DynamicPermission.API.csproj
```

Sau migration, quy trình vận hành phải gán Role `ADMIN` cho User quản trị đầu tiên qua database hoặc kênh bootstrap được kiểm soát. API không thể tự cấp quyền cho caller.

## 8. Khởi tạo quyền quản trị

Trước khi bật bảo vệ toàn bộ API quản trị, cần có quy trình tạo các permission code quản trị và cấp chúng cho Role/tài khoản quản trị ban đầu. Nếu không, hệ thống có thể không còn principal nào đủ quyền để quản trị.

**Quyết định:** Seed Role `ADMIN`, các permission code quản trị và assignment của chúng qua migration có thể áp dụng lặp an toàn. Không tự gán User nào vào Role này. Policy vẫn được tạo động từ permission code; việc gán User quản trị đầu tiên cần do vận hành thực hiện riêng.

## 9. Kế hoạch kiểm thử

### Unit tests cho Application Services

- User: tạo thành công, username/email trùng, cập nhật, khóa/mở khóa, không lộ password hash.
- Role: tạo/cập nhật, code trùng, vô hiệu hóa, xóa khi còn User tham chiếu.
- Permission: tạo/cập nhật, code trùng, filter Module, vô hiệu hóa.
- Assignment: gán/gỡ Role, gán/gỡ Permission, entity không tồn tại, assignment trùng, Role/Permission inactive, quy tắc gỡ Role đã thống nhất.
- Effective permission: Role/User/Permission inactive, grant/deny, thay đổi assignment và cache invalidation.
- List query: kết hợp filter, bỏ filter, pagination, sorting hợp lệ, tổng số bản ghi.

### API-level tests

- Kiểm tra `201` và Location khi tạo; `400` validation; `404` khi không tồn tại; `409` khi trùng/tham chiếu xung đột.
- Kiểm tra `401` khi chưa đăng nhập và `403` khi không có permission.
- Kiểm tra mọi endpoint quản trị có permission phù hợp.
- Kiểm tra mọi endpoint danh sách hoạt động khi filter không truyền và không áp dụng điều kiện cố định.
- Kiểm tra response User không bao giờ chứa `PasswordHash`.

## 10. Các bước triển khai

1. Hoàn tất các quyết định nghiệp vụ đã chốt trong tài liệu này.
2. Cập nhật Domain, EF configuration và migration cho trạng thái Permission cùng dữ liệu seed quản trị.
3. Xây dựng DTO, query filter/pagination và Application services.
4. Thêm API controllers, permission attributes, HTTP metadata và OpenAPI response types.
5. Hoàn thiện cache invalidation và quy trình seed/bootstrap các permission quản trị.
6. Viết unit tests cho services, query và permission effective; bổ sung API tests phù hợp cấu trúc solution.
7. Cập nhật `.http` với request mẫu cho các endpoint.
8. Chạy build và toàn bộ test suite; kiểm tra status code, authorization, duplicate handling, filtering và không lộ password hash.

## 11. Các quyết định đã chốt

1. User luôn có đúng một Role; thay Role bằng endpoint assignment, không gỡ độc lập.
2. Role và Permission được vô hiệu hóa, không xóa vật lý.
3. Migration seed quyền quản trị và Role `ADMIN`, không tự gán User.
4. Một User có tối đa một Role trong phạm vi triển khai này.
