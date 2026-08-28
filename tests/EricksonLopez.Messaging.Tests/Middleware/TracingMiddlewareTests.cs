// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Tests.Middleware;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using AwesomeAssertions;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Diagnostics;
using EricksonLopez.Messaging.Middleware;
using EricksonLopez.Messaging.Tests.Common;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[Trait("Category", "Unit")]
public class TracingMiddlewareTests : IDisposable
{
    private readonly object _syncLock = new();
    private readonly ActivityListener _activityListener;
    private readonly List<Activity> _stoppedActivities = new();
    private readonly MeterListener _meterListener;
    private readonly List<(string MetricName, double Value)> _recordedMeasurements = new();

    private List<Activity> GetStoppedActivities()
    {
        lock (_syncLock)
        {
            return _stoppedActivities.ToList();
        }
    }

    private List<(string MetricName, double Value)> GetRecordedMeasurements()
    {
        lock (_syncLock)
        {
            return _recordedMeasurements.ToList();
        }
    }

    private Func<ActivityCreationOptions<ActivityContext>, ActivitySamplingResult> SampleCallback { get; set; } = _ => ActivitySamplingResult.AllDataAndRecorded;

    public TracingMiddlewareTests()
    {
        _activityListener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == MessagingDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => SampleCallback(options),
            ActivityStopped = activity =>
            {
                lock (_syncLock)
                {
                    _stoppedActivities.Add(activity);
                }
            }
        };
        ActivitySource.AddActivityListener(_activityListener);

