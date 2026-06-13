using System.Threading.Channels;

namespace EtwSuite.Core;

public interface IEtwLiveEventConsumer : IAsyncDisposable
{
    ChannelReader<EtwLiveEventRecord> Events { get; }

    Task StartAsync(
        EtwProviderEnableOptions options,
        CancellationToken cancellationToken);

    Task StopAsync();
}

