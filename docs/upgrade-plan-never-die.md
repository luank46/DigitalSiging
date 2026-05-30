# Kế hoạch nâng cấp "Never Die"

## 3 giai đoạn: Cứu mạng → Tự phục hồi → Không chết

---

## GIAI ĐOẠN 1 — CỨU MẠNG (1-2 tuần)

### 1.1. Circuit breaker thực tế cho Provider HTTP calls
**Hiện tại:** `CircuitBreaker` đã được DI-register nhưng không được inject vào Provider services.

```csharp
// VnptProviderService.GetTokenAsync()
var response = await _httpClient.PostAsync(url, content, ct);
response.EnsureSuccessStatusCode(); // ← Nếu VNPT sập, vẫn gọi tiếp
```

**Fix:** `ProtectedProviderDecorator` đã có — cần verify ProviderFactory dùng decorator:
- ✅ Architecture OK (đã implement)
- Cần test: nếu provider trả 503 liên tục, circuit mở trong 30s

### 1.2. MessageConsumer Channel recovery trên connection loss
**Hiện tại:** `AutomaticRecoveryEnabled=true` recovery connection, nhưng channel cũ stale.

**Fix (đã làm):** Tạo channel mới mỗi `StartConsumingAsync`.
**Còn thiếu:** Cần reconnect loop khi `StartConsumingAsync` throw do connection loss:

```csharp
// BackgroundWorker.ExecuteAsync cần:
while (!stoppingToken.IsCancellationRequested)
{
    try
    {
        await Consumer.StartConsumingAsync(handler, stoppingToken);
    }
    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
    {
        Logger.LogError(ex, "Connection lost, retrying in 5s...");
        await Task.Delay(5_000, stoppingToken);
    }
}
```

### 1.3. MongoDB connection pool monitoring
**Hiện tại:** `MaxPoolSize=500` hardcoded, không monitor pool exhaustion.

**Cần:** Thêm health check kiểm tra `serverStatus.connections`:
```csharp
// MongoDbHealthCheck hiện tại chỉ ping, cần thêm:
var serverStatus = await database.RunCommandAsync<BsonDocument>("{ serverStatus: 1 }");
var connections = serverStatus["connections"];
var used = connections["current"].AsInt32;
var available = connections["available"].AsInt32;
```

### 1.4. RabbitMQ connection heartbeat + cluster support
**Hiện tại:** `ConnectionFactory` không set heartbeat, không hỗ trợ cluster.

```csharp
// RabbitMqConnection.cs cần:
factory.RequestedHeartbeat = TimeSpan.FromSeconds(60);
factory.AutomaticRecoveryEnabled = true;
factory.TopologyRecoveryEnabled = true;

// Hỗ trợ cluster connection string: "host1,host2,host3"
if (settings.Host.Contains(','))
    factory.Uri = new Uri($"amqp://{settings.UserName}:{settings.Password}@{settings.Host}/");
```

---

## GIAI ĐOẠN 2 — TỰ PHỤC HỒI (2-4 tuần)

### 2.1. Dead Letter Queue Replay dashboard
**Hiện tại:** DLQ message được ghi vào MongoDB `dlqMessages` collection, không có tool để replay.

**Cần:**
- API endpoint `POST /admin/dlq/{id}/replay` — publish lại message vào exchange
- API endpoint `GET /admin/dlq` — list messages với filter theo step, error code
- Background job auto-replay DLQ messages sau 5 phút (max 3 lần)

```csharp
// DlqReplayService.cs (HostedService)
protected override async Task ExecuteAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        var messages = await _dlqRepo.GetUnreplayedAsync(batchSize: 50);
        foreach (var msg in messages)
        {
            var queueMsg = JsonSerializer.Deserialize<QueueMessage>(msg.Payload);
            await _publisher.PublishAsync(queueMsg);
            msg.Replayed = true;
            msg.ReplayedAt = DateTime.UtcNow;
            await _dlqRepo.UpdateAsync(msg);
        }
        await Task.Delay(TimeSpan.FromMinutes(5), ct);
    }
}
```

### 2.2. Graceful Shutdown cho Workers
**Hiện tại:** Không có graceful shutdown — worker bị kill giữa chừng, transaction pending mất.

**Cần:** Implement `StopAsync` trong BackgroundWorker:

```csharp
protected override async Task StopAsync(CancellationToken ct)
{
    Logger.LogWarning("Shutdown requested — draining pending messages...");
    
    // Signal cancellation
    await base.StopAsync(ct);
    
    // Wait for in-flight messages to complete
    int retries = 0;
    while (MetricsCollector.CurrentInFlight > 0 && retries < 30)
    {
        await Task.Delay(1000, ct);
        retries++;
    }
    
    Logger.LogInformation("Drain complete. Shutting down.");
}
```

### 2.3. Transaction recovery scheduler
**Hiện tại:** Transaction treo nếu worker crash giữa chừng — không recovery.

**Cần:** `TransactionRecoveryService` (HostedService) chạy mỗi 5 phút:

```csharp
// Tìm transaction ở trạng thái "đang xử lý" quá 10 phút
var hanging = await _txRepo.FindAsync(t =>
    t.CurrentStatus == TransactionStatus.PreparingFile ||
    t.CurrentStatus == TransactionStatus.Hashing ||
    t.CurrentStatus == TransactionStatus.ProviderAuthorizing ||
    (t.CurrentStatus == TransactionStatus.WaitingUserConfirm &&
     t.WaitingExpireAt < DateTime.UtcNow));

foreach (var tx in hanging)
{
    Logger.LogWarning("Recovering hanging transaction {Tx}", tx.MaGiaoDich);
    await _publisher.PublishAsync(new QueueMessage
    {
        MaGiaoDich = tx.MaGiaoDich,
        Step = tx.CurrentStep,
        Provider = tx.ProviderType,
        Attempt = tx.RetryCount + 1,
    });
}
```