        _meterListener = new MeterListener();
        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MessagingDiagnostics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            lock (_syncLock)
            {
                _recordedMeasurements.Add((instrument.Name, measurement));
            }
        });
        _meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            lock (_syncLock)
            {
                _recordedMeasurements.Add((instrument.Name, measurement));
            }
        });
        _meterListener.Start();
    }


    public void Dispose()
    {
        _activityListener.Dispose();
        _meterListener.Dispose();
    }

    [Fact]
    public async Task InvokeAsync_WhenSuccess_StartsActivityWithTagsAndOkStatusAndRecordsDuration()
    {
        // Arrange
        var middleware = new TracingMiddleware();
        const string traceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        var context = TestMessageContextFactory.CreateContext(
            messageType: "orders.created.v1",
            correlationId: "corr-100",
            traceParent: traceParent,
            tenantId: "tenant-99",
            partitionKey: "pk-100");

        // Act
        var result = await middleware.InvokeAsync(
            context,
            (ctx, ct) => ValueTask.FromResult(Result.Success()),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var activity = GetStoppedActivities().First(a => (string?)a.GetTagItem("messaging.message.id") == context.Metadata.MessageId);
        activity.Should().NotBeNull();

        activity.DisplayName.Should().Be("orders.created.v1 process");
        activity.Kind.Should().Be(ActivityKind.Consumer);
        activity.Status.Should().Be(ActivityStatusCode.Ok);
        activity.ParentId.Should().Be(traceParent);

        activity.GetTagItem("messaging.system").Should().Be("ericksonlopez.messaging");
        activity.GetTagItem("messaging.operation").Should().Be("process");
        activity.GetTagItem("messaging.message.id").Should().Be(context.Metadata.MessageId);
        activity.GetTagItem("messaging.message.type").Should().Be("orders.created.v1");
        activity.GetTagItem("messaging.message.conversation_id").Should().Be("corr-100");
        activity.GetTagItem("messaging.tenant.id").Should().Be("tenant-99");
        activity.GetTagItem("messaging.destination.partition.id").Should().Be("pk-100");

        var measurements = GetRecordedMeasurements();
        measurements.Should().Contain(m => m.MetricName == "messaging.process.duration" && m.Value >= 0);
        measurements.Should().NotContain(m => m.MetricName == "messaging.failed.messages");
    }

    [Fact]
    public async Task InvokeAsync_WithoutOptionalMetadata_OmitsTenantAndPartitionTags()
    {
        // Arrange
        var middleware = new TracingMiddleware();
        var context = TestMessageContextFactory.CreateContext(messageType: "orders.created.v1", correlationId: "corr-100");

        // Act
        var result = await middleware.InvokeAsync(
            context,
            (ctx, ct) => ValueTask.FromResult(Result.Success()),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var activity = GetStoppedActivities().First(a => (string?)a.GetTagItem("messaging.message.id") == context.Metadata.MessageId);
        activity.Should().NotBeNull();

        activity.GetTagItem("messaging.tenant.id").Should().BeNull();
        activity.GetTagItem("messaging.destination.partition.id").Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_WhenBusinessFailure_SetsErrorStatusAndIncrementsMessagesFailed()
    {
        // Arrange
        var middleware = new TracingMiddleware();
        var context = TestMessageContextFactory.CreateContext(messageType: "orders.created.v1", correlationId: "corr-100");
        var error = Error.Validation("Order.Invalid", "Invalid item price");

        // Act
        var result = await middleware.InvokeAsync(
            context,
            (ctx, ct) => ValueTask.FromResult(Result.Failure(error)),
            CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        var activity = GetStoppedActivities().First(a => (string?)a.GetTagItem("messaging.message.id") == context.Metadata.MessageId);
        activity.Should().NotBeNull();

        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.StatusDescription.Should().Be("Invalid item price");

        var measurements = GetRecordedMeasurements();
        measurements.Should().Contain(m => m.MetricName == "messaging.failed.messages" && m.Value == 1);
        measurements.Should().Contain(m => m.MetricName == "messaging.process.duration" && m.Value >= 0);
    }

    [Fact]
    public async Task InvokeAsync_WhenExceptionThrown_SetsErrorStatusAddsExceptionAndIncrementsMessagesFailed()
    {
        // Arrange
        var middleware = new TracingMiddleware();
        var context = TestMessageContextFactory.CreateContext(messageType: "orders.created.v1", correlationId: "corr-100");
        var expectedException = new InvalidOperationException("Fatal unexpected processing error");

        // Act
        Func<Task> act = async () => await middleware.InvokeAsync(
            context,
            (ctx, ct) => throw expectedException,
            CancellationToken.None);

        // Assert
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Should().BeSameAs(expectedException);

        var activity = GetStoppedActivities().First(a => (string?)a.GetTagItem("messaging.message.id") == context.Metadata.MessageId);
        activity.Should().NotBeNull();
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.StatusDescription.Should().Be("Fatal unexpected processing error");
        activity.Events.Should().Contain(e => e.Name == "exception");

        var measurements = GetRecordedMeasurements();
        measurements.Should().Contain(m => m.MetricName == "messaging.failed.messages" && m.Value == 1);
        measurements.Should().Contain(m => m.MetricName == "messaging.process.duration" && m.Value >= 0);
    }

    [Fact]
    public async Task InvokeAsync_WhenActivityIsNullAndBusinessFailure_HandlesNullActivityGracefully()
    {
        // Arrange
        SampleCallback = _ => ActivitySamplingResult.None;
        var middleware = new TracingMiddleware();
        var context = TestMessageContextFactory.CreateContext(messageType: "orders.created.v1", correlationId: "corr-100");
        var error = Error.Validation("Order.Invalid", "Invalid item price");

        // Act
        var result = await middleware.InvokeAsync(
            context,
            (ctx, ct) => ValueTask.FromResult(Result.Failure(error)),
            CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WhenActivityIsNullAndExceptionThrown_HandlesNullActivityGracefully()
    {
        // Arrange
        SampleCallback = _ => ActivitySamplingResult.None;
        var middleware = new TracingMiddleware();
        var context = TestMessageContextFactory.CreateContext(messageType: "orders.created.v1", correlationId: "corr-100");
        var expectedException = new InvalidOperationException("Fatal error without activity");

        // Act
        Func<Task> act = async () => await middleware.InvokeAsync(
            context,
            (ctx, ct) => throw expectedException,
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task InvokeAsync_WhenActivityIsNullAndSuccess_HandlesNullActivityGracefully()
    {
        // Arrange
        SampleCallback = _ => ActivitySamplingResult.None;
        var middleware = new TracingMiddleware();
        var context = TestMessageContextFactory.CreateContext(messageType: "orders.created.v1", correlationId: "corr-100");

        // Act
        var result = await middleware.InvokeAsync(
            context,
            (ctx, ct) => ValueTask.FromResult(Result.Success()),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }


    private sealed class TrackingSynchronizationContext : SynchronizationContext
    {
        public int PostCount;

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref PostCount);
            ThreadPool.QueueUserWorkItem(_ => d(state));
        }
    }

    [Fact]
    public void InvokeAsync_ConfiguresAwaitFalse_DoesNotCaptureSynchronizationContext()
    {
        // Arrange
        var syncContext = new TrackingSynchronizationContext();
        var prevContext = SynchronizationContext.Current;
        try
        {
            var tcs = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
            var context = TestMessageContextFactory.CreateContext(messageType: "orders.created.v1", correlationId: "corr-100");
            var middleware = new TracingMiddleware();

            // Set SynchronizationContext BEFORE invoking middleware
            SynchronizationContext.SetSynchronizationContext(syncContext);

            var valueTask = middleware.InvokeAsync(
                context,
                (ctx, ct) => new ValueTask<Result>(tcs.Task),
                CancellationToken.None);

            // Reset SynchronizationContext immediately after invocation setup
            SynchronizationContext.SetSynchronizationContext(prevContext);

            // Act - complete task on ThreadPool
#pragma warning disable xUnit1031
            Task.Run(() => tcs.SetResult(Result.Success())).Wait();

            var result = valueTask.GetAwaiter().GetResult();
#pragma warning restore xUnit1031

            // Assert
            result.IsSuccess.Should().BeTrue();
            syncContext.PostCount.Should().Be(0);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prevContext);
        }
    }
}



