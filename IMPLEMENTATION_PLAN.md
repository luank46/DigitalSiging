# Kế hoạch triển khai hệ thống ký số cải tiến

## Tổng quan
Dự án xây dựng hệ thống ký số mới dựa trên kiến trúc micro-service, xử lý sự kiện qua RabbitMQ, với mục tiêu chịu tải 5.000 giao dịch đồng thời.

## Các giai đoạn triển khai

### Giai đoạn 0: Thiết lập dự án (Hoàn thành)
- Tạo solution mới: `DigitalSigningImproved.sln`
- Tạo các project:
  - `DigitalSigning.Core` (Class Library, net9.0)
  - `DigitalSigning.Api` (ASP.NET Core Web API, net9.0)
  - 7 Worker projects:
    - `DigitalSigning.Workers.FilePrepare`
    - `DigitalSigning.Workers.Hash`
    - `DigitalSigning.Workers.Provider`
    - `DigitalSigning.Workers.Waiting`
    - `DigitalSigning.Workers.Append`
    - `DigitalSigning.Workers.Upload`
    - `DigitalSigning.Workers.Webhook`
- Thêm tham chiếu từ tất cả project tới `DigitalSigning.Core`

### Giai đoạn 1: Xây dựng lõiCore (Models, Services, Repositories)
**Mục tiêu:** Xây dựng các thành phần cơ bản bao gồm:
- Domain models (Transaction, TransactionEvent, WebhookDelivery, IdempotencyKey)
- Enum định nghĩa (TransactionStatus, TransactionStep, ErrorCode, ProviderType)
- Repository interfaces và implementations cho MongoDB
- Service interfaces và implementations cho business logic
- Idempotency service với unique index trên MongoDB
- Dead Letter Queue (DLQ) service
- Basic metrics collection

**Files to create in DigitalSigning.Core:**
```
Core/
├── Models/
│   ├── Transaction.cs
│   ├── TransactionEvent.cs
│   ├── WebhookDelivery.cs
│   ├── IdempotencyKey.cs
│   ├── SignRequestReference.cs
│   └── FileMetadata.cs
├── Enums/
│   ├── TransactionStatus.cs
│   ├── TransactionStep.cs
│   ├── ErrorCode.cs
│   └── ProviderType.cs
├── Repositories/
│   ├── IMongoRepository.cs
│   ├── TransactionRepository.cs
│   ├── TransactionEventRepository.cs
│   ├── WebhookDeliveryRepository.cs
│   ├── IdempotencyKeyRepository.cs
│   └── SignRequestReferenceRepository.cs
├── Services/
│   ├── ITransactionService.cs
│   ├── TransactionService.cs
│   ├── IIdempotencyService.cs
│   ├── IdempotencyService.cs
│   ├── IDLQService.cs
│   └── DLQService.cs
├── Exceptions/
│   └── DomainException.cs
└── Settings/
    ├── MongoDbSettings.cs
    ├── RedisSettings.cs
    ├── RabbitMqSettings.cs
    └── AppSettings.cs
```

### Giai đoạn 2: Thiết kế Message Contracts và RabbitMQ Topology
**Mục tiêu:** Thiết kế các message chứa chỉ metadata, không chứa file bytes lớn, thiết kế topology RabbitMQ với các queue riêng biệt.

**Files to create in DigitalSigning.Core:**
```
Messaging/
├── Contracts/
│   ├── BaseMessage.cs
│   ├── FilePrepareMessage.cs
│   ├── HashMessage.cs
│   ├── ProviderMessage.cs
│   ├── WaitingMessage.cs
│   ├── AppendMessage.cs
│   ├── UploadMessage.cs
│   └── WebhookMessage.cs
├── Enums/
│   └── MessageStep.cs
├── Topology/
│   ├── IRabbitMqTopologyInitializer.cs
│   └── RabbitMqTopologyInitializer.cs
├── Publishers/
│   ├── IMessagePublisher.cs
│   └── MessagePublisher.cs
└── Consumers/
    ├── IMessageConsumer.cs
    └── MessageConsumer.cs
```

