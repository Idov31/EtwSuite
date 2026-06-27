using System.Text.Json;
using EtwSuite.Core;

namespace EtwSuite.Etw.TraceLogging;

public sealed class FileTraceLoggingProviderCache : ITraceLoggingProviderCache
{
    private readonly string _settingsPath;
    private readonly string _cachePath;

    public FileTraceLoggingProviderCache()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EtwSuite",
                "settings.json"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EtwSuite",
                "tracelogging-cache.json"))
    {
    }

    public FileTraceLoggingProviderCache(string settingsPath, string cachePath)
    {
        _settingsPath = settingsPath;
        _cachePath = cachePath;
    }

    public async Task<IReadOnlyList<TraceLoggingScanPath>> LoadConfiguredPathsAsync(CancellationToken cancellationToken)
    {
        SettingsDto settings = await LoadSettingsAsync(cancellationToken);
        return settings.TraceLoggingPaths
            .Where(path => !string.IsNullOrWhiteSpace(path.Path))
            .Select(path => new TraceLoggingScanPath(path.Path, path.Kind))
            .ToArray();
    }

    public async Task SaveConfiguredPathsAsync(
        IReadOnlyList<TraceLoggingScanPath> paths,
        CancellationToken cancellationToken)
    {
        SettingsDto settings = await LoadSettingsAsync(cancellationToken);
        settings.TraceLoggingPaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path.Path))
            .Select(path => new TraceLoggingScanPathDto
            {
                Path = path.Path,
                Kind = path.Kind
            })
            .ToList();

        await SaveSettingsAsync(settings, cancellationToken);
    }

    public async Task<TraceLoggingScanResult> LoadCachedResultAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_cachePath))
        {
            return new TraceLoggingScanResult([], []);
        }

        await using FileStream stream = File.OpenRead(_cachePath);
        TraceLoggingScanResult? result = await JsonSerializer.DeserializeAsync<TraceLoggingScanResult>(
            stream,
            cancellationToken: cancellationToken);

        return result is null
            ? new TraceLoggingScanResult([], [])
            : result with
            {
                Providers = [.. result.Providers.Select(provider => provider with { FromCache = true })]
            };
    }

    public async Task SaveCachedResultAsync(
        TraceLoggingScanResult result,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath) ?? ".");
        TraceLoggingScanResult cacheResult = result with
        {
            Providers = [.. result.Providers.Select(provider => provider with { FromCache = false })]
        };

        await using FileStream stream = File.Create(_cachePath);
        await JsonSerializer.SerializeAsync(
            stream,
            cacheResult,
            new JsonSerializerOptions { WriteIndented = true },
            cancellationToken);
    }

    private async Task<SettingsDto> LoadSettingsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath))
        {
            return new SettingsDto();
        }

        await using FileStream stream = File.OpenRead(_settingsPath);
        return await JsonSerializer.DeserializeAsync<SettingsDto>(stream, cancellationToken: cancellationToken)
            ?? new SettingsDto();
    }

    private async Task SaveSettingsAsync(SettingsDto settings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath) ?? ".");
        await using FileStream stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(
            stream,
            settings,
            new JsonSerializerOptions { WriteIndented = true },
            cancellationToken);
    }

    private sealed class SettingsDto
    {
        public string? SavedSessionsDatabasePath { get; set; }

        public string? ThemeMode { get; set; }

        public List<TraceLoggingScanPathDto> TraceLoggingPaths { get; set; } = [];
    }

    private sealed class TraceLoggingScanPathDto
    {
        public string Path { get; set; } = string.Empty;

        public TraceLoggingScanPathKind Kind { get; set; }
    }
}
