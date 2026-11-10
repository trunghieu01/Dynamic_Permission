# Kế hoạch xây dựng Cache cho Dynamic Permission

## 1. Mục tiêu

Thiết kế cache danh sách quyền hiệu lực theo `UserId` bằng `IMemoryCache` cho giai đoạn một, giảm truy vấn database trong authorization nhưng không dùng dữ liệu quyền cũ để cấp quyền sau khi có thay đổi.

**Trạng thái:** Đã triển khai cache in-process. Redis và distributed invalidation chưa triển khai.

## 2. Hiện trạng

- `PermissionService` tính quyền hiệu lực từ User, Role, RolePermission, UserPermission và Permission; chỉ truy vấn database khi cache miss.
- `MemoryPermissionCache` dùng `IMemoryCache`, absolute TTL cấu hình được (mặc định 60 giây), per-User async single-flight và snapshot permission codes.
- Cache key: `dynamic-permission:v1:user:{userId}:generation:{userGeneration}`.
- Cache entry lưu thêm global generation và RoleId/role generation để phát hiện thay đổi Permission hoặc Role.
- `DynamicPermissionDbContext` gom thay đổi tracked và invalidate sau SaveChanges thành công; explicit transaction trì hoãn invalidation tới commit, rollback bỏ pending invalidation.
- Ghi User/UserPermission invalidate User; ghi Role/RolePermission invalidate Role; ghi Permission tăng global generation.
- Keyed lock được dọn sau khi không còn holder/waiter. Invalidation vẫn chỉ chia sẻ trong process hiện tại.

## 3. Thiết kế đã triển khai

### 3.1 Tách Cache Service khỏi Authorization Handler

Giữ handler chỉ làm authorization: lấy UserId đã xác thực, gọi `IPermissionService`, kiểm tra code quyền và fail-secure khi lỗi.

Đã tách trách nhiệm:

- `IPermissionService`: API lấy quyền hiệu lực cho một User.
- `IPermissionCache`: `GetOrCreateAsync`, single-flight, TTL, generation và invalidation.
- `PermissionCacheInvalidation`: gom UserId/RoleId/global invalidation.
- `PermissionCacheTransactionInterceptor`: trì hoãn invalidation trong explicit transaction tới commit và bỏ pending state khi rollback.

Handler không chứa logic cache. Invalidation áp dụng tự động cho tracked entity changes qua `DynamicPermissionDbContext`; không invalidate nếu SaveChanges lỗi.

### 3.2 Cache Key

Dùng key có namespace và User generation:

```text
dynamic-permission:v1:user:{userId}:generation:{generation}
```

- `userId` là Guid lấy từ identity đã xác thực, không lấy từ request để quyết định danh tính.
- `userGeneration` tăng khi User hoặc UserPermission thay đổi; key cũ không được đọc lại sau khi generation đổi.
- Cache entry lưu `globalGeneration`, dùng khi Permission entity thay đổi.
- Cache entry lưu RoleId và Role generation; thay đổi Role làm miss cache của những User đã nạp từ Role đó mà không cần duyệt toàn bộ cache.

### 3.3 Cache Value

Lưu snapshot chỉ gồm permission code hiệu lực, so sánh không phân biệt hoa/thường:

```text
PermissionCacheSnapshot(RoleId, permission codes) -> private string[] in cache
```

Không cache entity EF, navigation property, password, JWT claim hoặc object gắn với DbContext. Khi trả kết quả, tạo set copy để caller không sửa cache value. Cache cả tập rỗng để tránh lặp truy vấn.

### 3.4 Cache Expiration

Thiết lập hiện tại:

- Absolute expiration: **60 giây mặc định**, cấu hình tại `PermissionCache:ExpirationSeconds`.
- Sliding expiration: không dùng.

TTL ngắn là lớp bảo vệ bổ sung khi invalidation bị bỏ sót; không thay thế invalidation. Dùng `MemoryCacheEntryOptions` tập trung trong cache service để dễ đo lường và điều chỉnh bằng configuration sau này.

