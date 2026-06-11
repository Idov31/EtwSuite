using System.Collections.ObjectModel;
using EtwSuite.Core;
using EtwSuite.Etw;
using Microsoft.UI.Dispatching;

namespace EtwSuite.ViewModels;

public sealed class ConsumeProviderViewModel : ObservableObject, IAsyncDisposable
{
    private const int MaxDisplayedEvents = 10_000;
    private readonly IEtwProviderCatalog _providerCatalog;
    private readonly DispatcherQueue _dispatcherQueue;
    private IReadOnlyList<EtwProviderInfo> _allProviders = Array.Empty<EtwProviderInfo>();
    private IEtwLiveEventConsumer? _consumer;
    private CancellationTokenSource? _consumeCancellation;
    private EtwProviderInfo? _selectedProvider;
    private string _searchText = string.Empty;
    private string? _statusMessage = "Select a provider to start consuming.";
    private EtwTraceSessionState _state = EtwTraceSessionState.Stopped;
    private long _droppedDisplayEvents;

    public ConsumeProviderViewModel(IEtwProviderCatalog providerCatalog)
    {
        _providerCatalog = providerCatalog;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public ObservableCollection<EtwProviderInfo> Providers { get; } = new();

    public ObservableCollection<LiveEventViewModel> Events { get; } = new();

    public IReadOnlyList<string> ExportFormats { get; } = new[] { "JSON", "CSV", "ETL", "EVTX" };

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

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
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
                OnPropertyChanged(nameof(StartStopText));
            }
        }
    }

    public bool CanStart => SelectedProvider is not null && State is EtwTraceSessionState.Stopped or EtwTraceSessionState.Failed;

    public bool CanStop => State == EtwTraceSessionState.Running;

    public string StartStopText => CanStop ? "Stop Consuming" : "Start Consuming";

    public string EventCountText => $"{Events.Count:N0} events";

    public string DroppedEventsText => _droppedDisplayEvents == 0
        ? string.Empty
        : $"{_droppedDisplayEvents:N0} older events dropped from view";

    public async Task LoadProvidersAsync(CancellationToken cancellationToken)
    {
        _allProviders = await _providerCatalog.EnumerateProvidersAsync(cancellationToken);
        ApplyFilter();
        SelectedProvider = Providers.FirstOrDefault();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!CanStart || SelectedProvider is null)
        {
            return;
        }

        State = EtwTraceSessionState.Starting;
        StatusMessage = "Starting trace session...";
        Events.Clear();
        _droppedDisplayEvents = 0;
        OnPropertyChanged(nameof(EventCountText));
        OnPropertyChanged(nameof(DroppedEventsText));

        _consumeCancellation?.Cancel();
        _consumeCancellation?.Dispose();
        _consumeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var consumer = new KrabsEtwLiveEventConsumer();
        _consumer = consumer;

        try
        {
            await consumer.StartAsync(
                new EtwProviderEnableOptions(SelectedProvider.Name, SelectedProvider.Id),
                _consumeCancellation.Token);

            State = EtwTraceSessionState.Running;
            StatusMessage = $"Consuming {SelectedProvider.Name}.";
            _ = DrainEventsAsync(consumer, _consumeCancellation.Token);
        }
        catch (Exception ex)
        {
            State = EtwTraceSessionState.Failed;
            StatusMessage = ex.Message;
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
            State = EtwTraceSessionState.Stopped;
            return;
        }

        State = EtwTraceSessionState.Stopping;
        StatusMessage = "Stopping trace session...";
        _consumeCancellation?.Cancel();

        IEtwLiveEventConsumer consumer = _consumer;
        _consumer = null;
        await consumer.DisposeAsync();

        State = EtwTraceSessionState.Stopped;
        StatusMessage = "Trace session stopped.";
    }

    public async ValueTask DisposeAsync()
    {
        _consumeCancellation?.Cancel();
        if (_consumer is not null)
        {
            await _consumer.DisposeAsync();
            _consumer = null;
        }

        _consumeCancellation?.Dispose();
    }

    private async Task DrainEventsAsync(
        IEtwLiveEventConsumer consumer,
        CancellationToken cancellationToken)
    {
        var batch = new List<EtwLiveEventRecord>(250);
        try
        {
            await foreach (EtwLiveEventRecord record in consumer.Events.ReadAllAsync(cancellationToken))
            {
                batch.Add(record);
                if (batch.Count >= 250)
                {
                    FlushBatch(batch);
                    batch.Clear();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (batch.Count > 0)
            {
                FlushBatch(batch);
            }
        }
    }

    private void FlushBatch(IReadOnlyList<EtwLiveEventRecord> records)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            foreach (EtwLiveEventRecord record in records)
            {
                Events.Add(new LiveEventViewModel(record));
            }

            while (Events.Count > MaxDisplayedEvents)
            {
                Events.RemoveAt(0);
                _droppedDisplayEvents++;
            }

            OnPropertyChanged(nameof(EventCountText));
            OnPropertyChanged(nameof(DroppedEventsText));
        });
    }

    private void ApplyFilter()
    {
        EtwProviderInfo? previousSelection = SelectedProvider;
        string searchText = SearchText.Trim();

        IEnumerable<EtwProviderInfo> filteredProviders = _allProviders;
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            filteredProviders = filteredProviders.Where(provider =>
                provider.Name.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
                provider.Id.ToString("D").Contains(searchText, StringComparison.OrdinalIgnoreCase));
        }

        Providers.Clear();
        foreach (EtwProviderInfo provider in filteredProviders.Take(500))
        {
            Providers.Add(provider);
        }

        SelectedProvider = previousSelection is not null && Providers.Contains(previousSelection)
            ? previousSelection
            : Providers.FirstOrDefault();
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