### 2.4. Rate limiter per-provider + dynamic adjustment
**Hiện tại:** Rate limiter capacity fixed (200/s), không auto-adjust theo provider health.

**Cần:**
- Theo dõi provider error rate trong sliding window 5 phút
- Nếu error rate > 20% → giảm rate limit xuống 50%
- Nếu error rate < 5% trong 10 phút → tăng rate limit dần

---

## GIAI ĐOẠN 3 — KHÔNG CHẾT (4-8 tuần)

### 3.1. Saga pattern cho transaction pipeline
**Hiện tại:** Mỗi step publish message độc lập — nếu step 3 (Provider) fail, step 1+2 đã chạy xong, không có rollback.

**Cần:** Saga orchestration:
```csharp
public class SigningSagaOrchestrator
{
    // TransactionLog: ghi mỗi step bắt đầu và kết thúc
    // Nếu step N fail → publish CompensatingMessages cho step N-1, N-2...
    
    // Compensating actions:
    // FilePrepare → delete uploaded file từ GridFS
    // Hash → no-op (hash không side-effect)
    // Provider → no-op (provider tự timeout)
    // Append → delete signed file từ GridFS
    // Upload → call external API delete
    // Webhook → call external API rollback
}
```

### 3.2. Idempotency implementation
**Hiện tại:** `IIdempotencyService` được DI-register, inject vào BackgroundWorker, nhưng không bao giờ gọi — code comment "ack semantics handles idempotency".

**Sai lầm:** RabbitMQ ack chỉ đảm bảo at-least-once delivery. Worker crash sau khi xử lý nhưng trước khi ack → message được redeliver → duplicate processing.

**Fix:**
```csharp
// Trong BackgroundWorker.ProcessCoreAsync (hoặc handler)
var idempotentKey = $"{message.MaGiaoDich}:{Step}";
var result = await Idempotency.CheckAndStoreAsync(
    message.TenantId, idempotentKey, message.MaGiaoDich);
if (result == IdempotencyResult.Duplicate)
{
    Logger.LogWarning("Skipping duplicate message {Key}", idempotentKey);
    return; // → ack, không xử lý lại
}
```

### 3.3. Bulkhead pattern cho Provider calls
**Hiện tại:** `IBulkheadPolicy<HttpResponseMessage>` được DI-register (max 10) nhưng không được dùng.

**Cần:**
```csharp
// Trước mỗi Provider HTTP call:
if (!_bulkhead.TryAcquire())
{
    // Queue hoặc reject
    return ProviderResult { ErrorMessage = "System busy, retry later" };
}
```

### 3.4. Distributed tracing end-to-end
**Hiện tại:** OpenTelemetry chỉ export ra Console. Không có tracing backend.

**Cần:**
- Deploy OpenTelemetry Collector (Docker)
- Export traces + metrics lên Jaeger hoặc Grafana Tempo
- Export logs lên Elasticsearch (Serilog đã config sẵn)

### 3.5. Zone isolation (multi-region)
**Hiện tại:** 1 RabbitMQ cluster, 1 MongoDB replica set.

**Cho tương lai:**
- Mỗi provider có worker pool riêng (VNPT worker, Viettel worker...)
- Nếu VNPT API sập, ảnh hưởng tối đa 30% transaction
- Transaction routing: round-robin hoặc priority queue

---

## BẢNG ƯU TIÊN

| # | Task | Phase | Effort | Tác động |
|---|---|---|---|---|
| 1 | Reconnect loop BackgroundWorker | 1 | 1h | 🔴 Ngăn worker chết vĩnh viễn |
| 2 | DLQ auto-replay | 2 | 4h | 🟡 Tự phục hồi message lỗi |
| 3 | Graceful shutdown workers | 2 | 2h | 🔴 Không mất transaction khi deploy |
| 4 | Transaction recovery scheduler | 2 | 4h | 🔴 Cứu transaction treo |
| 5 | Idempotency triển khai thật | 3 | 4h | 🔴 Chống duplicate |
| 6 | Saga rollback | 3 | 2w | 🟡 Rollback an toàn |
| 7 | RabbitMQ cluster support | 1 | 2h | 🟡 HA cho queue |
| 8 | MongoDB monitor pool | 1 | 1h | 🟡 Phát hiện sớm exhaustion |
| 9 | Bulkhead thực tế | 3 | 2h | 🟡 Chống cascade failure |
| 10 | OTEL Collector + tracing | 3 | 1w | 🟢 Debug distributed |

---

## ROADMAP TRIỂN KHAI

```
Tuần 1-2 (Cứu mạng)
├── P0: Reconnect loop + DLQ auto-replay
├── P0: Graceful shutdown
├── P1: RabbitMQ cluster + heartbeat
└── P1: MongoDB pool monitor

Tuần 3-4 (Tự phục hồi)
├── P0: Transaction recovery scheduler
├── P1: DLQ dashboard + manual replay
├── P2: Rate limiter dynamic adjustment
└── P2: Stress test with k6

Tuần 5-8 (Không chết)
├── P0: Idempotency triển khai
├── P1: Saga pattern
├── P2: Bulkhead pattern
├── P2: OTEL + tracing
└── P2: Load test + tuning
```
