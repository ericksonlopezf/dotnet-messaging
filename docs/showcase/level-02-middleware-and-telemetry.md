# Level 02: Middleware Pipeline & OpenTelemetry Observability

## 1. Composable Middleware Pipeline

Cross-cutting concerns (resilience, security, deduplication, auditing) are handled by composable middleware components implementing `IMessageMiddleware`. Middlewares return `ValueTask<Result>`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Middleware;
using EricksonLopez.Result;
using Microsoft.Extensions.Logging;

public sealed class AuditMiddleware : IMessageMiddleware
{
    private readonly ILogger<AuditMiddleware> _logger;

    public AuditMiddleware(ILogger<AuditMiddleware> logger) => _logger = logger;

    public async ValueTask<Result> InvokeAsync(
        MessageContext context,
        MessageExecutionDelegate next,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Before handler execution. MessageId: {MessageId}, Type: {MessageType}",
            context.Metadata.MessageId,
            context.Metadata.MessageType);

        var result = await next(context, cancellationToken);

        _logger.LogInformation(
            "After handler execution. MessageId: {MessageId}, Success: {IsSuccess}",
            context.Metadata.MessageId,
            result.IsSuccess);

        return result;
    }
}
```

### Pipeline Registration

Register built-in and custom middlewares via `MessagingOptionsBuilder`:

```csharp
builder.Services.AddMessaging(options =>
{
    // Fast-failing Circuit Breaker
    options.AddCircuitBreaker(cb =>
    {
        cb.FailureThreshold = 3;
        cb.BreakDuration = TimeSpan.FromSeconds(10);
    });

    // Per-handler timeout
    options.AddHandlerTimeout(TimeSpan.FromSeconds(5));

    // Jittered exponential retry
    options.AddRetry(r =>
    {
        r.MaxRetries = 3;
        r.InitialDelay = TimeSpan.FromMilliseconds(200);
    });

    // Idempotent deduplication
    options.AddDeduplication(d =>
    {
        d.Expiration = TimeSpan.FromMinutes(10);
    });

    // Custom middleware
    options.AddMiddleware<AuditMiddleware>();
});
```

---

## 2. Distributed Tracing & Metrics with OpenTelemetry

`EricksonLopez.Messaging.OpenTelemetry` automatically injects and extracts W3C `traceparent` headers across all broker transports, generating standardized spans and metric counters.

```csharp
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddMessagingInstrumentation();
        tracing.AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMessagingInstrumentation();
        metrics.AddOtlpExporter();
    });
```

### Emitted Telemetry
- **Traces**: `MessagingDiagnostics.ActivitySourceName` (`"EricksonLopez.Messaging"`) creates activities with standard messaging semantic conventions (`messaging.system`, `messaging.destination`, `messaging.operation`).
- **Metrics**: `MessagingDiagnostics.MeterName` (`"EricksonLopez.Messaging"`) records throughput counters (`MessagesPublished`, `MessagesReceived`, `MessagesFailed`) and duration histograms (`ProcessingDuration`).
