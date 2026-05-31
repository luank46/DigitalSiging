using DigitalSigning.Api.Controllers;
using DigitalSigning.Api.Models.DTOs;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.Models;
using DigitalSigning.Core.MessageQueue.Interfaces;
using DigitalSigning.Core.Protection;
using DigitalSigning.Core.Services;
using DigitalSigning.Core.Settings;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace DigitalSigning.Api.Tests;

public class TransactionControllerTests
{
    private readonly Mock<ITransactionService> _txServiceMock = new();
    private readonly Mock<IMessagePublisher> _publisherMock = new();
    private readonly Mock<IIdempotencyService> _idempotencyMock = new();
    private readonly Mock<IMetricsService> _metricsMock = new();
    private readonly Mock<ILogger<TransactionController>> _loggerMock = new();
    private readonly Mock<IRateLimiter> _rateLimiterMock = new();
    private readonly Mock<FileLockService> _fileLockMock;
    private readonly FeatureFlags _featureFlags = new() { IdempotencyV2 = true };

    private readonly TransactionController _controller;
    private readonly TransactionController _controllerV1;
    private readonly FileLockService _fileLock;

    public TransactionControllerTests()
    {
        _rateLimiterMock.Setup(r => r.TryAcquire(It.IsAny<ProviderType>(), It.IsAny<string>()))
            .Returns(true);

        // FileLockService setup
        _fileLock = CreateFileLockService();

        _controller = CreateController(_featureFlags);    // V2 (default)
        _controllerV1 = CreateController(new FeatureFlags { IdempotencyV2 = false });
    }

