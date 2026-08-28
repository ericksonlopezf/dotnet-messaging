# Level 02: Middleware Pipeline & OpenTelemetry Observability

## 1. Composable Middleware Pipeline
Intercept publish and consume pipelines for retry, validation, rate limiting, and deduplication:

```csharp
public sealed class LoggingMiddleware : IMessageMiddleware
{
    public async ValueTask InvokeAsync(MessageContext context, MiddlewareDelegate next)
    {
        // Execute pre-processing logic
        await next(context);
        // Execute post-processing logic
    }
}
```

---

## 2. Distributed Tracing with OpenTelemetry
Trace headers are automatically propagated according to W3C Trace Context specifications, creating unified trace graphs across distributed microservices.
