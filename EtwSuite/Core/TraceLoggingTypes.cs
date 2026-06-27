namespace EtwSuite.Core;

public enum TraceLoggingScanPathKind
{
    File,
    Folder
}

public enum TraceLoggingDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record TraceLoggingScanPath(
    string Path,
    TraceLoggingScanPathKind Kind);

public sealed record TraceLoggingScanDiagnostic(
    TraceLoggingDiagnosticSeverity Severity,
    string Message,
    string? Path = null);

public sealed record TraceLoggingEventSchema(
    string Name,
    byte Channel,
    byte Level,
    byte Opcode,
    ulong Keyword,
    IReadOnlyList<EtwSchemaParameter> Fields,
    string SourcePath);

public sealed record TraceLoggingProviderInfo(
    string Name,
    Guid? Id,
    Guid? GroupId,
    string SourcePath,
    IReadOnlyList<TraceLoggingEventSchema> Events,
    IReadOnlyList<TraceLoggingScanDiagnostic> Diagnostics,
    bool FromCache,
    long SourceLength = 0,
    DateTimeOffset SourceLastWriteTimeUtc = default)
{
    public EtwProviderInfo? ToEtwProviderInfo()
    {
        return Id is Guid providerId
            ? new EtwProviderInfo(Name, providerId, EtwProviderSchemaSource.TraceLogging)
            : null;
    }
}

public sealed record TraceLoggingScanResult(
    IReadOnlyList<TraceLoggingProviderInfo> Providers,
    IReadOnlyList<TraceLoggingScanDiagnostic> Diagnostics,
    int ScannerVersion = 0);

public interface ITraceLoggingProviderScanner
{
    Task<TraceLoggingScanResult> ScanAsync(
        IReadOnlyList<TraceLoggingScanPath> paths,
        bool useCache,
        CancellationToken cancellationToken);
}

public interface ITraceLoggingProviderCache
{
    Task<IReadOnlyList<TraceLoggingScanPath>> LoadConfiguredPathsAsync(CancellationToken cancellationToken);

    Task SaveConfiguredPathsAsync(
        IReadOnlyList<TraceLoggingScanPath> paths,
        CancellationToken cancellationToken);

    Task<TraceLoggingScanResult> LoadCachedResultAsync(CancellationToken cancellationToken);

    Task SaveCachedResultAsync(
        TraceLoggingScanResult result,
        CancellationToken cancellationToken);
}