### 3.5 Quy trình đọc cache và chống cache stampede

1. Thử cache key/generation trong bộ nhớ; cache hit trả snapshot ngay, không query database.
2. Nếu miss, lấy per-User asynchronous lock/single-flight để chỉ một request nạp quyền cho User đó.
3. Sau khi lấy lock, đọc lại cache; request đang chờ có thể dùng kết quả vừa được nạp.
4. Cache miss leader resolve RoleId rồi load permission snapshot. Chỉ cache nếu User/global/Role generation còn đúng và RoleId từ dữ liệu nạp khớp.
5. Cache miss hiện cần hai truy vấn: resolve User.RoleId và load permission snapshot.
6. Nếu phát hiện invalidation trong lúc load, không cache/trả snapshot cũ mà thử lại theo generation mới.
7. Trả set copy; giải phóng lock khi thành công, cancellation hoặc lỗi.

Không dùng global lock cho mọi User. Keyed lock đếm reference và chỉ xóa semaphore khi không còn holder/waiter.

### 3.6 Race condition và cache invalidation

Thay đổi tracked entity invalidate sau `SaveChanges` thành công; explicit transaction trì hoãn tới commit qua `PermissionCacheTransactionInterceptor`:

| Thay đổi | Cache cần invalidate |
|---|---|
| Gán/thay Role cho User | User đó |
| Gỡ Role khỏi User (nếu có trong tương lai) | User đó |
| Gán/gỡ Permission cho Role | Tất cả User đang có Role đó |
| Role bị vô hiệu hóa/kích hoạt lại | Tất cả User đang có Role đó |
| User bị khóa/mở khóa | User đó |
| Grant/deny trực tiếp cho User hoặc thay đổi override | User đó |
| Permission bị vô hiệu hóa/kích hoạt lại, đổi code hoặc metadata | Global generation |
| Tạo Permission mới | Global generation; an toàn nhưng invalidates rộng hơn cần thiết |

Role invalidation tăng generation riêng của Role. Các cache entry đã nạp từ Role đó miss ở lần đọc kế tiếp; không cần truy vấn UserIds hoặc duyệt/xóa trực tiếp toàn bộ entry.

Để tránh stale fill race:

- Cache miss ghi nhận User/global generation trước khi resolve RoleId và Role generation trước khi query permissions.
- Request đang query chỉ được set cache nếu generation không đổi và RoleId vẫn khớp.
- Nếu generation đổi trong lúc load, không cache/trả snapshot cũ; loader retry theo generation mới.
- Cache hit đối chiếu User/global/Role generation trước khi trả snapshot.

Với explicit transaction, interceptor áp dụng invalidation ở commit callback và bỏ pending state khi rollback. Thay đổi Permission dùng global generation vì có thể ảnh hưởng Role grants lẫn direct overrides.

### 3.7 Fail-secure và cập nhật quyền ngay lập tức

- Authorization vẫn từ chối nếu đọc database/cache lỗi; không tạo kết quả fallback cấp quyền.
- Invalidation lỗi sau commit không bị nuốt im lặng; lỗi được propagate và được ghi log bởi error middleware.
- Với cache miss có lỗi DB, không lưu kết quả rỗng giả vào cache.
- Thay đổi direct grant/deny tăng User generation sau commit; request kế tiếp nạp lại từ DB. Không sửa collection trong cache tại chỗ.
- Hành vi “hiệu lực ngay lập tức” trong single instance được bảo đảm sau khi mutation commit và invalidation hoàn tất; request authorization đã bắt đầu trước thời điểm commit có thể hoàn tất theo snapshot trước đó. Cần nêu rõ ranh giới này và kiểm thử.

## 4. Giới hạn nhiều API Instance và lộ trình Redis

`IMemoryCache` là cache cục bộ từng process. Invalidation ở instance A **không** tự xóa cache instance B. Hiện tại:

