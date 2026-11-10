# Kế hoạch quản lý Exception tập trung và logging lên Seq

## 1. Mục tiêu

- Chuẩn hóa mọi lỗi API theo một response template thống nhất.
- Không trả exception gốc, `Exception.Message`, inner exception, stack trace hay chi tiết hạ tầng cho client.
- Ghi log có cấu trúc vào Seq; không format log bằng string interpolation hoặc nối chuỗi.
- Ghi nhận thông tin cần điều tra request/response nhưng phải loại bỏ credential, token, mật khẩu và dữ liệu nhạy cảm.
- Không thay đổi business logic hoặc response thành công ngoài phạm vi yêu cầu.

**Phạm vi:** ASP.NET Core API hiện tại, target `net7.0`; không nâng framework trong kế hoạch này.

**Trạng thái:** Đã triển khai in-process exception handling, structured request/response logging và Seq async sink. API đã smoke test với Seq endpoint unreachable; chưa xác nhận ingest vào Seq đang chạy.

## 2. Hiện trạng và implementation

- API dùng Controllers; `ApiExceptionMiddleware` tập trung exception handling và phát một structured event cho mỗi request.
- Response lỗi là Problem Details với message cố định, path và `traceId`; model-state validation cũng trả format an toàn thống nhất.
- `Serilog.AspNetCore`, `Serilog.Sinks.Async` và `Serilog.Sinks.Seq` đã được cấu hình; endpoint Seq là `Seq:ServerUrl`, API key đọc từ `Seq:ApiKey` (có thể cấu hình qua environment).
- Tests bao phủ mapping, không lộ exception, structured properties, redaction, request-body preservation, giới hạn body và cancellation.

## 3. Thiết kế đã triển khai

### 3.1 Response exception template

Trả JSON theo RFC 7807 Problem Details cho tất cả lỗi do exception handler xử lý. Template thống nhất dự kiến:

```json
{
  "type": "about:blank",
  "title": "Request failed",
  "status": 500,
  "detail": "An unexpected error occurred.",
  "instance": "/api/resource",
  "extensions": { "traceId": "..." }
}
```

- Chỉ trả thông tin an toàn: status, mã/title và thông báo cố định theo nhóm lỗi, path và `traceId`.
- Không serialize exception, exception message, inner exception, stack trace, SQL/provider message hoặc object lỗi nội bộ.
- Lỗi model validation trả detail chung; không đưa field values hoặc lỗi chi tiết vào response.
- Giữ chi tiết kỹ thuật trong log nội bộ có quyền truy cập hạn chế, không đưa vào response.

### 3.2 Mapping exception sang HTTP

Thay mapping rải rác bằng một bảng mapping trung tâm, ưu tiên exception nghiệp vụ có kiểu cụ thể thay vì suy luận từ loại exception .NET tổng quát:

| Nhóm lỗi | HTTP | Nội dung client |
|---|---:|---|
| Validation/input không hợp lệ | 400 | Thông báo cố định; field errors đã lọc nếu có |
| Chưa xác thực | 401 | Thông báo cố định |
| Không đủ quyền | 403 | Thông báo cố định |
| Không tìm thấy tài nguyên | 404 | Thông báo cố định |
| Trùng dữ liệu/xung đột trạng thái | 409 | Thông báo cố định |
| Lỗi không dự kiến | 500 | `An unexpected error occurred.` |

- Mapping hiện có: `ArgumentException` 400, `UnauthorizedAccessException` 401, `SecurityException` 403, `KeyNotFoundException` 404, `ConflictException`/SQL unique conflict 409; exception khác trả 500.
- Lỗi đăng ký trùng username/email và role inactive trong `AuthService` đã chuyển sang `ConflictException`.
- Không map mọi `InvalidOperationException`/`ArgumentException` một cách rộng rãi nếu có thể che giấu lỗi lập trình; chỉ map loại được xác định là lỗi client.
- Hủy request do `RequestAborted` không tạo response lỗi giả và không ghi thành lỗi server.
- Nếu response đã bắt đầu, không cố ghi Problem Details lần nữa; để pipeline xử lý theo quy tắc ASP.NET Core.

### 3.3 Logging có cấu trúc và Seq

- Dùng Serilog làm logging provider và Seq sink tương thích với .NET 7; cấu hình endpoint Seq qua configuration/env var.
- Không commit Seq API key hoặc credential; cung cấp endpoint mặc định cho môi trường phát triển và lấy key từ secret store/environment khi triển khai.
- Seq logging chạy qua async sink, queue giới hạn mặc định 10.000 events (`Seq:BufferSize`) và `blockWhenFull: false`; Seq sink nhận batch 100 events mỗi 2 giây.
- Queue đầy không block request; async sink có thể drop events khi đầy và chẩn đoán qua Serilog `SelfLog` ra stderr. Chưa có dropped-event metric hoặc durable disk buffer.
- `Log.CloseAndFlush()` chạy khi ứng dụng dừng. Không có fallback sink nào gọi ngược Seq.
- Smoke test với `Seq:ServerUrl=http://127.0.0.1:1` xác nhận API trả HTTP 200 trước và sau khi sink thử kết nối. Chưa xác nhận khả năng nhận event khi Seq hoạt động.
- Dùng message template cố định và named properties, ví dụ:
  `logger.LogError(exception, "Unhandled API exception {TraceId} {Endpoint} {StatusCode}", traceId, endpoint, statusCode)`.
