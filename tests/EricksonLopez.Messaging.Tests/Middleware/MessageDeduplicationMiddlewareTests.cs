// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Middleware;
using EricksonLopez.Messaging.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Messaging.Tests.Middleware;

using Result = EricksonLopez.Result.Result;

public sealed class MessageDeduplicationMiddlewareTests
{
    private sealed record TestOrder(Guid OrderId, decimal Amount) : IMessage;

    [Fact]
    public async Task InvokeAsync_FirstTimeMessage_ExecutesHandlerAndReturnsSuccess()
    {
        var store = new InMemoryMessageDeduplicationStore();
        var middleware = new MessageDeduplicationMiddleware(store);

        var executed = false;
        var meta = TestMessageContextFactory.CreateMetadata("test.order") with { MessageId = "msg-first-time-01" };
        var context = new MessageContext(meta, new ServiceCollection().BuildServiceProvider())
        {
            Message = new TestOrder(Guid.NewGuid(), 100m)
        };

        var result = await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            executed = true;
            return ValueTask.FromResult(Result.Success());
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        executed.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_DuplicateMessage_SkipsHandlerAndReturnsSuccess()
    {
        var store = new InMemoryMessageDeduplicationStore();
        var middleware = new MessageDeduplicationMiddleware(store);

        var meta = TestMessageContextFactory.CreateMetadata("test.order") with { MessageId = "msg-duplicate-02" };
        var context = new MessageContext(meta, new ServiceCollection().BuildServiceProvider())
        {
            Message = new TestOrder(Guid.NewGuid(), 150m)
        };

        // First execution succeeds
        var firstResult = await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Success()), CancellationToken.None);
        firstResult.IsSuccess.Should().BeTrue();

        // Second execution with identical MessageId should be skipped
        var secondExecuted = false;
        var secondResult = await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            secondExecuted = true;
            return ValueTask.FromResult(Result.Success());
        }, CancellationToken.None);

        secondResult.IsSuccess.Should().BeTrue();
        secondExecuted.Should().BeFalse("duplicate message execution must be suppressed by deduplication middleware");
    }

    [Fact]
    public async Task InvokeAsync_ExpiredMessage_AllowsReExecution()
    {
        var store = new InMemoryMessageDeduplicationStore();
        var options = Options.Create(new MessageDeduplicationOptions
        {
            Expiration = TimeSpan.FromMilliseconds(50)
        });
        var middleware = new MessageDeduplicationMiddleware(store, options);

        var meta = TestMessageContextFactory.CreateMetadata("test.order") with { MessageId = "msg-expired-03" };
        var context = new MessageContext(meta, new ServiceCollection().BuildServiceProvider())
        {
            Message = new TestOrder(Guid.NewGuid(), 200m)
        };

        // First execution
        await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Success()), CancellationToken.None);

        // Wait for TTL expiration
        await Task.Delay(100);

        // Second execution after expiration should execute
        var secondExecuted = false;
        var secondResult = await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            secondExecuted = true;
            return ValueTask.FromResult(Result.Success());
        }, CancellationToken.None);

        secondResult.IsSuccess.Should().BeTrue();
        secondExecuted.Should().BeTrue("expired deduplication record should permit re-execution");
    }

    [Fact]
    public async Task InvokeAsync_DisabledDeduplication_DoesNotSuppressDuplicates()
    {
        var store = new InMemoryMessageDeduplicationStore();
        var options = Options.Create(new MessageDeduplicationOptions { Enabled = false });
        var middleware = new MessageDeduplicationMiddleware(store, options);

        var meta = TestMessageContextFactory.CreateMetadata("test.order") with { MessageId = "msg-disabled-04" };
        var context = new MessageContext(meta, new ServiceCollection().BuildServiceProvider())
        {
            Message = new TestOrder(Guid.NewGuid(), 50m)
        };

        var executions = 0;
        await middleware.InvokeAsync(context, (ctx, ct) => { executions++; return ValueTask.FromResult(Result.Success()); }, CancellationToken.None);
        await middleware.InvokeAsync(context, (ctx, ct) => { executions++; return ValueTask.FromResult(Result.Success()); }, CancellationToken.None);

        executions.Should().Be(2, "disabled deduplication must allow all invocations");
    }

    [Fact]
    public async Task InvokeAsync_EmptyMessageId_PassesThrough()
    {
        var store = new InMemoryMessageDeduplicationStore();
        var middleware = new MessageDeduplicationMiddleware(store);

        var meta = TestMessageContextFactory.CreateMetadata("test.order") with { MessageId = "" };
        var context = new MessageContext(meta, new ServiceCollection().BuildServiceProvider())
        {
            Message = new TestOrder(Guid.NewGuid(), 75m)
        };

        var executed = false;
        var result = await middleware.InvokeAsync(context, (ctx, ct) =>
        {
            executed = true;
            return ValueTask.FromResult(Result.Success());
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        executed.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WithCustomLogger_LogsDuplicateSkippedMessage()
    {
        var store = new InMemoryMessageDeduplicationStore();
        var logger = NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<MessageDeduplicationMiddleware>>();
        var middleware = new MessageDeduplicationMiddleware(store, logger: logger);

        var meta = TestMessageContextFactory.CreateMetadata("test.order") with { MessageId = "msg-dup-logged" };
        var context = new MessageContext(meta, new ServiceCollection().BuildServiceProvider())
        {
            Message = new TestOrder(Guid.NewGuid(), 100m)
        };

        await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Success()), CancellationToken.None);
        await middleware.InvokeAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Success()), CancellationToken.None);

        logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }
}
