using System.Threading.Channels;

namespace EtwSuite.ViewModels;

internal static class LiveEventBatchReader
{
    public static async Task DrainBatchesAsync<T>(
        ChannelReader<T> reader,
        int maxBatchSize,
        TimeSpan maxDelay,
        Func<IReadOnlyList<T>, Task> flushAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(flushAsync);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBatchSize);
        if (maxDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDelay), maxDelay, "Batch delay must be greater than zero.");
        }

        var batch = new List<T>(maxBatchSize);
        DateTimeOffset? flushDeadline = null;

        try
        {
            while (true)
            {
                while (batch.Count < maxBatchSize && reader.TryRead(out T? item))
                {
                    if (batch.Count == 0)
                    {
                        flushDeadline = DateTimeOffset.UtcNow.Add(maxDelay);
                    }

                    batch.Add(item);
                }

                if (batch.Count >= maxBatchSize)
                {
                    await FlushAsync(batch, flushAsync).ConfigureAwait(false);
                    flushDeadline = null;
                    continue;
                }

                if (batch.Count > 0 && flushDeadline is not null)
                {
                    TimeSpan remainingDelay = flushDeadline.Value - DateTimeOffset.UtcNow;
                    if (remainingDelay <= TimeSpan.Zero)
                    {
                        await FlushAsync(batch, flushAsync).ConfigureAwait(false);
                        flushDeadline = null;
                        continue;
                    }

                    using var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    Task<bool> waitForEvents = reader.WaitToReadAsync(cancellationToken).AsTask();
                    Task waitForDelay = Task.Delay(remainingDelay, delayCancellation.Token);
                    Task completedTask = await Task.WhenAny(waitForEvents, waitForDelay).ConfigureAwait(false);

                    if (completedTask == waitForDelay)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await FlushAsync(batch, flushAsync).ConfigureAwait(false);
                        flushDeadline = null;
                        continue;
                    }

                    await delayCancellation.CancelAsync().ConfigureAwait(false);
                    if (!await waitForEvents.ConfigureAwait(false))
                    {
                        break;
                    }

                    continue;
                }

                if (!await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        if (batch.Count > 0)
        {
            await FlushAsync(batch, flushAsync).ConfigureAwait(false);
        }
    }

    private static Task FlushAsync<T>(
        List<T> batch,
        Func<IReadOnlyList<T>, Task> flushAsync)
    {
        T[] records = batch.ToArray();
        batch.Clear();
        return flushAsync(records);
    }
}
