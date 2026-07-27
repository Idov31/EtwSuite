using System.Collections.ObjectModel;
using EtwSuite.Core;

namespace EtwSuite.ViewModels;

public sealed class TraceLoggingProvidersViewModel : ObservableObject
{
    private readonly ITraceLoggingProviderScanner _scanner;
    private readonly ITraceLoggingProviderCache _cache;
    private IReadOnlyList<TraceLoggingProviderInfo> _allProviders = Array.Empty<TraceLoggingProviderInfo>();
    private TraceLoggingProviderViewModel? _selectedProvider;
    private TraceLoggingScanPathViewModel? _selectedScanPath;
    private string _providerSearchText = string.Empty;
    private string _schemaSearchText = string.Empty;
    private string? _statusMessage = "Configure files or folders to scan for static TraceLogging metadata.";
    private bool _isBusy;
    private bool _useCache = true;

    public TraceLoggingProvidersViewModel(
        ITraceLoggingProviderScanner scanner,
        ITraceLoggingProviderCache cache)
    {
        _scanner = scanner;
        _cache = cache;
    }

    public ObservableCollection<TraceLoggingScanPathViewModel> ScanPaths { get; } = new();

    public ObservableCollection<TraceLoggingProviderViewModel> Providers { get; } = new();

    public ObservableCollection<TraceLoggingEventViewModel> Events { get; } = new();

    public ObservableCollection<string> Diagnostics { get; } = new();

    public string ProviderSearchText
    {
        get => _providerSearchText;
        set
        {
            if (SetProperty(ref _providerSearchText, value))
            {
                ApplyProviderFilter();
            }
        }
    }

    public string SchemaSearchText
    {
        get => _schemaSearchText;
        set
        {
            if (SetProperty(ref _schemaSearchText, value))
            {
                RefreshSelectedProviderDetails();
            }
        }
    }

    public TraceLoggingProviderViewModel? SelectedProvider
    {
        get => _selectedProvider;
        set
        {
            if (SetProperty(ref _selectedProvider, value))
            {
                RefreshSelectedProviderDetails();
                OnPropertyChanged(nameof(CanConsumeSelectedProvider));
            }
        }
    }

    public TraceLoggingScanPathViewModel? SelectedScanPath
    {
        get => _selectedScanPath;
        set
        {
            if (SetProperty(ref _selectedScanPath, value))
            {
                OnPropertyChanged(nameof(CanRemovePath));
            }
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanRefresh));
            }
        }
    }

    public bool UseCache
    {
        get => _useCache;
        set => SetProperty(ref _useCache, value);
    }

    public bool CanRefresh => !IsBusy && ScanPaths.Count > 0;

    public bool CanRemovePath => SelectedScanPath is not null && !IsBusy;

    public bool CanConsumeSelectedProvider => SelectedProvider?.Provider.Id is not null;

    public string ProviderCountText => IsBusy
        ? "Scanning TraceLogging metadata..."
        : $"{Providers.Count:N0} TraceLogging providers";

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ScanPaths.Clear();
        foreach (TraceLoggingScanPath path in await _cache.LoadConfiguredPathsAsync(cancellationToken))
        {
            ScanPaths.Add(new TraceLoggingScanPathViewModel(path));
        }

        ApplyScanResult(await _cache.LoadCachedResultAsync(cancellationToken));
        OnPathPropertiesChanged();
    }

    public async Task AddPathAsync(
        string path,
        TraceLoggingScanPathKind kind,
        CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(path);
        if (ScanPaths.Any(candidate =>
            candidate.Kind == kind &&
            string.Equals(candidate.Path, fullPath, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedScanPath = ScanPaths.First(candidate =>
                candidate.Kind == kind &&
                string.Equals(candidate.Path, fullPath, StringComparison.OrdinalIgnoreCase));
            return;
        }

        var item = new TraceLoggingScanPathViewModel(new TraceLoggingScanPath(fullPath, kind));
        ScanPaths.Add(item);
        SelectedScanPath = item;
        await SavePathsAsync(cancellationToken);
        OnPathPropertiesChanged();
    }

    public async Task RemoveSelectedPathAsync(CancellationToken cancellationToken)
    {
        TraceLoggingScanPathViewModel? selected = SelectedScanPath;
        if (selected is null)
        {
            return;
        }

        int selectedIndex = ScanPaths.IndexOf(selected);
        ScanPaths.Remove(selected);
        SelectedScanPath = ScanPaths.Count == 0
            ? null
            : ScanPaths[Math.Clamp(selectedIndex, 0, ScanPaths.Count - 1)];

        await SavePathsAsync(cancellationToken);
        OnPathPropertiesChanged();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (IsBusy || ScanPaths.Count == 0)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = UseCache
            ? "Loading cached TraceLogging metadata or scanning changed files..."
            : "Scanning configured files for TraceLogging metadata...";
        OnPropertyChanged(nameof(ProviderCountText));

        try
        {
            TraceLoggingScanResult result = await _scanner.ScanAsync(
                [.. ScanPaths.Select(path => path.ToModel())],
                UseCache,
                cancellationToken);
            await _cache.SaveCachedResultAsync(result, cancellationToken);
            ApplyScanResult(result);
            StatusMessage = $"Found {Providers.Count:N0} TraceLogging providers.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(ProviderCountText));
        }
    }

    public EtwProviderInfo? GetSelectedEtwProvider()
    {
        return SelectedProvider?.Provider.ToEtwProviderInfo();
    }

    public void ReportError(string message)
    {
        StatusMessage = message;
    }

    private async Task SavePathsAsync(CancellationToken cancellationToken)
    {
        await _cache.SaveConfiguredPathsAsync(
            [.. ScanPaths.Select(path => path.ToModel())],
            cancellationToken);
    }

    private void ApplyScanResult(TraceLoggingScanResult result)
    {
        _allProviders = result.Providers;

        Diagnostics.Clear();
        foreach (TraceLoggingScanDiagnostic diagnostic in result.Diagnostics)
        {
            Diagnostics.Add(string.IsNullOrWhiteSpace(diagnostic.Path)
                ? $"{diagnostic.Severity}: {diagnostic.Message}"
                : $"{diagnostic.Severity}: {diagnostic.Message} ({diagnostic.Path})");
        }

        ApplyProviderFilter();
    }

    private void ApplyProviderFilter()
    {
        Guid? selectedProviderId = SelectedProvider?.Provider.Id;
        string? selectedProviderName = SelectedProvider?.Provider.Name;
        string searchText = ProviderSearchText.Trim();

        IEnumerable<TraceLoggingProviderInfo> filteredProviders = _allProviders;
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            filteredProviders = filteredProviders.Where(provider => MatchesProviderSearch(provider, searchText));
        }

        Providers.Clear();
        foreach (TraceLoggingProviderInfo provider in filteredProviders)
        {
            Providers.Add(new TraceLoggingProviderViewModel(provider));
        }

        SelectedProvider = Providers.FirstOrDefault(provider =>
                provider.Provider.Id == selectedProviderId ||
                string.Equals(provider.Provider.Name, selectedProviderName, StringComparison.OrdinalIgnoreCase))
            ?? Providers.FirstOrDefault();

        OnPropertyChanged(nameof(ProviderCountText));
    }

    private void RefreshSelectedProviderDetails()
    {
        Events.Clear();
        if (SelectedProvider is null)
        {
            return;
        }

        string searchText = SchemaSearchText.Trim();
        IEnumerable<TraceLoggingEventSchema> events = SelectedProvider.Provider.Events;
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            events = events.Where(schemaEvent => MatchesEventSearch(schemaEvent, searchText));
        }

        foreach (TraceLoggingEventSchema schemaEvent in events)
        {
            Events.Add(new TraceLoggingEventViewModel(schemaEvent));
        }
    }

    private static bool MatchesProviderSearch(TraceLoggingProviderInfo provider, string searchText)
    {
        return Contains(provider.Name, searchText) ||
            Contains(provider.Id?.ToString("D") ?? string.Empty, searchText) ||
            Contains(provider.GroupId?.ToString("D") ?? string.Empty, searchText) ||
            Contains(provider.SourcePath, searchText) ||
            Contains(Path.GetFileName(provider.SourcePath), searchText) ||
            Contains(provider.FromCache ? "Cached" : "Scanned", searchText) ||
            Contains($"{provider.Events.Count:N0} events", searchText);
    }

    private static bool MatchesEventSearch(TraceLoggingEventSchema schemaEvent, string searchText)
    {
        return Contains(schemaEvent.Name, searchText) ||
            Contains(schemaEvent.Level.ToString(), searchText) ||
            Contains(schemaEvent.Opcode.ToString(), searchText) ||
            Contains(schemaEvent.Channel.ToString(), searchText) ||
            Contains($"0x{schemaEvent.Keyword:X16}", searchText) ||
            Contains(schemaEvent.OwnershipConfidence.ToString(), searchText) ||
            schemaEvent.Fields.Any(field => Contains(field.Name, searchText) || Contains(field.Type, searchText));
    }

    private static bool Contains(string value, string searchText)
    {
        return value.Contains(searchText, StringComparison.CurrentCultureIgnoreCase);
    }

    private void OnPathPropertiesChanged()
    {
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanRemovePath));
    }
}