### Giai đoạn 3: Triển khai Waiting Scheduler với Redis
**Mục tiêu:** Xây dựng hệ thống quản lý trạng thái chờ người dùng xác thực bằng Redis Sorted Set.

**Files to create in DigitalSigning.Core:**
```
Waiting/
├── Interfaces/
│   ├── IWaitingScheduler.cs
│   └── IWaitingLockService.cs
├── Services/
│   ├── WaitingScheduler.cs
│   └── WaitingLockService.cs
├── Models/
│   └── WaitingItem.cs
└── Config/
    └── WaitingOptions.cs
```

### Giai đoạn 4: Triển khai Workers
**Mục tiêu:** Xây dựng 7 workers xử lý các bước khác nhau trong quy trình ký số.

Mỗi worker sẽ có cấu trúc cơ bản:
- Kết nối tới MongoDB, Redis, RabbitMQ
- Lắng nghe queue cụ thể
- Xử lý message tương ứng với bước làm việc
- Cập nhật trạng thái giao dịch
- Publish message tới queue tiếp theo (nếu có)
- Xử lý lỗi và retry

**Files to create (mẫu cho mỗi worker):**
```
DigitalSigning.Workers.{WorkerName}/
├── Workers/
│   └── {WorkerName}Worker.cs
├── Services/
│   ├── I{WorkerName}Service.cs
│   └── {WorkerName}Service.cs
├── Config/
│   └── {WorkerName}Options.cs
└── Program.cs
```

### Giai đoạn 5: Triển khai Rate Limiting và Circuit Breaker
**Mục tiêu:** Bảo vệ hệ thống khỏi quá tải và ngăn cascade failure.

**Files to create in DigitalSigning.Core:**
```
Protection/
├── RateLimiting/
│   ├── IRateLimitPolicy.cs
│   ├── FixedWindowRateLimitPolicy.cs
│   ├── TokenBucketRateLimitPolicy.cs
│   └── RateLimitService.cs
├── CircuitBreaker/
│   ├── ICircuitBreakerPolicy.cs
│   ├── StandardCircuitBreakerPolicy.cs
│   └── CircuitBreakerService.cs
└── Policies/
    ├── ProviderRateLimitPolicy.cs
    └── TenantRateLimitPolicy.cs
```

### Giai đoạn 6: Triển khai Observability
**Mục tiêu:** Giám sát, tracing, logging và metrics toàn diện.

**Files to create in DigitalSigning.Core:**
```
Observability/
├── Tracing/
│   ├── ActivitySourceExtensions.cs
│   └── DiagnosticListener.cs
├── Metrics/
│   ├── PrometheusMetrics.cs
│   └── MetricsCollector.cs
├── Logging/
│   ├── SerilogExtensions.cs
│   └── LogEnrichers.cs
└── HealthChecks/
    ├── MongoDbHealthCheck.cs
    ├── RabbitMqHealthCheck.cs
    └── RedisHealthCheck.cs
```

### Giai đoạn 7: Mở rộng API với các endpoint mới
**Mục tiêu:** Cung cấp API để quản lý và監控 giao dịch.

**Files to create in DigitalSigning.Api:**
```
Controllers/
├── TransactionController.cs
├── EventController.cs
├── WebhookController.cs
├── HealthController.cs
└── MetricsController.cs
Models/
├── DTOs/
│   ├── CreateTransactionRequest.cs
│   ├── TransactionResponse.cs
│   ├── TransactionEventResponse.cs
│   └── WebhookDeliveryResponse.cs
└── Validators/
    └── CreateTransactionRequestValidator.cs
```

### Giai đoạn 8: Bảo mật
**Mục tiêu:** Bảo vệ dữ liệu nhạy cảm và đảm bảo an toàn thông tin.

**Files to create in DigitalSigning.Core:**
```
Security/
├── Encryption/
│   ├── IEncryptionService.cs
│   └── AesEncryptionService.cs
├── Secrets/
│   ├── ISecretProvider.cs
│   ├── EnvironmentSecretProvider.cs
│   └── VaultSecretProvider.cs
├── Webhook/
│   ├── IWebhookSigner.cs
│   └── HmacWebhookSigner.cs
└── Middleware/
    ├── SecurityHeadersMiddleware.cs
    └── RequestSigningMiddleware.cs
```

