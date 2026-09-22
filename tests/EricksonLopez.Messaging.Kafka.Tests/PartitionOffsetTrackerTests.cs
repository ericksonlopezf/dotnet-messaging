// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Messaging.Transport.Kafka;
using Xunit;

namespace EricksonLopez.Messaging.Kafka.Tests;

[Trait("Category", "Unit")]
public class PartitionOffsetTrackerTests
{
    [Fact]
    public void Track_AddsToInFlightCount()
    {
        var tracker = new PartitionOffsetTracker();
        tracker.Track(100);
        tracker.Track(101);

        tracker.InFlightCount.Should().Be(2);
        tracker.PendingCommitCount.Should().Be(0);
    }

    [Fact]
    public void MarkCompleted_SequentialOffsets_AdvancesWatermarkMonotonically()
    {
        var tracker = new PartitionOffsetTracker();
        tracker.Track(0);
        tracker.Track(1);
        tracker.Track(2);

        var c0 = tracker.MarkCompleted(0);
        c0.Should().Be(1);

        var c1 = tracker.MarkCompleted(1);
        c1.Should().Be(2);

        var c2 = tracker.MarkCompleted(2);
        c2.Should().Be(3);

        tracker.InFlightCount.Should().Be(0);
        tracker.PendingCommitCount.Should().Be(0);
    }

    [Fact]
    public void MarkCompleted_OutOfOrderOffsets_ReturnsNullUntilGapFilled()
    {
        var tracker = new PartitionOffsetTracker();
        tracker.Track(10);
        tracker.Track(11);
        tracker.Track(12);

        // 12 finishes first
        var c12 = tracker.MarkCompleted(12);
        c12.Should().BeNull();
        tracker.PendingCommitCount.Should().Be(1);

        // 11 finishes second
        var c11 = tracker.MarkCompleted(11);
        c11.Should().BeNull();
        tracker.PendingCommitCount.Should().Be(2);

        // 10 finishes last — fills the contiguous gap!
        var c10 = tracker.MarkCompleted(10);
        c10.Should().Be(13); // Advances watermark through 10, 11, and 12, next commit is 13
        tracker.InFlightCount.Should().Be(0);
        tracker.PendingCommitCount.Should().Be(0);
    }

    [Fact]
    public void MarkFailed_PreventsWatermarkFromAdvancingPastFailedOffset()
    {
        var tracker = new PartitionOffsetTracker();
        tracker.Track(20);
        tracker.Track(21);
        tracker.Track(22);

        // 21 and 22 succeed
        var c21 = tracker.MarkCompleted(21);
        c21.Should().BeNull();

        var c22 = tracker.MarkCompleted(22);
        c22.Should().BeNull();

        // 20 fails
        tracker.MarkFailed(20);

        // Even though 21 and 22 finished, 20 failed and was not committed.
        // Therefore watermark must NEVER advance past 20, preventing Kafka from skipping message 20!
        tracker.InFlightCount.Should().Be(0);
        tracker.PendingCommitCount.Should().Be(2);
    }

    [Fact]
    public void NonZeroInitialOffset_CorrectlyInitializesWatermark()
    {
        var tracker = new PartitionOffsetTracker();
        tracker.Track(5000);
        tracker.Track(5001);

        var c5000 = tracker.MarkCompleted(5000);
        c5000.Should().Be(5001);

        var c5001 = tracker.MarkCompleted(5001);
        c5001.Should().Be(5002);
    }

    [Fact]
    public async Task ConcurrentStress_MultipleThreads_NeverMissesOrDuplicatesOffsets()
    {
        var tracker = new PartitionOffsetTracker();
        const int count = 1000;
        var offsets = Enumerable.Range(100, count).Select(x => (long)x).ToList();

        foreach (var offset in offsets)
        {
            tracker.Track(offset);
        }

        // Shuffle offsets to simulate random concurrent worker completion orders
        var rng = new Random(42);
        var shuffled = offsets.OrderBy(_ => rng.Next()).ToList();

        long? highestObservedCommit = null;
        var commitList = new List<long>();
        var syncLock = new object();

        await Parallel.ForEachAsync(shuffled, new ParallelOptions { MaxDegreeOfParallelism = 16 }, (offset, ct) =>
        {
            var commit = tracker.MarkCompleted(offset);
            if (commit.HasValue)
            {
                lock (syncLock)
                {
                    commitList.Add(commit.Value);
                    if (highestObservedCommit is null || commit.Value > highestObservedCommit.Value)
                    {
                        highestObservedCommit = commit.Value;
                    }
                }
            }
            return ValueTask.CompletedTask;
        });

        highestObservedCommit.Should().Be(100 + count);
        tracker.InFlightCount.Should().Be(0);
        tracker.PendingCommitCount.Should().Be(0);

        // Offsets committed must be strictly increasing
        for (int i = 1; i < commitList.Count; i++)
        {
            commitList[i].Should().BeGreaterThan(commitList[i - 1]);
        }
    }
}