- Cần triển khai một API instance nếu yêu cầu invalidation quyền tức thì.
- Nếu chạy nhiều instance, quyền có thể stale tối đa TTL 60 giây ở instance khác; local memory cache không bảo đảm consistency tức thì cross-instance.
- Nếu yêu cầu bảo mật bắt buộc quyền đổi có hiệu lực đồng thời trên mọi instance, không bật multi-instance với local memory cache đơn thuần.
- Giai đoạn Redis thay `IPermissionCache` bằng implementation Redis, giữ cùng contract key/value/TTL.
- Đồng bộ invalidation giữa instance qua Redis Pub/Sub hoặc distributed event. Để không mất event khi consumer offline, lựa chọn production nên là transactional outbox trong cùng DB transaction và publisher/retry, hoặc cơ chế generation phân tán có tính bền vững.
- Local L1 cache kết hợp Redis L2 chỉ được thêm nếu có distributed invalidation/generation và kiểm thử stale-read; nếu không, L1 có thể tiếp tục cấp quyền cũ.
- Redis Pub/Sub đơn thuần không bảo đảm delivery. Nếu cần security consistency chặt, dùng durable outbox/version hoặc kiểm tra generation nguồn đáng tin cậy; đánh đổi latency/DB load được đánh giá riêng.

## 5. Cấu hình và đăng ký DI

- Đăng ký `IMemoryCache` singleton và `MemoryPermissionCache` singleton.
- `IPermissionService` tiếp tục scoped vì dùng `IApplicationDbContext`.
- Cache service không giữ scoped DbContext hay bất kỳ request state nào.
- TTL cấu hình tại `PermissionCache:ExpirationSeconds`, mặc định 60 giây.
- Meter `DynamicPermission.Authorization` phát metric hit/miss/load/load failure/load duration/invalidation theo scope. Không tag UserId hoặc ghi permission set.

## 6. Unit Test cho Cache Service

Unit tests đã bao phủ cache hit/miss, cache rỗng, User/Role/global invalidation, expiration, snapshot copy, 20 request cùng User chỉ một loader, User khác load song song, cancellation waiter, loader exception và race Role invalidation. PermissionService tests kiểm tra thay đổi RolePermission, UserPermission, User active, Role active được phản ánh ở lần gọi kế tiếp.

Explicit relational transaction commit/rollback callback chưa có integration test dùng relational database.

## 7. Trạng thái triển khai

- Đã triển khai memory cache, TTL cấu hình được, per-User single-flight, generation checks và invalidation sau SaveChanges/transaction commit.
- Đã nối invalidation cho User, UserPermission, Role, RolePermission và Permission changes.
- Đã thêm metrics và unit tests cho cache, concurrent load, invalidation/race, cancellation/error và làm mới effective permissions.
- Build solution và toàn bộ unit tests đã chạy thành công: **119 passed, 0 failed**.
- Chưa có relational integration test trực tiếp cho explicit transaction commit/rollback callbacks.
- Chưa triển khai distributed invalidation, outbox hoặc Redis; cần hoàn thành trước khi đòi hỏi consistency tức thì ở nhiều API instance.

## 8. Tiêu chí hoàn thành

- Cache key chứa UserId và User generation; cache entry kiểm tra global/Role generation; value chỉ là permission codes hiệu lực.
- Cache hit không thực hiện truy vấn database.
- TTL tuyệt đối được áp dụng và có thể cấu hình/test.
- Cache miss đồng thời cùng User được coalesce thành một lần loader (hai truy vấn database trong loader hiện tại).
- Tất cả đường mutation permission invalidate đối tượng/nhóm User bị ảnh hưởng sau commit.
- Không cache kết quả sai khi generation đổi trong lúc query hoặc khi DB lỗi.
- Permission thay đổi có hiệu lực ở request kế tiếp trên instance sau khi mutation commit; giới hạn cross-instance được tài liệu hóa.
- Unit tests bao phủ expiration, scoped invalidation, concurrency, cancellation/error và race.
- Handler không chứa logic cache.
