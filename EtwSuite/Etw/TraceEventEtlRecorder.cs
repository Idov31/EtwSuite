using EtwSuite.Core;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace EtwSuite.Etw;

public sealed class TraceEventEtlRecorder : IAsyncDisposable
{
    private TraceEventSession? _session;
    private string? _filePath;

    public string? FilePath => _filePath;

    public Task StartAsync(
        EtwProviderEnableOptions options,
        string filePath,
        CancellationToken cancellationToken)
    {
        if (_session is not null)
        {
            throw new InvalidOperationException("An ETL recording session is already running.");
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Select an ETL recording path before starting.", nameof(filePath));
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".");

        string sessionName = CreateSessionName(options);
        var session = new TraceEventSession(sessionName, filePath)
        {
            StopOnDispose = true
        };

        session.EnableProvider(
            options.ProviderId,
            (TraceEventLevel)options.Level,
            options.AnyKeyword,
            new TraceEventProviderOptions());

        _session = session;
        _filePath = filePath;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        TraceEventSession? session = _session;
        _session = null;
        if (session is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            session.Flush();
            session.Stop();
        }
        finally
        {
            session.Dispose();
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private static string CreateSessionName(EtwProviderEnableOptions options)
    {
        string providerName = new([.. options.ProviderName
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .Take(42)]);

        if (string.IsNullOrWhiteSpace(providerName))
        {
            providerName = "Provider";
        }

        return $"EtwSuite-Etl-{providerName}-{Guid.NewGuid():N}"[..64];
    }
}