### Giai đoạn 9: Trừu tượng hóa File Storage
**Mục tiêu:** Hỗ trợ nhiều loại storage với khả năng fallback.

**Files to create in DigitalSigning.Core:**
```
Storage/
├── Interfaces/
│   ├── IFileStorage.cs
│   ├── IFileDownloader.cs
│   ├── IFileHashService.cs
│   └── ISignedFileUploader.cs
├── Implementations/
│   ├── LocalFileStorage.cs
│   ├── MinIoFileStorage.cs
│   ├── GridFsFallbackStorage.cs
│   └── CompositeFileStorage.cs
├── Services/
│   ├── FileHashService.cs
│   └── FileValidationService.cs
└── Config/
    └── StorageOptions.cs
```

### Giai đoạn 10: Tối ưu hóa và Kế hoạch Năng lực
**Mục tiêu:** Đảm bảo hệ thống có thể mở rộng ngang và chịu tải mục tiêu.

**Hoạt động:**
- Cấu hình MongoDB Replica Set/Sharding
- Cấu hình RabbitMQ Cluster
- Cấu hình Redis Cluster
- Thiết kế script load test với k6
- Tài liệu capacité planning

### Giai đoạn 11: Chiến lược Di chuyển từ Hệ thống Cũ
**Mục tiêu:** Di chuyển an toàn từ hệ thống hiện tại sang hệ thống mới.

**Các bước:**
1. Chạy song song hai hệ thống
2. Di chuyển dữ liệu lịch sử
3. Cutover bằng feature flag
4. Giám sát và rollback nếu cần

### Giai đoạn 12: Kiểm tra Sẵn sàng sản xuất
**Mục tiêu:** Đảm bảo hệ thống đáp ứng các yêu cầu produzione.

**Checklist:**
- [ ] Logging đầy đủ với cấu trúc
- [ ] Metrics được выставка lên Prometheus
- [ ] Health checks cho tất cả service
- [ ] Rate limiting và circuit breaker hoạt động
- [ ] Idempotency được kiểm tra
- [ ] DLQ có thể replay được
- [ ] Bảo mật: mã hóa, không log thông tin nhạy cảm
- [ ] Dockerfile và docker-compose.yml cho phát triển
- [ ] Helm chart cho Kubernetes
- [ ] Tài liệu triển khai và vận hành

## Kế hoạch Công việc Ngắn hạn (Tiếp theo)

### Tuần 1: Giai đoạn 1 và 2
- Hoàn thành Core models, repositories, services
- Thiết kế message contracts và RabbitMQ topology
- Xây dựng basic publisher/consumer infrastructure

### Tuần 2: Giai đoạn 3 và 4
- Triển khai Waiting scheduler với Redis
- Bắt đầu triển khai FilePrepare worker và Hash worker

### Tuần 3: Giai đoạn 4 (tiếp theo) và 5
- Hoàn thành các workers còn lại
- Triển khai rate limiting và circuit breaker

### Tuần 4: Giai đoạn 6 và 7
- Triển khai observability
- Mở rộng API với các endpoint mới

### Tuần 5: Giai đoạn 8, 9 và 10
- Bảo mật
- Trừu tượng hóa file storage
- Tối ưu hóa và kế hoạch năng lực

## Định nghĩa Hoàn thành (DoD) cho mỗi Giai đoạn
- Mã nguồn được viết theo chuẩn coding style của 팀
- Đã viết unit tests cho ít nhất 80% mã nguồn mới
- Đã viết integration tests cho các luồng chính
- Mã nguồn đã được review bởi ít nhất một thành viên khác
- Đã cập nhật tài liệu thiết kế nếu cần
- Mã nguồn đã được build thành công và pass tất cả tests locally

## Tài liệu Tham khảo
- Hệ thống hiện tại: tham khảo tại `D:/AI_WORKSPACE/CLAUDE/KY_SO_NEW/CHU_KY_DIEN_TU_REMOTE_SIGNING`
- Kiến trúc đề xuất: xem file `docs/plan.md` trong thư mục hiện tại