using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using EtwSuite.Core;
using EtwSuite.Etw;
using Microsoft.UI.Dispatching;

namespace EtwSuite.ViewModels;

public sealed class ConsumeProviderViewModel : ObservableObject, IAsyncDisposable
{
    private const int MaxDisplayedEvents = 10_000;
    private const int DefaultEventsPerPage = 100;
    private const int LiveEventFlushBatchSize = 10;
    private static readonly TimeSpan LiveEventFlushInterval = TimeSpan.FromSeconds(1);
    private readonly IEtwProviderCatalog _providerCatalog;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly List<EtwLiveEventRecord> _eventBuffer = new();
    private readonly List<EtwLiveEventRecord> _filteredEvents = new();
    private IReadOnlyList<EtwProviderInfo> _allProviders = Array.Empty<EtwProviderInfo>();
    private IEtwLiveEventConsumer? _consumer;
    private TraceEventEtlRecorder? _etlRecorder;
    private CancellationTokenSource? _consumeCancellation;
    private EtwProviderInfo? _selectedProvider;
    private EtwProviderInfo? _activeProvider;
    private string _searchText = string.Empty;
    private string _eventFilterText = string.Empty;
    private EtwFilterMode _selectedEventFilterMode = EtwFilterMode.Basic;
    private string? _statusMessage = "Select a provider to start consuming.";
    private EtwTraceSessionState _state = EtwTraceSessionState.Stopped;
    private long _droppedDisplayEvents;
    private int _eventsPerPage = DefaultEventsPerPage;
    private int _currentPage = 1;
    private string _selectedExportFormat = "JSON";
    private bool _isEtlRecordingEnabled;
    private string? _etlRecordingPath;
    private string? _lastRecordedEtlPath;
    private LiveEventSortField _sortField = LiveEventSortField.Time;
    private LiveEventSortDirection _sortDirection = LiveEventSortDirection.Ascending;

    public ConsumeProviderViewModel(IEtwProviderCatalog providerCatalog)
    {
        _providerCatalog = providerCatalog;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public ObservableCollection<EtwProviderInfo> Providers { get; } = new();

    public ObservableCollection<LiveEventViewModel> Events { get; } = new();

    public IReadOnlyList<EtwFilterMode> EventFilterModes { get; } = new[] { EtwFilterMode.Basic, EtwFilterMode.SQL };

    public IReadOnlyList<string> ExportFormats { get; } = new[] { "JSON", "CSV", "ETL" };

    public LiveEventSortField SortField => _sortField;

    public LiveEventSortDirection SortDirection => _sortDirection;

    public string TimeSortHeader => CreateSortHeader("Time", LiveEventSortField.Time);

    public string ProviderSortHeader => CreateSortHeader("Provider", LiveEventSortField.Provider);

    public string EventSortHeader => CreateSortHeader("Event", LiveEventSortField.Event);

    public string IdSortHeader => CreateSortHeader("ID", LiveEventSortField.Id);

    public string VersionSortHeader => CreateSortHeader("Version", LiveEventSortField.Version);

    public string OpcodeSortHeader => CreateSortHeader("Opcode", LiveEventSortField.Opcode);

    public string LevelSortHeader => CreateSortHeader("Level", LiveEventSortField.Level);

    public string ProcessIdSortHeader => CreateSortHeader("PID", LiveEventSortField.ProcessId);

    public string ProcessNameSortHeader => CreateSortHeader("Process", LiveEventSortField.ProcessName);

    public string ThreadIdSortHeader => CreateSortHeader("TID", LiveEventSortField.ThreadId);

    public string ParametersSortHeader => CreateSortHeader("Parameters", LiveEventSortField.Parameters);

    public string SelectedExportFormat
    {
        get => _selectedExportFormat;
        set
        {
            if (SetProperty(ref _selectedExportFormat, value))
            {
                OnPropertyChanged(nameof(CanExport));
            }
        }
    }

    public bool IsEtlRecordingEnabled
    {
        get => _isEtlRecordingEnabled;
        set
        {
            if (SetProperty(ref _isEtlRecordingEnabled, value))
            {
                OnPropertyChanged(nameof(EtlRecordingText));
            }
        }
    }

    public string? EtlRecordingPath
    {
        get => _etlRecordingPath;
        set
        {
            if (SetProperty(ref _etlRecordingPath, value))
            {
                OnPropertyChanged(nameof(EtlRecordingText));
            }
        }
    }

    public string? LastRecordedEtlPath
    {
        get => _lastRecordedEtlPath;
        private set
        {
            if (SetProperty(ref _lastRecordedEtlPath, value))
            {
                OnPropertyChanged(nameof(CanOpenLastRecording));
                OnPropertyChanged(nameof(CanExport));
                OnPropertyChanged(nameof(EtlRecordingText));
            }
        }
    }

    public string EtlRecordingText => IsEtlRecordingEnabled
        ? string.IsNullOrWhiteSpace(EtlRecordingPath)
            ? "ETL recording enabled"
            : State == EtwTraceSessionState.Running
                ? $"Recording to {EtlRecordingPath}"
                : LastRecordedEtlPath is not null
                    ? $"Recorded to {LastRecordedEtlPath}"
                    : $"ETL path: {EtlRecordingPath}"
        : "ETL recording disabled";

    public bool CanOpenLastRecording => !string.IsNullOrWhiteSpace(LastRecordedEtlPath) && File.Exists(LastRecordedEtlPath);

    public EtwProviderInfo? SelectedProvider
    {
        get => _selectedProvider;
        set
        {
            if (SetProperty(ref _selectedProvider, value))
            {
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(SelectedProviderText));
            }
        }
    }

