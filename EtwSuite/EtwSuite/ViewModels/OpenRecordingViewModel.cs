using System.Collections.ObjectModel;
using EtwSuite.Core;

namespace EtwSuite.ViewModels;

public sealed class OpenRecordingViewModel : ObservableObject
{
    private const int MaxLoadedEvents = 10_000;
    private const int DefaultEventsPerPage = 100;
    private readonly IEtwRecordingReader _recordingReader;
    private readonly List<EtwLiveEventRecord> _eventBuffer = new();
    private readonly List<EtwLiveEventRecord> _filteredEvents = new();
    private string _eventFilterText = string.Empty;
    private EtwFilterMode _selectedEventFilterMode = EtwFilterMode.Basic;
    private string? _statusMessage = "Open an ETL, JSON, or CSV recording.";
    private string? _selectedFilePath;
    private bool _isLoading;
    private long _droppedEvents;
    private int _eventsPerPage = DefaultEventsPerPage;
    private int _currentPage = 1;

    public OpenRecordingViewModel(IEtwRecordingReader recordingReader)
    {
        _recordingReader = recordingReader;
    }

    public ObservableCollection<LiveEventViewModel> Events { get; } = new();

    public IReadOnlyList<EtwFilterMode> EventFilterModes { get; } = new[] { EtwFilterMode.Basic, EtwFilterMode.SQL };

    public string? SelectedFilePath
    {
        get => _selectedFilePath;
        private set => SetProperty(ref _selectedFilePath, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(CanOpenFile));
            }
        }
    }

    public bool CanOpenFile => !IsLoading;

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

    public string EventCountText => string.IsNullOrWhiteSpace(EventFilterText)
        ? $"{_eventBuffer.Count:N0} total events"
        : $"{_filteredEvents.Count:N0} matching, {_eventBuffer.Count:N0} total events";

    public string DroppedEventsText => _droppedEvents == 0
        ? string.Empty
        : $"{_droppedEvents:N0} events were not loaded because the view is capped at {MaxLoadedEvents:N0} events";

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

    public async Task OpenAsync(string filePath, CancellationToken cancellationToken)
    {
        IsLoading = true;
        StatusMessage = "Opening recording...";

        var loadedEvents = new List<EtwLiveEventRecord>();
        long droppedEvents = 0;

        try
        {
            await foreach (IReadOnlyList<EtwLiveEventRecord> batch in _recordingReader.ReadEventsAsync(
                filePath,
                500,
                cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (EtwLiveEventRecord record in batch)
                {
                    if (loadedEvents.Count < MaxLoadedEvents)
                    {
                        loadedEvents.Add(record);
                    }
                    else
                    {
                        droppedEvents++;
                    }
                }
            }

            _eventBuffer.Clear();
            _eventBuffer.AddRange(loadedEvents);
            _droppedEvents = droppedEvents;
            SelectedFilePath = filePath;
            _currentPage = 1;
            StatusMessage = $"Opened {Path.GetFileName(filePath)}.";
            ApplyEventFilter(resetPage: true);
            OnPropertyChanged(nameof(DroppedEventsText));
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Opening recording was canceled.";
        }
        catch (EtwRecordingException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (UnauthorizedAccessException)
        {
            StatusMessage = "EtwSuite does not have permission to read the selected recording.";
        }
        catch (IOException ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
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

    private void ApplyEventFilter(bool resetPage)
    {
        EtwCompiledFilter<EtwLiveEventRecord> eventFilter =
            EtwFilterCompiler.CompileEventFilter(SelectedEventFilterMode, EventFilterText);

        _filteredEvents.Clear();
        if (eventFilter.ErrorMessage is null)
        {
            _filteredEvents.AddRange(_eventBuffer.Where(eventFilter.Matches));
            ClearFilterError();
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
        OnPropertyChanged(nameof(EventCountText));
        OnPagingPropertiesChanged();
    }

    private void RefreshCurrentPage()
    {
        Events.Clear();
        int skip = (CurrentPage - 1) * EventsPerPage;
        foreach (EtwLiveEventRecord record in _filteredEvents.Skip(skip).Take(EventsPerPage))
        {
            Events.Add(new LiveEventViewModel(record));
        }
    }

    private void OnPagingPropertiesChanged()
    {
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(CanGoToPreviousPage));
        OnPropertyChanged(nameof(CanGoToNextPage));
        OnPropertyChanged(nameof(PageStatusText));
    }

    private void ClearFilterError()
    {
        if (StatusMessage?.StartsWith("Event filter:", StringComparison.Ordinal) == true)
        {
            StatusMessage = SelectedFilePath is null
                ? "Open an ETL, JSON, or CSV recording."
                : $"Opened {Path.GetFileName(SelectedFilePath)}.";
        }
    }
}