public sealed class TraceLoggingScanPathViewModel
{
    public TraceLoggingScanPathViewModel(TraceLoggingScanPath path)
    {
        Path = path.Path;
        Kind = path.Kind;
    }

    public string Path { get; }

    public TraceLoggingScanPathKind Kind { get; }

    public string KindText => Kind == TraceLoggingScanPathKind.File ? "File" : "Folder";

    public TraceLoggingScanPath ToModel()
    {
        return new TraceLoggingScanPath(Path, Kind);
    }
}

public sealed class TraceLoggingProviderViewModel
{
    public TraceLoggingProviderViewModel(TraceLoggingProviderInfo provider)
    {
        Provider = provider;
    }

    public TraceLoggingProviderInfo Provider { get; }

    public string Name => Provider.Name;

    public string IdText => Provider.Id?.ToString("D") ?? "Unknown";

    public string GroupIdText => Provider.GroupId?.ToString("D") ?? string.Empty;

    public string SourcePath => Provider.SourcePath;

    public string SourceFileName => string.IsNullOrWhiteSpace(SourcePath)
        ? string.Empty
        : Path.GetFileName(SourcePath);

    public string EventCountText => $"{Provider.Events.Count:N0} events";

    public string CacheStatus => Provider.FromCache ? "Cached" : "Scanned";
}

public sealed class TraceLoggingEventViewModel
{
    public TraceLoggingEventViewModel(TraceLoggingEventSchema schemaEvent)
    {
        Name = schemaEvent.Name;
        Channel = schemaEvent.Channel;
        Level = schemaEvent.Level;
        Opcode = schemaEvent.Opcode;
        Keyword = $"0x{schemaEvent.Keyword:X16}";
        Confidence = schemaEvent.OwnershipConfidence.ToString();
        Fields = new ObservableCollection<EtwSchemaParameter>(schemaEvent.Fields);
    }

    public string Name { get; }

    public byte Channel { get; }

    public byte Level { get; }

    public byte Opcode { get; }

    public string Keyword { get; }

    public string Confidence { get; }

    public ObservableCollection<EtwSchemaParameter> Fields { get; }

    public string FieldCountText => $"{Fields.Count:N0} fields";
}
