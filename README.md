# DigitalSigningImproved

Hệ thống ký số từ xa (Remote Digital Signing) dành cho ngành giáo dục Việt Nam.
Hỗ trợ các CA provider: VNPT, Viettel, BKAV, MISA, GCC.

## Kiến trúc tổng quan

```
Client → API (ASP.NET Core 9.0) → RabbitMQ → Workers (x7) → MongoDB / Redis
```

### Components

| Component | Mô tả |
|-----------|-------|
| **DigitalSigning.Api** | REST API – nhận request ký, kiểm tra idempotency, rate‑limiting |
| **DigitalSigning.Core** | Business logic, models, providers, message queue, rate‑limiters |
| **Workers (7)** | FilePrepare → Hash → Provider → Waiting → Append → Upload → Webhook |

### Worker Pipeline

```
FilePrepare → Hash → Provider → [Waiting] → Append → Upload → Webhook
```

Mỗi worker lắng nghe một queue riêng trên RabbitMQ, xử lý message và publish message tiếp theo.

---

## Idempotency

Hệ thống hỗ trợ idempotency qua `IdempotencyKey` trong request.

### V1 (legacy fallback)
- Khi `FeatureFlags__IdempotencyV2 = false`
- Chỉ log duplicate key, vẫn tạo transaction mới

### V2 (mặc định)
- Khi `FeatureFlags__IdempotencyV2 = true`
- Nếu key đã tồn tại, trả về transaction cũ (200 OK)
- Xử lý race condition (double‑check pattern)
- Key TTL: 24h (MongoDB TTL index)

**Flow:**
1. Client gửi request với `IdempotencyKey`
2. Service lookup key trong MongoDB
3. Nếu tồn tại → trả về transaction cũ
4. Nếu chưa tồn tại → lưu key + tạo transaction mới
5. Nếu race condition (Duplicate khi lưu) → lookup lại transaction thắng

---

## Rate‑Limiting

| Level | Capacity | Refill rate | Config key |
|-------|----------|-------------|------------|
| **Per provider** | 200 req | 100 req/s | `RateLimiter:ProviderCapacity` / `ProviderRefillRate` |
| **Per tenant** | 50 req | 30 req/s | `RateLimiter:TenantCapacity` / `TenantRefillRate` |

Khi vượt quá giới hạn, API trả về **429 Too Many Requests**.

Rate‑limiter sử dụng **Token Bucket** algorithm (`TokenBucketRateLimiter`).

---

## Error Handling

| HTTP Status | Ý nghĩa | Khi nào |
|-------------|---------|---------|
| 200 | Thành công | Transaction được tạo / trả về |
| 400 | Validation error | Dữ liệu không hợp lệ |
| 409 | Conflict | Business rule violation |
| 429 | Rate limited | Vượt quá quota request |
| 499 | Client cancelled | Client ngắt kết nối |
| 500 | Server error | Lỗi không xác định |
| 503 | Service unavailable | Downstream service timeout |

Các exception được phân loại:
- `OperationCanceledException` → 499
- `TimeoutException` → 503
- `InvalidOperationException` → 409
- `Exception` → 500 (chi tiết chỉ hiển thị trong Development)

Global exception middleware (`GlobalExceptionMiddleware`) bắt tất cả unhandled exception và trả JSON.

---

## Request Size Limits

| Endpoint | Giới hạn |
|----------|----------|
| `POST /Signature/CreateTransaction` | 512 KB |
| `POST /Signature/GetTransaction` | 10 KB |
| `FileUrls` | Tối đa 10 URL, mỗi URL ≤ 2.048 ký tự |

---

## Feature Flags

Cấu hình trong `appsettings.json`:

```json
{
  "FeatureFlags": {
    "IdempotencyV2": true
  }
}
```

Có thể override bằng environment variable: `FeatureFlags__IdempotencyV2=false`

---

## Development

### Prerequisites
- .NET 9.0 SDK
- Docker Desktop (cho MongoDB, Redis, RabbitMQ)

### Quick start
```bash
# Khởi động infrastructure
docker compose up -d mongodb redis rabbitmq

# Build và run API
cd DigitalSigning.Api
dotnet run

# Run tests
dotnet test
```

### Run full stack
```bash
docker compose up -d
```

### Run tests
```bash
# Unit tests
dotnet test DigitalSigning.Api.Tests

# All tests
dotnet test DigitalSigningImproved.sln
```

---

## CI/CD Pipeline (GitHub Actions)

| Stage | Trigger | Mô tả |
|-------|---------|-------|
| Build & Test | push/PR main | `dotnet build` + `dotnet test` |
| Docker Build | push main/develop | Build 8 images (api + 7 workers) |
| Deploy Staging | push develop | SSH + docker compose up |
| Deploy Production | push main | SSH + docker compose up (scale api=2) |

---

## Package Vulnerability Warnings

Hiện tại có 4 package warnings:
- `OpenTelemetry.*` 1.9.0 (moderate)
- `System.Security.Cryptography.Xml` 9.0.0 (high)

Cập nhật lên phiên bản mới nhất khi có bản vá bảo mật.
