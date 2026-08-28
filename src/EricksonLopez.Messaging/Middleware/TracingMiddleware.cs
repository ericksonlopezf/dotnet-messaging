// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Messaging.Middleware;

using System.Diagnostics;
using EricksonLopez.Messaging.Contracts;
using EricksonLopez.Messaging.Diagnostics;
using EricksonLopez.Result;

/// <summary>
/// Provides OpenTelemetry distributed tracing and metrics middleware for message processing.
/// </summary>
public sealed class TracingMiddleware : IMessageMiddleware
{
    /// <inheritdoc />
    public async ValueTask<Result> InvokeAsync(
        MessageContext context,
        MessageExecutionDelegate next,
        CancellationToken cancellationToken)
    {
        var metadata = context.Metadata;
        var parentContext = default(ActivityContext);

        if (!string.IsNullOrWhiteSpace(metadata.TraceParent))
        {
            ActivityContext.TryParse(metadata.TraceParent, null, out parentContext);
        }

        using var activity = MessagingDiagnostics.ActivitySource.StartActivity(
            $"{metadata.MessageType} process",
            ActivityKind.Consumer,
            parentContext);

        if (activity is not null)
        {
            activity.SetTag("messaging.system", "ericksonlopez.messaging");
            activity.SetTag("messaging.operation", "process");
            activity.SetTag("messaging.message.id", metadata.MessageId);
            activity.SetTag("messaging.message.type", metadata.MessageType);
            activity.SetTag("messaging.message.conversation_id", metadata.CorrelationId);

            if (!string.IsNullOrEmpty(metadata.TenantId))
            {
                activity.SetTag("messaging.tenant.id", metadata.TenantId);
            }
            if (!string.IsNullOrEmpty(metadata.PartitionKey))
            {
                activity.SetTag("messaging.destination.partition.id", metadata.PartitionKey);
            }
        }

        var startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            var result = await next(context, cancellationToken).ConfigureAwait(false);
            MessagingDiagnostics.ProcessingDuration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);

            if (result.IsFailure)
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.Error.Description);
                MessagingDiagnostics.MessagesFailed.Add(1);
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
            }

            return result;
        }
        catch (Exception ex)
        {
            MessagingDiagnostics.ProcessingDuration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            MessagingDiagnostics.MessagesFailed.Add(1);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            throw;
        }
    }
}



