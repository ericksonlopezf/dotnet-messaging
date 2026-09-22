// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Tests.Pipeline;

using AwesomeAssertions;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Middleware;
using EricksonLopez.Messaging.Tests.Common;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[Trait("Category", "Unit")]
public class MiddlewarePipelineTests
{
    private sealed class OrderTrackingMiddleware : IMessageMiddleware
    {
        private readonly string _name;
        private readonly List<string> _log;

        public OrderTrackingMiddleware(string name, List<string> log)
        {
            _name = name;
            _log = log;
        }

        public async ValueTask<Result> InvokeAsync(
            MessageContext context,
            MessageExecutionDelegate next,
            CancellationToken cancellationToken = default)
        {
            _log.Add($"{_name}:Before");
            var result = await next(context, cancellationToken);
            _log.Add($"{_name}:After");
            return result;
        }
    }

    private sealed class ShortCircuitMiddleware : IMessageMiddleware
    {
        private readonly Result _result;

        public ShortCircuitMiddleware(Result result)
        {
            _result = result;
        }

        public ValueTask<Result> InvokeAsync(
            MessageContext context,
            MessageExecutionDelegate next,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(_result);
        }
    }

    [Fact]
    public async Task ExecuteAsync_NullMiddlewares_DirectlyExecutesTerminalHandler()
    {
        // Arrange
        var pipeline = new MiddlewarePipeline(null);
        var context = TestMessageContextFactory.CreateContext("test.order");
        bool executed = false;

        // Act
        var result = await pipeline.ExecuteAsync(context, (ctx, ct) =>
        {
            executed = true;
            return ValueTask.FromResult(Result.Success());
        });

        // Assert
        result.IsSuccess.Should().BeTrue();
        executed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_EmptyMiddlewares_DirectlyExecutesTerminalHandler()
    {
        // Arrange
        var pipeline = new MiddlewarePipeline(Array.Empty<IMessageMiddleware>());
        var context = TestMessageContextFactory.CreateContext("test.order");
        bool executed = false;

        // Act
        var result = await pipeline.ExecuteAsync(context, (ctx, ct) =>
        {
            executed = true;
            return ValueTask.FromResult(Result.Success());
        });

        // Assert
        result.IsSuccess.Should().BeTrue();
        executed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_NullContext_ThrowsArgumentNullException()
    {
        // Arrange
        var pipeline = new MiddlewarePipeline();

        // Act
        Func<Task> act = async () => await pipeline.ExecuteAsync(null!, (ctx, ct) => ValueTask.FromResult(Result.Success()));

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("context");
    }

    [Fact]
    public async Task ExecuteAsync_NullTerminalHandler_ThrowsArgumentNullException()
    {
        // Arrange
        var pipeline = new MiddlewarePipeline();
        var context = TestMessageContextFactory.CreateContext("test.order");

        // Act
        Func<Task> act = async () => await pipeline.ExecuteAsync(context, null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("terminalHandler");
    }

    [Fact]
    public async Task ExecuteAsync_MultipleMiddlewares_ExecutesInCorrectOrder()
    {
        // Arrange
        var log = new List<string>();
        var m1 = new OrderTrackingMiddleware("M1", log);
        var m2 = new OrderTrackingMiddleware("M2", log);
        var m3 = new OrderTrackingMiddleware("M3", log);
        var pipeline = new MiddlewarePipeline(new[] { m1, m2, m3 });
        var context = TestMessageContextFactory.CreateContext("test.order");

        // Act
        var result = await pipeline.ExecuteAsync(context, (ctx, ct) =>
        {
            log.Add("TerminalHandler");
            return ValueTask.FromResult(Result.Success());
        });

        // Assert
        result.IsSuccess.Should().BeTrue();
        log.Should().Equal(
            "M1:Before",
            "M2:Before",
            "M3:Before",
            "TerminalHandler",
            "M3:After",
            "M2:After",
            "M1:After"
        );
    }

    [Fact]
    public async Task ExecuteAsync_ShortCircuitMiddleware_PreventsSubsequentExecution()
    {
        // Arrange
        var log = new List<string>();
        var m1 = new OrderTrackingMiddleware("M1", log);
        var shortCircuitError = Error.Validation("Block.Error", "Blocked by security");
        var mShort = new ShortCircuitMiddleware(Result.Failure(shortCircuitError));
        var m2 = new OrderTrackingMiddleware("M2", log);
        var pipeline = new MiddlewarePipeline(new IMessageMiddleware[] { m1, mShort, m2 });
        var context = TestMessageContextFactory.CreateContext("test.order");

        // Act
        var result = await pipeline.ExecuteAsync(context, (ctx, ct) =>
        {
            log.Add("TerminalHandler");
            return ValueTask.FromResult(Result.Success());
        });

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Block.Error");
        log.Should().Equal("M1:Before", "M1:After");
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellationToken_ForwardsCancellationTokenToAllSteps()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        CancellationToken capturedToken = default;

        var pipeline = new MiddlewarePipeline();
        var context = TestMessageContextFactory.CreateContext("test.order", cancellationToken: token);

        // Act
        await pipeline.ExecuteAsync(context, (ctx, ct) =>
        {
            capturedToken = ct;
            return ValueTask.FromResult(Result.Success());
        }, token);

        // Assert
        capturedToken.Should().Be(token);
    }

    [Fact]
    public async Task Constructor_WithNonArrayEnumerable_ConvertsToArrayAndExecutes()
    {
        // Arrange
        var log = new List<string>();
        var middlewares = new List<IMessageMiddleware>
        {
            new OrderTrackingMiddleware("ListM", log)
        };
        var pipeline = new MiddlewarePipeline(middlewares);
        var context = TestMessageContextFactory.CreateContext("test.order");

        // Act
        var result = await pipeline.ExecuteAsync(context, (ctx, ct) => ValueTask.FromResult(Result.Success()));

        // Assert
        result.IsSuccess.Should().BeTrue();
        log.Should().Equal("ListM:Before", "ListM:After");
    }

    private sealed class RetryTestMiddleware : IMessageMiddleware
    {
        private readonly int _maxRetries;
        public int Attempts { get; private set; }

        public RetryTestMiddleware(int maxRetries)
        {
            _maxRetries = maxRetries;
        }

        public async ValueTask<Result> InvokeAsync(
            MessageContext context,
            MessageExecutionDelegate next,
            CancellationToken cancellationToken = default)
        {
            Result result = Result.Failure(Error.Unexpected("Initial", "Init"));
            for (int i = 0; i <= _maxRetries; i++)
            {
                Attempts++;
                result = await next(context, cancellationToken);
                if (result.IsSuccess)
                {
                    return result;
                }
            }
            return result;
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithRetryMiddleware_ReexecutesDownstreamMiddlewares()
    {
        // Arrange
        var log = new List<string>();
        var retry = new RetryTestMiddleware(maxRetries: 2);
        var downstream = new OrderTrackingMiddleware("Downstream", log);
        var pipeline = new MiddlewarePipeline(new IMessageMiddleware[] { retry, downstream });
        var context = TestMessageContextFactory.CreateContext("test.order");

        int terminalInvocations = 0;

        // Act
        var result = await pipeline.ExecuteAsync(context, (ctx, ct) =>
        {
            terminalInvocations++;
            if (terminalInvocations < 3)
            {
                return ValueTask.FromResult(Result.Failure(Error.Unexpected("Temporary", "Fail")));
            }
            return ValueTask.FromResult(Result.Success());
        });

        // Assert
        result.IsSuccess.Should().BeTrue();
        retry.Attempts.Should().Be(3);
        terminalInvocations.Should().Be(3);
        log.FindAll(x => x == "Downstream:Before").Count.Should().Be(3);
        log.FindAll(x => x == "Downstream:After").Count.Should().Be(3);
    }

    [Fact]
    public void BuildChain_NullTerminalHandler_ThrowsArgumentNullException()
    {
        var pipeline = new MiddlewarePipeline(null);
        var act = () => pipeline.BuildChain(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("terminalHandler");
    }

    [Fact]
    public void BuildChain_EmptyMiddlewares_ReturnsExactTerminalHandlerInstance()
    {
        var pipeline = new MiddlewarePipeline([]);
        MessageExecutionDelegate terminal = (ctx, ct) => ValueTask.FromResult(Result.Success());
        var chain = pipeline.BuildChain(terminal);
        ReferenceEquals(chain, terminal).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_FourOrMoreMiddlewares_ExecutesAllInOrderUsingInvokeStepAsync()
    {
        var log = new List<string>();
        var m1 = new OrderTrackingMiddleware("M1", log);
        var m2 = new OrderTrackingMiddleware("M2", log);
        var m3 = new OrderTrackingMiddleware("M3", log);
        var m4 = new OrderTrackingMiddleware("M4", log);

        var pipeline = new MiddlewarePipeline([m1, m2, m3, m4]);
        var context = TestMessageContextFactory.CreateContext("test.order");

        var result = await pipeline.ExecuteAsync(context, (ctx, ct) =>
        {
            log.Add("Terminal");
            return ValueTask.FromResult(Result.Success());
        });

        result.IsSuccess.Should().BeTrue();
        log.Should().Equal(
            "M1:Before", "M2:Before", "M3:Before", "M4:Before",
            "Terminal",
            "M4:After", "M3:After", "M2:After", "M1:After");
    }
}




