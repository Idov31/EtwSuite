using System.Diagnostics;
using System.Threading.Channels;
using EtwSuite.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EtwSuite.Tests;

[TestClass]
public sealed class LiveEventBatchReaderTests
{
    [TestMethod]
    public async Task ReadBatchesAsync_FlushesWhenBatchSizeIsReached()
    {
        Channel<int> channel = Channel.CreateUnbounded<int>();
        for (int index = 0; index < 10; index++)
        {
            await channel.Writer.WriteAsync(index);
        }

        channel.Writer.Complete();

        IReadOnlyList<int> batch = await ReadFirstBatchAsync(
            channel.Reader,
            maxBatchSize: 10,
            maxDelay: TimeSpan.FromSeconds(10));

        CollectionAssert.AreEqual(Enumerable.Range(0, 10).ToArray(), batch.ToArray());
    }

    [TestMethod]
    public async Task ReadBatchesAsync_FlushesPartialBatchAfterDelay()
    {
        Channel<int> channel = Channel.CreateUnbounded<int>();
        await channel.Writer.WriteAsync(42);

        var stopwatch = Stopwatch.StartNew();
        IReadOnlyList<int> batch = await ReadFirstBatchAsync(
            channel.Reader,
            maxBatchSize: 10,
            maxDelay: TimeSpan.FromMilliseconds(100));

        Assert.IsTrue(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(75));
        CollectionAssert.AreEqual(new[] { 42 }, batch.ToArray());
    }

    [TestMethod]
    public async Task ReadBatchesAsync_FlushesPartialBatchWhenChannelCompletes()
    {
        Channel<int> channel = Channel.CreateUnbounded<int>();
        await channel.Writer.WriteAsync(1);
        await channel.Writer.WriteAsync(2);
        channel.Writer.Complete();

        IReadOnlyList<int> batch = await ReadFirstBatchAsync(
            channel.Reader,
            maxBatchSize: 10,
            maxDelay: TimeSpan.FromSeconds(10));

        CollectionAssert.AreEqual(new[] { 1, 2 }, batch.ToArray());
    }

    [TestMethod]
    public async Task ReadBatchesAsync_FlushesPartialBatchWhenCanceled()
    {
        Channel<int> channel = Channel.CreateUnbounded<int>();
        await channel.Writer.WriteAsync(7);

        IReadOnlyList<int>? flushedBatch = null;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await LiveEventBatchReader.DrainBatchesAsync(
            channel.Reader,
            maxBatchSize: 10,
            maxDelay: TimeSpan.FromSeconds(10),
            batch =>
            {
                flushedBatch = batch;
                return Task.CompletedTask;
            },
            cancellation.Token);

        Assert.IsNotNull(flushedBatch);
        CollectionAssert.AreEqual(new[] { 7 }, flushedBatch.ToArray());
    }

    private static async Task<IReadOnlyList<int>> ReadFirstBatchAsync(
        ChannelReader<int> reader,
        int maxBatchSize,
        TimeSpan maxDelay)
    {
        IReadOnlyList<int>? firstBatch = null;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await LiveEventBatchReader.DrainBatchesAsync(
            reader,
            maxBatchSize,
            maxDelay,
            batch =>
            {
                firstBatch = batch;
                cancellation.Cancel();
                return Task.CompletedTask;
            },
            cancellation.Token);

        if (firstBatch is not null)
        {
            return firstBatch;
        }

        Assert.Fail("No batch was produced.");
        return Array.Empty<int>();
    }
}
