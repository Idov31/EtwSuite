using System.Collections.ObjectModel;
using EtwSuite.Core;

namespace EtwSuite.ViewModels;

public sealed class ProvidersViewModel : ObservableObject
{
    private readonly IEtwProviderCatalog _providerCatalog;
    private IReadOnlyList<EtwProviderInfo> _allProviders = Array.Empty<EtwProviderInfo>();
    private readonly HashSet<Guid> _missingProviderIds = new();
    private EtwProviderInfo? _selectedProvider;
    private ProviderDetailsViewModel? _selectedProviderDetails;
    private string _searchText = string.Empty;
    private string? _errorMessage;
    private bool _isLoading;
    private bool _showMissingProviders;
    private long _schemaLoadVersion;

    public ProvidersViewModel(IEtwProviderCatalog providerCatalog)
    {
        _providerCatalog = providerCatalog;
    }

    public ObservableCollection<EtwProviderInfo> Providers { get; } = new();

    public EtwProviderInfo? SelectedProvider
    {
        get => _selectedProvider;
        set
        {
            if (SetProperty(ref _selectedProvider, value))
            {
                SelectedProviderDetails = value is null ? null : new ProviderDetailsViewModel(value);
            }
        }
    }

    public ProviderDetailsViewModel? SelectedProviderDetails
    {
        get => _selectedProviderDetails;
        private set => SetProperty(ref _selectedProviderDetails, value);
    }

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

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool ShowMissingProviders
    {
        get => _showMissingProviders;
        set
        {
            if (SetProperty(ref _showMissingProviders, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string ProviderCountText => IsLoading
        ? "Loading providers..."
        : $"{Providers.Count:N0} providers";

    public async Task LoadProvidersAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        ErrorMessage = null;
        OnPropertyChanged(nameof(ProviderCountText));

        try
        {
            _allProviders = await _providerCatalog.EnumerateProvidersAsync(cancellationToken);
            ApplyFilter();
            SelectedProvider = Providers.FirstOrDefault();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Providers.Clear();
            SelectedProvider = null;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(ProviderCountText));
        }
    }

    public async Task LoadSelectedProviderSchemaAsync(CancellationToken cancellationToken)
    {
        long loadVersion = Interlocked.Increment(ref _schemaLoadVersion);
        EtwProviderInfo? provider = SelectedProvider;
        ProviderDetailsViewModel? details = SelectedProviderDetails;
        if (provider is null || details is null)
        {
            return;
        }

        details.BeginSchemaLoad();

        try
        {
            EtwProviderSchema schema = await _providerCatalog.GetProviderSchemaAsync(provider, cancellationToken);
            if (loadVersion == _schemaLoadVersion && SelectedProvider == provider && SelectedProviderDetails == details)
            {
                details.SetSchema(schema);
                SetProviderMissing(provider, schema.Events.Count == 0);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (loadVersion == _schemaLoadVersion && SelectedProvider == provider && SelectedProviderDetails == details)
            {
                details.SetSchemaError(ex.Message);
            }
        }
        finally
        {
            if (loadVersion == _schemaLoadVersion && SelectedProvider == provider && SelectedProviderDetails == details)
            {
                details.EndSchemaLoad();
            }
        }
    }

    private void ApplyFilter()
    {
        EtwProviderInfo? previousSelection = SelectedProvider;
        EtwCompiledFilter<EtwProviderInfo> searchFilter =
            EtwFilterCompiler.CompileProviderFilter(EtwFilterMode.Basic, SearchText);

        IEnumerable<EtwProviderInfo> filteredProviders = _allProviders;
        if (!ShowMissingProviders)
        {
            filteredProviders = filteredProviders.Where(provider =>
                provider.SchemaSource != EtwProviderSchemaSource.Unknown &&
                !_missingProviderIds.Contains(provider.Id));
        }

        filteredProviders = filteredProviders.Where(searchFilter.Matches);

        Providers.Clear();
        foreach (EtwProviderInfo provider in filteredProviders)
        {
            Providers.Add(provider);
        }

        SelectedProvider = previousSelection is not null && Providers.Contains(previousSelection)
            ? previousSelection
            : Providers.FirstOrDefault();

        OnPropertyChanged(nameof(ProviderCountText));
    }

    private void SetProviderMissing(EtwProviderInfo provider, bool isMissing)
    {
        bool changed = isMissing
            ? _missingProviderIds.Add(provider.Id)
            : _missingProviderIds.Remove(provider.Id);

        if (changed && !ShowMissingProviders)
        {
            ApplyFilter();
        }
    }
}
