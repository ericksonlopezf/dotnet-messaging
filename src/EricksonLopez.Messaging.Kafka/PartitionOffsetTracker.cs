// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;

namespace EricksonLopez.Messaging.Transport.Kafka;

/// <summary>
/// Tracks in-flight and completed Kafka offsets per partition to guarantee that offset commits
/// are strictly monotonic and contiguous, preventing silent message skipping when concurrent worker tasks finish out of order.
/// </summary>
internal sealed class PartitionOffsetTracker
{
    private readonly object _lock = new();
    private readonly SortedSet<long> _inFlight = new();
    private readonly SortedSet<long> _completed = new();
    private long _highestCommittedOffset;
    private bool _initialized;

    /// <summary>
    /// Gets the number of offsets currently being processed.
    /// </summary>
    public int InFlightCount
    {
        get
        {
            lock (_lock)
            {
                return _inFlight.Count;
            }
        }
    }

    /// <summary>
    /// Gets the number of completed offsets waiting for preceding contiguous offsets to finish.
    /// </summary>
    public int PendingCommitCount
    {
        get
        {
            lock (_lock)
            {
                return _completed.Count;
            }
        }
    }

    /// <summary>
    /// Registers an offset as in-flight before dispatching it to a worker handler.
    /// </summary>
    /// <param name="offset">The offset of the consumed message.</param>
    public void Track(long offset)
    {
        lock (_lock)
        {
            if (!_initialized)
            {
                _highestCommittedOffset = offset - 1;
                _initialized = true;
            }

            _inFlight.Add(offset);
        }
    }

    /// <summary>
    /// Marks an offset as successfully completed.
    /// </summary>
    /// <param name="offset">The completed message offset.</param>
    /// <returns>
    /// The next offset to commit to Kafka (i.e., highest contiguous completed offset + 1)
    /// if the contiguous watermark advanced; otherwise, <see langword="null"/> if a preceding offset is still in-flight or failed.
    /// </returns>
    public long? MarkCompleted(long offset)
    {
        lock (_lock)
        {
            _inFlight.Remove(offset);
            _completed.Add(offset);

            long? newCommitOffset = null;
            while (_completed.Count > 0 && _completed.Min == _highestCommittedOffset + 1)
            {
                var minCompleted = _completed.Min;
                _highestCommittedOffset = minCompleted;
                _completed.Remove(minCompleted);
                newCommitOffset = minCompleted + 1;
            }

            return newCommitOffset;
        }
    }

    /// <summary>
    /// Marks an offset as failed (e.g. unhandled exception or Nack).
    /// </summary>
    /// <param name="offset">The failed message offset.</param>
    public void MarkFailed(long offset)
    {
        lock (_lock)
        {
            _inFlight.Remove(offset);
            // The offset is removed from in-flight but not added to _completed.
            // This intentionally halts contiguous watermark advancement for subsequent offsets on this partition.
        }
    }
}
