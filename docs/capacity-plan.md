# Kế hoạch Năng lực (Capacity Plan)

## Mục tiêu: 5.000 người dùng ký đồng thời

---

## 1. Phân tích Bottleneck

### Luồng xử lý 1 transaction điển hình

```
API (5ms) → FilePrepare (500ms) → Hash (100ms) → Provider (5s avg + 60s waiting)
  → Append (200ms) → Upload (200ms) → Webhook (100ms)
```

- **95% thời gian là chờ** — 60s waiting user confirm, 5s CA API call
- **CPU-bound steps** (Hash, Append) chỉ chiếm ~300ms
- **I/O steps** (FilePrepare, Upload, Webhook) phụ thuộc external services

### Real-time throughput chart với 5000 concurrent

```
Trạng thái tại 1 thời điểm:
┌─────────────────────────────────────────────────────┐
│  WaitingUserConfirm (Redis SortedSet)    ~4.750 tx  │ ← 95%
│  ProviderSigning (CA API call)            ~150 tx    │ ← 3%
│  FilePrepare / Hash / Append / Upload     ~100 tx    │ ← 2%
│  Webhook (hoàn thành)                     ~20 tx/s   │ ← throughput
└─────────────────────────────────────────────────────┘
```

### Bottleneck ranking

| # | Bottleneck | Giới hạn | Giải pháp |
|---|---|---|---|
| 🔴 1 | **CA Provider API rate limit** | VNPT ~30 RPM, Viettel ~20 RPM, BKAV ~15 RPM, GCC ~10 RPM, MISA ~10 RPM | Distributed per-provider queue, priority scheduling, fallback providers |
| 🟡 2 | **MongoDB write throughput** | ~5.000 writes/s trên 1 node | Replica set + sharding (nếu >10K tx/day) |
| 🟡 3 | **RabbitMQ queue depth** | 5000 msg/queue có thể gây memory pressure | Prefetch tuning, lazy queues, quorum queues |
| 🟢 4 | **GridFS upload/download** | I/O bound | Multiple mongos, SSD storage |
| 🟢 5 | **External upload server** | ~100 MB/s | Horizontal scaling upload server |

---

## 2. Cấu hình Infrastructure

### RabbitMQ Topology (đã fix)

```
Exchange: exchange.signature./ (Direct)
├── queue.file.prepare   → 5 workers  prefetch=20
├── queue.hash           → 3 workers  prefetch=20
├── queue.provider       → 20 workers prefetch=5   ← worker chính
├── queue.waiting        → 2 workers  prefetch=50  ← poll Redis
├── queue.append         → 3 workers  prefetch=20
├── queue.upload         → 3 workers  prefetch=20
├── queue.webhook        → 3 workers  prefetch=20
└── dlq.messages         → manual replay
```

### Quorum queues cho production

RabbitMQ topology cần dùng **Quorum queues** (thay vì Classic) để đảm bảo HA:

Cập nhật `RabbitMqTopologyInitializer.cs`:

```csharp
void DeclareQueue(string queueName)
{
    var args = new Dictionary<string, object>
    {
        { "x-dead-letter-exchange", dlxName },
        { "x-dead-letter-routing-key", queueName },
        { "x-queue-type", "quorum" },                          // ← Quorum queue
        { "x-quorum-initial-group-size", 3 }                  // ← 3 nodes cluster
    };
    channel.QueueDeclare(queue: queueName, durable: true,
                         exclusive: false, autoDelete: false, arguments: args);
}
```

### Sizing

| Component | Config cho 5K concurrent |
|---|---|
| **API (Kestrel)** | 2 instances (2 CPU, 4GB RAM) — sau load balancer |
| **ProviderWorker** | **10-20 instances** (4 CPU, 8GB RAM) — bottleneck step |
| **FilePrepareWorker** | 3 instances (2 CPU, 4GB RAM) |
| **HashWorker** | 2 instances (2 CPU, 4GB RAM) |
| **AppendWorker** | 2 instances (2 CPU, 4GB RAM) |
| **UploadWorker** | 2 instances (2 CPU, 4GB RAM) |
| **WebhookWorker** | 2 instances (2 CPU, 4GB RAM) |
| **WaitingWorker** | 2 instances (1 CPU, 2GB RAM) — chỉ poll Redis |
| **RabbitMQ** | 3 nodes cluster (4 CPU, 8GB RAM) |
| **MongoDB** | 3 nodes replica set (8 CPU, 32GB RAM, SSD) |
| **Redis** | 1 node (4GB RAM) — cache + lock + waiting set |

