using System.Collections.ObjectModel;
using EtwSuite.Core;
using Microsoft.Data.Sqlite;

namespace EtwSuite.ViewModels;

public sealed class SavedSessionsViewModel : ObservableObject
{
    private readonly IEtwSessionTemplateStore _templateStore;
    private readonly IEtwSessionTemplateSettings _settings;
    private readonly ConsumeProviderViewModel _consumeProviderViewModel;
    private SavedSessionTemplateViewModel? _selectedTemplate;
    private string? _activeDatabasePath;
    private string? _statusMessage = "Create or open a saved sessions database.";
    private bool _isBusy;

    public SavedSessionsViewModel(
        IEtwSessionTemplateStore templateStore,
        IEtwSessionTemplateSettings settings,
        ConsumeProviderViewModel consumeProviderViewModel)
    {
        _templateStore = templateStore;
        _settings = settings;
        _consumeProviderViewModel = consumeProviderViewModel;
        _consumeProviderViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ConsumeProviderViewModel.SelectedProvider) or nameof(ConsumeProviderViewModel.State))
            {
                OnActionPropertiesChanged();
            }
        };
    }

    public ObservableCollection<SavedSessionTemplateViewModel> Templates { get; } = new();

    public SavedSessionTemplateViewModel? SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (SetProperty(ref _selectedTemplate, value))
            {
                OnActionPropertiesChanged();
            }
        }
    }

    public string? ActiveDatabasePath
    {
        get => _activeDatabasePath;
        private set
        {
            if (SetProperty(ref _activeDatabasePath, value))
            {
                OnPropertyChanged(nameof(HasDatabase));
                OnPropertyChanged(nameof(DatabasePathText));
                OnActionPropertiesChanged();
            }
        }
    }

    public string DatabasePathText => HasDatabase
        ? ActiveDatabasePath ?? string.Empty
        : "No saved sessions database selected";

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
                OnActionPropertiesChanged();
            }
        }
    }

    public bool HasDatabase => !string.IsNullOrWhiteSpace(ActiveDatabasePath);

    public bool CanSaveCurrentSession =>
        HasDatabase && !IsBusy && _consumeProviderViewModel.SelectedProvider is not null;

    public bool CanLoadSelectedSession =>
        HasDatabase && !IsBusy && SelectedTemplate is not null && !_consumeProviderViewModel.CanStop;

    public bool CanDeleteSelectedSession => HasDatabase && !IsBusy && SelectedTemplate is not null;

    public string DefaultTemplateName
    {
        get
        {
            string providerName = _consumeProviderViewModel.SelectedProvider?.Name ?? "Session";
            return $"{providerName} {DateTimeOffset.Now:yyyy-MM-dd HH-mm}";
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            string? rememberedPath = await _settings.LoadDatabasePathAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(rememberedPath))
            {
                return;
            }

            if (!File.Exists(rememberedPath))
            {
                StatusMessage = "The remembered saved sessions database could not be found. Create or open a database.";
                return;
            }

            await OpenDatabaseAsync(rememberedPath, remember: false, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    public void ReportError(string message)
    {
        StatusMessage = message;
    }

    public async Task OpenDatabaseAsync(string databasePath, bool remember, CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            await _templateStore.InitializeAsync(databasePath, cancellationToken);
            if (remember)
            {
                await _settings.SaveDatabasePathAsync(_templateStore.DatabasePath ?? databasePath, cancellationToken);
            }

            ActiveDatabasePath = _templateStore.DatabasePath ?? databasePath;
            StatusMessage = "Saved sessions database ready.";
            await RefreshAsync(cancellationToken);
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
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!HasDatabase)
        {
            return;
        }

        IReadOnlyList<EtwSessionTemplate> templates = await _templateStore.ListAsync(cancellationToken);
        Templates.Clear();
        foreach (EtwSessionTemplate template in templates)
        {
            Templates.Add(new SavedSessionTemplateViewModel(template));
        }

        SelectedTemplate = Templates.FirstOrDefault();
        OnActionPropertiesChanged();
    }

    public async Task SaveCurrentSessionAsync(string name, CancellationToken cancellationToken)
    {
        if (!HasDatabase)
        {
            StatusMessage = "Create or open a saved sessions database first.";
            return;
        }

        EtwProviderInfo? provider = _consumeProviderViewModel.SelectedProvider;
        if (provider is null)
        {
            StatusMessage = "Select a provider before saving a session.";
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = "Template name is required.";
            return;
        }

        IsBusy = true;
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var template = new EtwSessionTemplate(
                0,
                name.Trim(),
                provider.Id,
                provider.Name,
                _consumeProviderViewModel.SelectedEventFilterMode,
                _consumeProviderViewModel.EventFilterText,
                now,
                now);

            EtwSessionTemplate savedTemplate = await _templateStore.SaveAsync(template, cancellationToken);
            await RefreshAsync(cancellationToken);
            SelectedTemplate = Templates.FirstOrDefault(candidate => candidate.Id == savedTemplate.Id);
            StatusMessage = $"Saved session '{savedTemplate.Name}'.";
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            StatusMessage = "A saved session with that name already exists.";
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
        }
    }

    public bool TryLoadSelectedSession()
    {
        if (SelectedTemplate is null)
        {
            StatusMessage = "Select a saved session to load.";
            return false;
        }

        if (_consumeProviderViewModel.CanStop)
        {
            StatusMessage = "Stop consuming before loading a saved session.";
            return false;
        }

        EtwSessionTemplate template = SelectedTemplate.Template;
        _consumeProviderViewModel.SelectProvider(new EtwProviderInfo(
            template.ProviderName,
            template.ProviderId,
            EtwProviderSchemaSource.Unknown));
        _consumeProviderViewModel.ApplySavedEventFilter(template.EventFilterMode, template.EventFilterText);
        StatusMessage = $"Loaded session '{template.Name}'.";
        return true;
    }

    public async Task DeleteSelectedSessionAsync(CancellationToken cancellationToken)
    {
        if (SelectedTemplate is null)
        {
            StatusMessage = "Select a saved session to delete.";
            return;
        }

        long id = SelectedTemplate.Id;
        string name = SelectedTemplate.Name;
        IsBusy = true;
        try
        {
            await _templateStore.DeleteAsync(id, cancellationToken);
            await RefreshAsync(cancellationToken);
            StatusMessage = $"Deleted session '{name}'.";
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
        }
    }

    private void OnActionPropertiesChanged()
    {
        OnPropertyChanged(nameof(CanSaveCurrentSession));
        OnPropertyChanged(nameof(CanLoadSelectedSession));
        OnPropertyChanged(nameof(CanDeleteSelectedSession));
        OnPropertyChanged(nameof(DefaultTemplateName));
    }
}

public sealed class SavedSessionTemplateViewModel
{
    public SavedSessionTemplateViewModel(EtwSessionTemplate template)
    {
        Template = template;
    }

    public EtwSessionTemplate Template { get; }

    public long Id => Template.Id;

    public string Name => Template.Name;

    public string Provider => $"{Template.ProviderName} ({Template.ProviderId:D})";

    public string FilterSummary => string.IsNullOrWhiteSpace(Template.EventFilterText)
        ? $"{Template.EventFilterMode}: no event filter"
        : $"{Template.EventFilterMode}: {Template.EventFilterText}";

    public string UpdatedAt => Template.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}