- Không dùng `$"..."`, `string.Format`, hoặc nối chuỗi để tạo message log. Không dùng exception message làm template.
- Mỗi request hoàn tất phát một structured event; exception middleware không ghi thêm event lỗi thứ hai.
- Trace ID được thêm vào `X-Correlation-ID`, Problem Details extension và log; event có RequestId, CorrelationId, endpoint, path, method, user ID, status, content type, elapsed time và body capture state.
- Ghi các thuộc tính:
  - `TraceId`/`RequestId`, `CorrelationId`.
  - HTTP method, route endpoint (route template/display name), path đã lọc và query đã lọc.
  - User ID đã xác thực hoặc giá trị `anonymous`; không ghi username/email nếu không cần thiết.
  - Status code, elapsed milliseconds, content type.
  - Request/response body đã lọc hoặc lý do body không được capture.
  - Exception type và exception/stack trace chỉ trong log nội bộ cho lỗi server.
- Phân mức log theo status: lỗi 5xx là Error; lỗi client đã dự kiến là Warning/Information theo mapping; request hủy không log như lỗi ứng dụng.

### 3.4 Ghi request/response an toàn

Request/response body được capture có giới hạn và redaction:

- Mask các property chứa password/token/secret/credential, email, username, phone, address và một số định danh/thông tin cá nhân thường gặp.
- Không ghi header `Authorization`, `Cookie`, `Set-Cookie` hoặc secret header.
- Chỉ capture JSON UTF-8 có Content-Length xác định, tối đa 16 KiB cho mỗi chiều; skip body không hỗ trợ, không rõ độ dài, quá giới hạn hoặc JSON không hợp lệ.
- Giữ nguyên request stream/response body để không thay đổi hành vi API; có giới hạn bộ nhớ và xử lý cancellation.
- Nếu response vượt giới hạn capture, bỏ toàn bộ response body khỏi log nhưng vẫn forward đầy đủ response tới client.
- Query string và header values không được ghi; body field chưa nhận diện vẫn có thể chứa PII, nên cần rà soát danh sách redact theo schema thật trước khi bật logging ở production.

Redaction hiện dựa trên tên property; đây không phải allowlist tuyệt đối và cần security review trước khi bật body logging ở production.

## 4. Các file đã thay đổi

- `src/DynamicPermission.API/Middleware/ApiExceptionMiddleware.cs`: mapping, Problem Details, correlation, body capture/redaction và structured request event.
- `src/DynamicPermission.API/Program.cs`: Serilog/Seq wiring, pipeline ordering và safe model validation response.
- `src/DynamicPermission.API/DynamicPermission.API.csproj`, `src/DynamicPermission.API/appsettings.json`: package/config Seq.
- `src/DynamicPermission.Application/Services/AuthService.cs`: dùng ConflictException cho xung đột đăng ký đã biết.
- `tests/DynamicPermission.UnitTests/API/Middleware/ApiExceptionMiddlewareTests.cs`: response, redaction, properties, body integrity, size limit và cancellation.
- `tests/DynamicPermission.UnitTests/Application/Services/AuthServiceTests.cs`: cập nhật test cho ConflictException.

## 5. Xác minh và việc còn lại

- Unit tests: **125 passed, 0 failed**.
- Smoke test khi Seq endpoint unreachable: `/swagger/v1/swagger.json` trả HTTP 200 liên tiếp.
- Cần triển khai Seq reachable để xác nhận ingest, cấu hình API key qua secret/environment, và rà soát redaction theo DTO/schema thực tế trước production.
- Chưa có dropped-event metric, durable queue hoặc kiểm thử queue saturation/shutdown khi Seq mất kết nối dài hạn.

## 6. Tiêu chí hoàn thành

- Các exception do middleware xử lý và model validation trả Problem Details theo template an toàn.
- Response không chứa exception gốc, message kỹ thuật, inner exception, stack trace hay SQL/database detail.
- Exception đã biết có status code phù hợp; lỗi không biết trả 500 với nội dung chung.
- Structured events gửi tới Seq có TraceId/RequestId, endpoint, user/anonymous, status, request/response capture state và elapsed time.
- Mọi message log dùng message template cố định; dữ liệu nằm trong named properties, không có string interpolation/nối chuỗi.
- Không có password, bearer/refresh token, cookie, secret hoặc raw sensitive payload trong log.
- Logging không làm thay đổi request/response body, không làm mất dữ liệu stream và có giới hạn kích thước.
- Lỗi request không bị ghi trùng; request cancellation không bị hiểu nhầm là lỗi server.
- API đã được smoke test không đổi response khi Seq unreachable; chưa smoke test event ingest tới Seq thật.
- Build và unit tests đều đạt.

## 7. Rủi ro và lưu ý

- Queue hiện bounded và non-blocking; khi đầy, log có thể bị drop và chỉ có SelfLog diagnostic, chưa có dropped metric hoặc durable buffer.
- Lưu request/response có thể chứa dữ liệu nhạy cảm. Cần mặc định redaction/skip, giới hạn retention và quyền truy cập Seq.
- Project target .NET 7; chọn phiên bản package tương thích hiện có và cân nhắc nâng .NET như một công việc riêng, không gộp vào thay đổi này.
- Exception chi tiết vẫn có thể cần trong log server-side để điều tra; tuyệt đối không trả cho client và phải hạn chế quyền truy cập log.