---

## 3. File cấu hình cần thay đổi

### 3.1. `DependencyInjection.cs` — Rate limiter tuning

```csharp
// Hiện tại:
services.AddSingleton<ProviderRateLimiter>(sp =>
    new ProviderRateLimiter(capacity: 200, refillRatePerSec: 100));
services.AddSingleton<TenantRateLimiter>(sp =>
    new TenantRateLimiter(capacity: 50, refillRatePerSec: 30));

// Với 5K concurrent, cần nới:
services.AddSingleton<ProviderRateLimiter>(sp =>
    new ProviderRateLimiter(capacity: 500, refillRatePerSec: 200));
services.AddSingleton<TenantRateLimiter>(sp =>
    new TenantRateLimiter(capacity: 200, refillRatePerSec: 100));
```

### 3.2. Provider timeout phân loại

```csharp
// Thay timeout uniform 120s:
services.AddHttpClient<VnptProviderService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);   // VNPT nhanh
});
services.AddHttpClient<ViettelProviderService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);   // Viettel SAD cần lâu hơn
});
services.AddHttpClient<GccProviderService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(180);  // GCC/HSM có thể chậm
});
```

### 3.3. ProviderWorker prefetch và instance scaling

```csharp
// ProviderWorker Program.cs:
services.AddSingleton<IMessageConsumer>(sp =>
    new MessageConsumer(
        sp.GetRequiredService<ILogger<MessageConsumer>>(),
        sp.GetRequiredService<RabbitMqConnection>(),
        RabbitMqTopologyInitializer.ProviderQueue,
        prefetchCount: 5));   // prefetch thấp → phân tán đều giữa các worker
```

### 3.4. appsettings.Production.json

```json
{
  "RabbitMq": {
    "Host": "172.16.40.115",
    "Port": 5672,
    "UserName": "admin",
    "Password": "123456789**",
    "VirtualHost": "/",
    "RequestedHeartbeat": 60,
    "AutomaticRecovery": true,
    "Topology": {
      "UseQuorumQueues": true
    }
  },
  "Worker": {
    "FilePrepare": { "InstanceCount": 3, "PrefetchCount": 20 },
    "Hash": { "InstanceCount": 2, "PrefetchCount": 20 },
    "Provider": { "InstanceCount": 15, "PrefetchCount": 5 },
    "Append": { "InstanceCount": 2, "PrefetchCount": 20 },
    "Upload": { "InstanceCount": 2, "PrefetchCount": 20 },
    "Webhook": { "InstanceCount": 2, "PrefetchCount": 20 },
    "Waiting": { "InstanceCount": 2, "PrefetchCount": 50 }
  },
  "RateLimiting": {
    "ProviderCapacity": 500,
    "ProviderRefillPerSec": 200,
    "TenantCapacity": 200,
    "TenantRefillPerSec": 100
  }
}
```

---

## 4. Monitoring Checklist

Đảm bảo có alert khi:

| Metric | Threshold | Action |
|---|---|---|
| Queue depth > 1000 | 🔴 Critical | Scale worker hoặc tăng prefetch |
| Queue depth > 500 | 🟡 Warning | Kiểm tra worker health |
| CurrentInFlight > 80% provider limit | 🔴 Critical | Rate limiting quá chặt, nới limits |
| RabbitMQ connection drops | 🔴 Critical | Network issue |
| MongDB slow queries > 100ms | 🟡 Warning | Check indexes |
| Provider latency > 10s | 🟡 Warning | Provider đang chậm |
| Redis memory > 3GB | 🟡 Warning | Dọn waiting set |

---

## 5. Load Testing Script (k6)

```javascript
import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  stages: [
    { duration: '2m', target: 1000 },  // ramp up
    { duration: '5m', target: 5000 },  // peak
    { duration: '2m', target: 0 },     // ramp down
  ],
};

const BASE_URL = 'http://localhost:5000';

export default function () {
  const payload = JSON.stringify({
    UserName: `user_${__VU}`,
    Password: 'test',
    FileUnsign: [{ Md5Hash: 'hash_' + __VU, FileName: 'test.xml', FileUrl: 'https://example.com/test.xml' }],
  });

  const res = http.post(`${BASE_URL}/Signature/SignBase64`, payload, {
    headers: { 'Content-Type': 'application/json', 'ApiKey': 'test-key' },
  });

  check(res, { 'status 200': (r) => r.status === 200 });
  sleep(1);
}
```
