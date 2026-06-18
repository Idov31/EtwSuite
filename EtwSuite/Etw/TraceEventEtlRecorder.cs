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

        EtwTraceSessionDescriptor traceSession = EtwTraceSessionNameResolver.ResolveSession(options, "EtwSuite-Etl-", 42);
        if (!traceSession.CanStopSession)
        {
            throw new NotSupportedException(
                $"ETL recording is not supported for {traceSession.SessionName} because the session is owned by Windows and cannot be stopped or reconfigured.");
        }

        var session = new TraceEventSession(traceSession.SessionName, filePath)
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

}