    private FileLockService CreateFileLockService()
    {
        var redisMock = new Mock<StackExchange.Redis.IConnectionMultiplexer>();
        var dbMock = new Mock<StackExchange.Redis.IDatabase>();
        redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(dbMock.Object);
        dbMock.Setup(d => d.KeyExistsAsync(It.IsAny<StackExchange.Redis.RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);
        dbMock.Setup(d => d.LockTakeAsync(
                It.IsAny<StackExchange.Redis.RedisKey>(),
                It.IsAny<StackExchange.Redis.RedisValue>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        dbMock.Setup(d => d.LockReleaseAsync(
                It.IsAny<StackExchange.Redis.RedisKey>(),
                It.IsAny<StackExchange.Redis.RedisValue>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        var loggerFactory = LoggerFactory.Create(b => { });
        var fileLockLogger = loggerFactory.CreateLogger<FileLockService>();
        return new FileLockService(redisMock.Object, fileLockLogger);
    }

    private TransactionController CreateController(FeatureFlags flags)
    {
        var controller = new TransactionController(
            _txServiceMock.Object,
            _publisherMock.Object,
            _idempotencyMock.Object,
            _metricsMock.Object,
            _rateLimiterMock.Object,
            flags,
            _fileLock,
            _loggerMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        return controller;
    }

    private static CreateTransactionRequest ValidRequest() => new()
    {
        TenantId = "school-1",
        ProviderType = ProviderType.Vnpt,
        FileUrls = new List<string> { "https://example.com/file1.pdf" },
        IdempotencyKey = null
    };

    // ── CreateTransaction: Success ─────────────────────────────────────

    [Fact]
    public async Task CreateTransaction_WithValidRequest_ReturnsOkWithMaGiaoDich()
    {
        // Arrange
        var request = ValidRequest();
        _txServiceMock.Setup(s => s.CreateAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Transaction tx, CancellationToken _) =>
            {
                tx.MaGiaoDich = "test-tx-id";
                return tx;
            });

        // Act
        var result = await _controller.CreateTransaction(request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<CreateTransactionResponse>().Subject;
        response.MaGiaoDich.Should().NotBeNullOrEmpty();
        response.CurrentStatus.Should().Be(TransactionStatus.Created);

        _txServiceMock.Verify(s => s.CreateAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()), Times.Once);
        _metricsMock.Verify(m => m.IncrementCounter("transactions_created_total", It.IsAny<string[]>()), Times.Once);
    }

    // ── CreateTransaction: Validation Failure ──────────────────────────

    [Fact]
    public async Task CreateTransaction_WithMissingTenantId_ReturnsBadRequest()
    {
        // Arrange
        var request = ValidRequest();
        request.TenantId = "";

        // Act
        var result = await _controller.CreateTransaction(request);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        _txServiceMock.Verify(s => s.CreateAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateTransaction_WithTooManyFileUrls_ReturnsBadRequest()
    {
        // Arrange
        var request = ValidRequest();
        request.FileUrls = Enumerable.Range(0, 11)
            .Select(i => $"https://example.com/file{i}.pdf")
            .ToList();

        // Act
        var result = await _controller.CreateTransaction(request);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        _txServiceMock.Verify(s => s.CreateAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateTransaction_WithInvalidFileUrl_ReturnsBadRequest()
    {
        // Arrange
        var request = ValidRequest();
        request.FileUrls = new List<string> { "ftp://invalid-scheme.com/file.pdf" };

        // Act
        var result = await _controller.CreateTransaction(request);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateTransaction_WithEmptyFileUrls_ReturnsBadRequest()
    {
        // Arrange
        var request = ValidRequest();
        request.FileUrls = new List<string>();

        // Act
        var result = await _controller.CreateTransaction(request);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── CreateTransaction: Idempotency ─────────────────────────────────

    [Fact]
    public async Task CreateTransaction_WithNewIdempotencyKey_CreatesTransaction()
    {
        // Arrange
        var request = ValidRequest();
        request.IdempotencyKey = "unique-key-123";

        _idempotencyMock.Setup(s => s.GetByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((IdempotencyKey?)null); // no existing key

        _idempotencyMock.Setup(s => s.CheckAndStoreAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(IdempotencyResult.Added);

        _txServiceMock.Setup(s => s.CreateAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Transaction tx, CancellationToken _) => tx);

        // Act
        var result = await _controller.CreateTransaction(request);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        _txServiceMock.Verify(s => s.CreateAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateTransaction_WithDuplicateIdempotencyKey_ReturnsExistingTransaction()
    {
        // Arrange
        var request = ValidRequest();
        request.IdempotencyKey = "existing-key";

        var existingKey = new IdempotencyKey
        {
            TenantId = "school-1",
            Key = "existing-key",
            TransactionId = "existing-tx-id"
        };

        var existingTx = new Transaction
        {
            MaGiaoDich = "existing-tx-id",
            CurrentStatus = TransactionStatus.Completed
        };

        _idempotencyMock.Setup(s => s.GetByIdempotencyKeyAsync("school-1", "existing-key"))
            .ReturnsAsync(existingKey);
        _txServiceMock.Setup(s => s.GetByMaGiaoDichAsync("existing-tx-id"))
            .ReturnsAsync(existingTx);

        // Act
        var result = await _controller.CreateTransaction(request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<CreateTransactionResponse>().Subject;
        response.MaGiaoDich.Should().Be("existing-tx-id");
        response.CurrentStatus.Should().Be(TransactionStatus.Completed);

        // Verify no new transaction was created
        _txServiceMock.Verify(s => s.CreateAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateTransaction_WithDuplicateKeyButStaleTx_CreatesNewTransaction()
    {
        // Arrange
        var request = ValidRequest();
        request.IdempotencyKey = "stale-key";

        var existingKey = new IdempotencyKey
        {
            TenantId = "school-1",
            Key = "stale-key",
            TransactionId = "deleted-tx-id"
        };

        _idempotencyMock.Setup(s => s.GetByIdempotencyKeyAsync("school-1", "stale-key"))
            .ReturnsAsync(existingKey);
        _txServiceMock.Setup(s => s.GetByMaGiaoDichAsync("deleted-tx-id"))
            .ReturnsAsync((Transaction?)null); // transaction was deleted

        // Need CheckAndStoreAsync to succeed since the idempotency key exists
        // but the transaction was deleted — controller will not try to store again
        // because it already got the key. So we need to let it pass through.

        _idempotencyMock.Setup(s => s.CheckAndStoreAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(IdempotencyResult.Added);

        _txServiceMock.Setup(s => s.CreateAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Transaction tx, CancellationToken _) => tx);

        // Act
        var result = await _controller.CreateTransaction(request);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        _txServiceMock.Verify(s => s.CreateAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateTransaction_WithRaceConditionOnIdempotency_ReturnsWinningTransaction()
    {
        // Arrange
        var request = ValidRequest();
        request.IdempotencyKey = "racing-key";

        // First call: no existing key found
        _idempotencyMock.SetupSequence(s => s.GetByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((IdempotencyKey?)null) // first check — nothing
            .ReturnsAsync(new IdempotencyKey       // second check (after Duplicate) — found
            {
                TenantId = "school-1",
                Key = "racing-key",
                TransactionId = "winner-tx-id"
            });

        // CheckAndStoreAsync returns Duplicate — another thread got there first
        _idempotencyMock.Setup(s => s.CheckAndStoreAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(IdempotencyResult.Duplicate);

        var winningTx = new Transaction
        {
            MaGiaoDich = "winner-tx-id",
            CurrentStatus = TransactionStatus.Completed
        };
        _txServiceMock.Setup(s => s.GetByMaGiaoDichAsync("winner-tx-id"))
            .ReturnsAsync(winningTx);

        // Act
        var result = await _controller.CreateTransaction(request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<CreateTransactionResponse>().Subject;
        response.MaGiaoDich.Should().Be("winner-tx-id");

        // No new transaction was created
        _txServiceMock.Verify(s => s.CreateAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── CreateTransaction: Rate Limiting ───────────────────────────────

    [Fact]
    public async Task CreateTransaction_WhenRateLimited_Returns429()
    {
        // Arrange
        _rateLimiterMock.Setup(r => r.TryAcquire(It.IsAny<ProviderType>(), It.IsAny<string>()))
            .Returns(false);
        var request = ValidRequest();

        // Act
        var result = await _controller.CreateTransaction(request);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(429);
        _txServiceMock.Verify(s => s.CreateAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── CreateTransaction: Cancellation ────────────────────────────────

    [Fact]
    public async Task CreateTransaction_WhenCancelled_Returns499()
    {
        // Arrange
        var request = ValidRequest();
        _txServiceMock.Setup(s => s.CreateAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _controller.CreateTransaction(request);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(499);
    }

    // ── CreateTransaction: Timeout ─────────────────────────────────────

    [Fact]
    public async Task CreateTransaction_WhenTimeout_Returns503()
    {
        // Arrange
        var request = ValidRequest();
        _txServiceMock.Setup(s => s.CreateAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("MongoDB timeout"));

        // Act
        var result = await _controller.CreateTransaction(request);

        // Assert
        var statusCodeResult = result.Should().BeOfType<ObjectResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(503);
    }

    // ── GetTransaction ─────────────────────────────────────────────────

    [Fact]
    public async Task GetTransaction_WithMissingMaGiaoDich_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.GetTransaction(new GetTransactionRequest { MaGiaoDich = "" });

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetTransaction_WithExistingId_ReturnsTransaction()
    {
        // Arrange
        var tx = new Transaction
        {
            MaGiaoDich = "test-tx",
            TenantId = "school-1",
            ProviderType = ProviderType.Vnpt,
            CurrentStatus = TransactionStatus.Completed,
            CreatedAt = DateTime.UtcNow
        };
        _txServiceMock.Setup(s => s.GetByMaGiaoDichAsync("test-tx"))
            .ReturnsAsync(tx);

        // Act
        var result = await _controller.GetTransaction(new GetTransactionRequest { MaGiaoDich = "test-tx" });

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<TransactionResponse>().Subject;
        response.MaGiaoDich.Should().Be("test-tx");
        response.CurrentStatus.Should().Be(TransactionStatus.Completed);
    }

    [Fact]
    public async Task GetTransaction_WithNonExistentId_ReturnsNotFound()
    {
        // Arrange
        _txServiceMock.Setup(s => s.GetByMaGiaoDichAsync("ghost-tx"))
            .ReturnsAsync((Transaction?)null);

        // Act
        var result = await _controller.GetTransaction(new GetTransactionRequest { MaGiaoDich = "ghost-tx" });

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ── V1 Idempotency Fallback ──────────────────────────────────────

    [Fact]
    public async Task CreateTransaction_V1Fallback_WithNewKey_CreatesTransaction()
    {
        // Arrange
        var request = ValidRequest();
        request.IdempotencyKey = "v1-new-key";

        _idempotencyMock.Setup(s => s.GetByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((IdempotencyKey?)null);
        _idempotencyMock.Setup(s => s.CheckAndStoreAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(IdempotencyResult.Added);
        _txServiceMock.Setup(s => s.CreateAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Transaction tx, CancellationToken _) => tx);

        // Act
        var result = await _controllerV1.CreateTransaction(request);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        _txServiceMock.Verify(s => s.CreateAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateTransaction_V1Fallback_WithDuplicateKey_StillCreatesTransaction()
    {
        // Arrange
        var request = ValidRequest();
        request.IdempotencyKey = "v1-dup-key";

        // V1 fallback only logs duplicate — still proceeds to create new transaction
        _idempotencyMock.Setup(s => s.GetByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new IdempotencyKey
            {
                TenantId = "school-1",
                Key = "v1-dup-key",
                TransactionId = "existing-tx-id"
            });
        _idempotencyMock.Setup(s => s.CheckAndStoreAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(IdempotencyResult.Duplicate);
        _txServiceMock.Setup(s => s.CreateAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Transaction tx, CancellationToken _) => tx);

        // Act
        var result = await _controllerV1.CreateTransaction(request);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        // V1 fallback should still create a new transaction despite duplicate key
        _txServiceMock.Verify(s => s.CreateAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