    public string SelectedProviderText => SelectedProvider is null
        ? "No provider selected"
        : $"{SelectedProvider.Name} ({SelectedProvider.Id:D})";

    public EtwProviderInfo? ActiveProvider
    {
        get => _activeProvider;
        private set
        {
            if (SetProperty(ref _activeProvider, value))
            {
                OnPropertyChanged(nameof(ActiveProviderText));
            }
        }
    }

    public string ActiveProviderText => ActiveProvider is null
        ? "No active provider"
        : $"{ActiveProvider.Name} ({ActiveProvider.Id:D})";

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyProviderFilter();
            }
        }
    }

    public string EventFilterText
    {
        get => _eventFilterText;
        set
        {
            if (SetProperty(ref _eventFilterText, value))
            {
                ApplyEventFilter(resetPage: true);
            }
        }
    }

    public EtwFilterMode SelectedEventFilterMode
    {
        get => _selectedEventFilterMode;
        set
        {
            if (SetProperty(ref _selectedEventFilterMode, value))
            {
                ApplyEventFilter(resetPage: true);
            }
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public EtwTraceSessionState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanStop));
                OnPropertyChanged(nameof(EtlRecordingText));
            }
        }
    }

    public bool CanStart =>
        SelectedProvider is not null &&
        State is EtwTraceSessionState.Stopped or EtwTraceSessionState.Running or EtwTraceSessionState.Failed;

    public bool CanStop => State == EtwTraceSessionState.Running;

    public string EventCountText => $"{Events.Count:N0} events";
    public string TotalEventCountText => string.IsNullOrWhiteSpace(EventFilterText)
        ? $"{_eventBuffer.Count:N0} total events"
        : $"{_filteredEvents.Count:N0} matching, {_eventBuffer.Count:N0} total events";

    public string DroppedEventsText => _droppedDisplayEvents == 0
        ? string.Empty
        : $"{_droppedDisplayEvents:N0} older events dropped from view";

    public int EventsPerPage
    {
        get => _eventsPerPage;
        set
        {
            int normalizedValue = Math.Clamp(value, 25, 1_000);
            if (SetProperty(ref _eventsPerPage, normalizedValue))
            {
                CurrentPage = Math.Min(CurrentPage, TotalPages);
                RefreshCurrentPage();
                OnPagingPropertiesChanged();
            }
        }
    }

    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            int normalizedValue = Math.Clamp(value, 1, TotalPages);
            if (SetProperty(ref _currentPage, normalizedValue))
            {
                RefreshCurrentPage();
                OnPagingPropertiesChanged();
            }
        }
    }

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(_filteredEvents.Count / (double)EventsPerPage));

    public bool CanGoToPreviousPage => CurrentPage > 1;

    public bool CanGoToNextPage => CurrentPage < TotalPages;

    public string PageStatusText => $"Page {CurrentPage:N0} of {TotalPages:N0}";

    public bool HasEvents => _filteredEvents.Count > 0;

    public bool CanClearEvents =>
        _eventBuffer.Count > 0 ||
        _filteredEvents.Count > 0 ||
        Events.Count > 0 ||
        _droppedDisplayEvents > 0;

    public bool CanExport => HasEvents ||
        (string.Equals(SelectedExportFormat, "ETL", StringComparison.OrdinalIgnoreCase) && CanOpenLastRecording);

    public async Task LoadProvidersAsync(CancellationToken cancellationToken)
    {
        _allProviders = await _providerCatalog.EnumerateProvidersAsync(cancellationToken);
        ApplyProviderFilter();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await StartAsync(null, cancellationToken);
    }

    public async Task StartAsync(string? etlRecordingPath, CancellationToken cancellationToken)
    {
        if (!CanStart || SelectedProvider is null || State is EtwTraceSessionState.Starting or EtwTraceSessionState.Stopping)
        {
            return;
        }

        EtwProviderInfo providerToStart = SelectedProvider;
        if (_consumer is not null)
        {
            await StopAsync();
        }

        State = EtwTraceSessionState.Starting;
        StatusMessage = "Starting trace session...";
        ClearEventsCore(updateStatus: false);

        _consumeCancellation?.Cancel();
        _consumeCancellation?.Dispose();
        _consumeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var consumer = new KrabsEtwLiveEventConsumer();
        TraceEventEtlRecorder? etlRecorder = null;
        _consumer = consumer;

        try
        {
            var enableOptions = new EtwProviderEnableOptions(providerToStart.Name, providerToStart.Id);
            if (IsEtlRecordingEnabled)
            {
                string? path = !string.IsNullOrWhiteSpace(etlRecordingPath)
                    ? etlRecordingPath
                    : EtlRecordingPath;

                if (string.IsNullOrWhiteSpace(path))
                {
                    throw new InvalidOperationException("Select an ETL recording path before starting.");
                }

                etlRecorder = new TraceEventEtlRecorder();
                await etlRecorder.StartAsync(enableOptions, path, _consumeCancellation.Token);
                _etlRecorder = etlRecorder;
                EtlRecordingPath = path;
                LastRecordedEtlPath = null;
            }

            await consumer.StartAsync(
                enableOptions,
                _consumeCancellation.Token);

            ActiveProvider = providerToStart;
            State = EtwTraceSessionState.Running;
            StatusMessage = GetRunningStatusMessage();
            _ = DrainEventsAsync(consumer, _consumeCancellation.Token);
        }
        catch (Exception ex)
        {
            ActiveProvider = null;
            State = EtwTraceSessionState.Failed;
            StatusMessage = ex.Message;
            if (etlRecorder is not null)
            {
                await etlRecorder.DisposeAsync();
                if (_etlRecorder == etlRecorder)
                {
                    _etlRecorder = null;
                }
            }

            await consumer.DisposeAsync();
            if (_consumer == consumer)
            {
                _consumer = null;
            }
        }
    }

    public async Task StopAsync()
    {
        if (_consumer is null)
        {
            ActiveProvider = null;
            State = EtwTraceSessionState.Stopped;
            return;
        }

        State = EtwTraceSessionState.Stopping;
        StatusMessage = "Stopping trace session...";
        _consumeCancellation?.Cancel();

        IEtwLiveEventConsumer consumer = _consumer;
        TraceEventEtlRecorder? etlRecorder = _etlRecorder;
        _consumer = null;
        _etlRecorder = null;
        ActiveProvider = null;
        await consumer.DisposeAsync();
        if (etlRecorder is not null)
        {
            await etlRecorder.DisposeAsync();
            LastRecordedEtlPath = etlRecorder.FilePath;
        }

        State = EtwTraceSessionState.Stopped;
        StatusMessage = CanOpenLastRecording
            ? $"Trace session stopped. ETL recorded to {LastRecordedEtlPath}."
            : "Trace session stopped.";
    }

    public async ValueTask DisposeAsync()
    {
        _consumeCancellation?.Cancel();
        if (_consumer is not null)
        {
            await _consumer.DisposeAsync();
            _consumer = null;
        }

        if (_etlRecorder is not null)
        {
            await _etlRecorder.DisposeAsync();
            _etlRecorder = null;
        }

        ActiveProvider = null;
        _consumeCancellation?.Dispose();
    }

    private async Task DrainEventsAsync(
        IEtwLiveEventConsumer consumer,
        CancellationToken cancellationToken)
    {
        await LiveEventBatchReader.DrainBatchesAsync(
            consumer.Events,
            LiveEventFlushBatchSize,
            LiveEventFlushInterval,
            batch =>
            {
                FlushBatch(consumer, batch);
                return Task.CompletedTask;
            },
            cancellationToken);
    }

    private void FlushBatch(IEtwLiveEventConsumer consumer, IReadOnlyList<EtwLiveEventRecord> records)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(_consumer, consumer))
            {
                return;
            }

            bool wasOnLastPage = CurrentPage == TotalPages;
            EtwCompiledFilter<EtwLiveEventRecord> eventFilter =
                EtwFilterCompiler.CompileEventFilter(SelectedEventFilterMode, EventFilterText);

            foreach (EtwLiveEventRecord record in records)
            {
                _eventBuffer.Add(record);
                if (eventFilter.ErrorMessage is null && eventFilter.Matches(record))
                {
                    _filteredEvents.Add(record);
                }
            }

            while (_eventBuffer.Count > MaxDisplayedEvents)
            {
                EtwLiveEventRecord droppedRecord = _eventBuffer[0];
                _eventBuffer.RemoveAt(0);
                _filteredEvents.Remove(droppedRecord);
                _droppedDisplayEvents++;
            }

            if (eventFilter.ErrorMessage is null)
            {
                ClearFilterError("Event filter:");
            }
            else
            {
                StatusMessage = $"Event filter: {eventFilter.ErrorMessage}";
            }

            if (CurrentPage > TotalPages)
            {
                _currentPage = TotalPages;
                OnPropertyChanged(nameof(CurrentPage));
            }

            if (wasOnLastPage)
            {
                _currentPage = TotalPages;
                OnPropertyChanged(nameof(CurrentPage));
            }

            RefreshCurrentPage();
            OnEventDisplayPropertiesChanged();
        });
    }

    public void SelectFirstMatchingProvider()
    {
        SelectedProvider = Providers.FirstOrDefault();
    }

    public void SelectProvider(EtwProviderInfo provider)
    {
        SearchText = provider.Name;
        EtwProviderInfo? matchingProvider = Providers.FirstOrDefault(candidate => candidate.Id == provider.Id);
        if (matchingProvider is null)
        {
            Providers.Insert(0, provider);
            matchingProvider = provider;
        }

        SelectedProvider = matchingProvider;
    }

    public void ApplySavedEventFilter(EtwFilterMode filterMode, string filterText)
    {
        SelectedEventFilterMode = filterMode;
        EventFilterText = filterText;
    }

    public void SortBy(LiveEventSortField sortField)
    {
        if (_sortField == sortField)
        {
            _sortDirection = _sortDirection == LiveEventSortDirection.Ascending
                ? LiveEventSortDirection.Descending
                : LiveEventSortDirection.Ascending;
        }
        else
        {
            _sortField = sortField;
            _sortDirection = LiveEventSortDirection.Ascending;
        }

        _currentPage = 1;
        OnPropertyChanged(nameof(CurrentPage));
        OnSortPropertiesChanged();
        RefreshCurrentPage();
        OnPagingPropertiesChanged();
    }

    public void ClearEvents()
    {
        ClearEventsCore(updateStatus: true);
    }

    public IReadOnlyList<LiveEventViewModel> GetEventSnapshot()
    {
        return GetSortedFilteredEvents()
            .Select(record => new LiveEventViewModel(record))
            .ToArray();
    }

    public string CreateExportContent()
    {
        IReadOnlyList<LiveEventViewModel> events = GetEventSnapshot();
        if (events.Count == 0)
        {
            StatusMessage = "There are no events to export.";
            return string.Empty;
        }

        string format = SelectedExportFormat.ToUpperInvariant();
        return format switch
        {
            "JSON" => CreateJson(events),
            "CSV" => CreateCsv(events),
            "ETL" => string.Empty,
            _ => throw new NotSupportedException($"{SelectedExportFormat} export is not supported yet."),
        };
    }

    public void ReportExported()
    {
        int eventCount = GetEventSnapshot().Count;
        if (eventCount > 0)
        {
            StatusMessage = $"Exported {eventCount:N0} events to {SelectedExportFormat}.";
        }
    }

    public string GetExportFileExtension()
    {
        string format = SelectedExportFormat.ToUpperInvariant();
        return format switch
        {
            "JSON" => ".json",
            "CSV" => ".csv",
            "ETL" => ".etl",
            _ => throw new NotSupportedException($"{SelectedExportFormat} export is not supported yet."),
        };
    }

    public string GetDefaultExportFileName()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        string providerName = !string.IsNullOrWhiteSpace(SelectedProvider?.Name)
            ? SanitizeFileNameComponent(SelectedProvider.Name)
            : string.Empty;

        string providerToken = string.IsNullOrWhiteSpace(providerName)
            ? (SelectedProvider?.Id ?? Guid.Empty).ToString("D")
            : providerName;

        return $"{providerToken}_{now:yyyyMMdd}_{now:HH}_{now:mm}{GetExportFileExtension()}";
    }

    public string GetDefaultEtlRecordingFileName()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        string providerName = !string.IsNullOrWhiteSpace(SelectedProvider?.Name)
            ? SanitizeFileNameComponent(SelectedProvider.Name)
            : string.Empty;

        string providerToken = string.IsNullOrWhiteSpace(providerName)
            ? (SelectedProvider?.Id ?? Guid.Empty).ToString("D")
            : providerName;

        return $"{providerToken}_{now:yyyyMMdd}_{now:HH}_{now:mm}.etl";
    }

    public async Task ExportAsync(string filePath, CancellationToken cancellationToken)
    {
        if (string.Equals(SelectedExportFormat, "ETL", StringComparison.OrdinalIgnoreCase))
        {
            if (!CanOpenLastRecording || string.IsNullOrWhiteSpace(LastRecordedEtlPath))
            {
                StatusMessage = "ETL export requires recording to ETL while consuming.";
                return;
            }

            await using FileStream source = File.OpenRead(LastRecordedEtlPath);
            await using FileStream destination = File.Create(filePath);
            await source.CopyToAsync(destination, cancellationToken);
            StatusMessage = $"Exported ETL recording to {filePath}.";
            return;
        }

        string content = CreateExportContent();
        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        await File.WriteAllTextAsync(filePath, content, Encoding.UTF8, cancellationToken);
        ReportExported();
    }

    public void ReportError(string message)
    {
        StatusMessage = message;
    }

    public void GoToPreviousPage()
    {
        if (CanGoToPreviousPage)
        {
            CurrentPage--;
        }
    }

    public void GoToNextPage()
    {
        if (CanGoToNextPage)
        {
            CurrentPage++;
        }
    }

    private IReadOnlyList<EtwLiveEventRecord> GetSortedFilteredEvents()
    {
        return LiveEventSorter.Sort(_filteredEvents, SortField, SortDirection);
    }

    private void ClearEventsCore(bool updateStatus)
    {
        _eventBuffer.Clear();
        _filteredEvents.Clear();
        Events.Clear();
        _currentPage = 1;
        _droppedDisplayEvents = 0;
        OnPropertyChanged(nameof(CurrentPage));
        OnEventDisplayPropertiesChanged();

        if (!updateStatus)
        {
            return;
        }

        StatusMessage = State == EtwTraceSessionState.Running
            ? $"Cleared events. {GetRunningStatusMessage()}"
            : "Cleared events.";
    }

    private void RefreshCurrentPage()
    {
        Events.Clear();

        int skip = (CurrentPage - 1) * EventsPerPage;
        foreach (EtwLiveEventRecord record in GetSortedFilteredEvents().Skip(skip).Take(EventsPerPage))
        {
            Events.Add(new LiveEventViewModel(record));
        }

        OnPropertyChanged(nameof(EventCountText));
    }

    private void OnEventDisplayPropertiesChanged()
    {
        OnPropertyChanged(nameof(EventCountText));
        OnPropertyChanged(nameof(TotalEventCountText));
        OnPropertyChanged(nameof(HasEvents));
        OnPropertyChanged(nameof(CanClearEvents));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(DroppedEventsText));
        OnPagingPropertiesChanged();
    }

    private void OnPagingPropertiesChanged()
    {
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(CanGoToPreviousPage));
        OnPropertyChanged(nameof(CanGoToNextPage));
        OnPropertyChanged(nameof(PageStatusText));
    }

    private string CreateSortHeader(string label, LiveEventSortField sortField)
    {
        if (SortField != sortField)
        {
            return label;
        }

        string sortGlyph = SortDirection == LiveEventSortDirection.Ascending ? "^" : "v";
        return $"{label} {sortGlyph}";
    }

    private void OnSortPropertiesChanged()
    {
        OnPropertyChanged(nameof(SortField));
        OnPropertyChanged(nameof(SortDirection));
        OnPropertyChanged(nameof(TimeSortHeader));
        OnPropertyChanged(nameof(ProviderSortHeader));
        OnPropertyChanged(nameof(EventSortHeader));
        OnPropertyChanged(nameof(IdSortHeader));
        OnPropertyChanged(nameof(VersionSortHeader));
        OnPropertyChanged(nameof(OpcodeSortHeader));
        OnPropertyChanged(nameof(LevelSortHeader));
        OnPropertyChanged(nameof(ProcessIdSortHeader));
        OnPropertyChanged(nameof(ProcessNameSortHeader));
        OnPropertyChanged(nameof(ThreadIdSortHeader));
        OnPropertyChanged(nameof(ParametersSortHeader));
    }

    private string GetRunningStatusMessage()
    {
        if (ActiveProvider is null)
        {
            return "Consuming provider.";
        }

        return IsEtlRecordingEnabled && !string.IsNullOrWhiteSpace(EtlRecordingPath)
            ? $"Consuming {ActiveProvider.Name} and recording ETL."
            : $"Consuming {ActiveProvider.Name}.";
    }

    private static string CreateJson(IReadOnlyList<LiveEventViewModel> events)
    {
        var exportEvents = events.Select(liveEvent => new
        {
            liveEvent.Time,
            liveEvent.Provider,
            liveEvent.Event,
            liveEvent.Id,
            liveEvent.Version,
            liveEvent.Opcode,
            liveEvent.Level,
            liveEvent.ProcessId,
            liveEvent.ProcessName,
            liveEvent.ThreadId,
            Parameters = liveEvent.Parameters.Select(parameter => new
            {
                parameter.Name,
                parameter.Type,
                parameter.Value,
            }),
        });

        return JsonSerializer.Serialize(
            exportEvents,
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static string CreateCsv(IReadOnlyList<LiveEventViewModel> events)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Time,Provider,Event,Id,Version,Opcode,Level,ProcessId,ProcessName,ThreadId,Parameters");

        foreach (LiveEventViewModel liveEvent in events)
        {
            string parameters = string.Join(
                "; ",
                liveEvent.Parameters.Select(parameter => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{parameter.Name}={parameter.Value}")));

            builder
                .Append(Csv(liveEvent.Time)).Append(',')
                .Append(Csv(liveEvent.Provider)).Append(',')
                .Append(Csv(liveEvent.Event)).Append(',')
                .Append(liveEvent.Id.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(liveEvent.Version.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(liveEvent.Opcode.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(liveEvent.Level.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(liveEvent.ProcessId.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Csv(liveEvent.ProcessName)).Append(',')
                .Append(liveEvent.ThreadId.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Csv(parameters))
                .AppendLine();
        }

        return builder.ToString();
    }

    private static string Csv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\r') && !value.Contains('\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string SanitizeFileNameComponent(string value)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (char character in value.Trim())
        {
            builder.Append(invalidChars.Contains(character) ? '_' : character);
        }

        return builder.ToString().Trim();
    }

    private void ApplyProviderFilter()
    {
        EtwProviderInfo? previousSelection = SelectedProvider;
        EtwCompiledFilter<EtwProviderInfo> searchFilter =
            EtwFilterCompiler.CompileProviderFilter(EtwFilterMode.Basic, SearchText);

        IEnumerable<EtwProviderInfo> filteredProviders = _allProviders;
        filteredProviders = filteredProviders.Where(searchFilter.Matches);

        Providers.Clear();
        foreach (EtwProviderInfo provider in filteredProviders.Take(500))
        {
            Providers.Add(provider);
        }

        SelectedProvider = previousSelection is not null &&
            Providers.Contains(previousSelection) &&
            IsExactProviderSearch(previousSelection)
            ? previousSelection
            : null;
    }

    private bool IsExactProviderSearch(EtwProviderInfo provider)
    {
        string searchText = SearchText.Trim();
        return string.Equals(searchText, provider.Name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(searchText, provider.Id.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(searchText.Trim('{', '}'), provider.Id.ToString("D"), StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyEventFilter(bool resetPage)
    {
        EtwCompiledFilter<EtwLiveEventRecord> eventFilter =
            EtwFilterCompiler.CompileEventFilter(SelectedEventFilterMode, EventFilterText);

        _filteredEvents.Clear();
        if (eventFilter.ErrorMessage is null)
        {
            _filteredEvents.AddRange(_eventBuffer.Where(eventFilter.Matches));
            ClearFilterError("Event filter:");
        }
        else
        {
            StatusMessage = $"Event filter: {eventFilter.ErrorMessage}";
        }

        if (resetPage)
        {
            _currentPage = 1;
            OnPropertyChanged(nameof(CurrentPage));
        }
        else if (CurrentPage > TotalPages)
        {
            _currentPage = TotalPages;
            OnPropertyChanged(nameof(CurrentPage));
        }

        RefreshCurrentPage();
        OnEventDisplayPropertiesChanged();
    }

    private void ClearFilterError(string prefix)
    {
        if (StatusMessage?.StartsWith(prefix, StringComparison.Ordinal) != true)
        {
            return;
        }

        StatusMessage = State == EtwTraceSessionState.Running
            ? GetRunningStatusMessage()
            : "Select a provider to start consuming.";
    }
}

public sealed class LiveEventViewModel
{
    public LiveEventViewModel(EtwLiveEventRecord record)
    {
        Time = record.ConsumedAt.ToString("HH:mm:ss.fff");
        Provider = record.ProviderName;
        Event = record.EventName;
        Id = record.EventId;
        Version = record.Version;
        Opcode = record.Opcode;
        Level = record.Level;
        ProcessId = record.ProcessId;
        ProcessName = record.ProcessName;
        ThreadId = record.ThreadId;
        Parameters = new ObservableCollection<LivePayloadValueViewModel>(
            record.Payload.Select(payload => new LivePayloadValueViewModel(payload)));
    }

    public string Time { get; }

    public string Provider { get; }

    public string Event { get; }

    public ushort Id { get; }

    public byte Version { get; }

    public byte Opcode { get; }

    public byte Level { get; }

    public uint ProcessId { get; }

    public string ProcessName { get; }

    public uint ThreadId { get; }

    public ObservableCollection<LivePayloadValueViewModel> Parameters { get; }

    public string ParameterSummary => Parameters.Count == 0
        ? "0 parameters"
        : $"{Parameters.Count:N0} parameters";
}

public sealed class LivePayloadValueViewModel
{
    public LivePayloadValueViewModel(EtwPayloadValue payload)
    {
        Name = payload.Name;
        Type = payload.Type;
        Value = payload.Value;
    }

    public string Name { get; }

    public string Type { get; }

    public string Value { get; }
}
