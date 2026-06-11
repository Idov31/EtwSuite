using System.Collections.ObjectModel;
using EtwSuite.Core;

namespace EtwSuite.ViewModels;

public sealed class ProvidersViewModel : ObservableObject
{
    private readonly IEtwProviderCatalog _providerCatalog;
    private IReadOnlyList<EtwProviderInfo> _allProviders = Array.Empty<EtwProviderInfo>();
    private EtwProviderInfo? _selectedProvider;
    private ProviderDetailsViewModel? _selectedProviderDetails;
    private string _searchText = string.Empty;
    private string? _errorMessage;
    private bool _isLoading;

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
            if (SelectedProvider == provider && SelectedProviderDetails == details)
            {
                details.SetSchema(schema);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (SelectedProvider == provider && SelectedProviderDetails == details)
            {
                details.SetSchemaError(ex.Message);
            }
        }
        finally
        {
            if (SelectedProvider == provider && SelectedProviderDetails == details)
            {
                details.EndSchemaLoad();
            }
        }
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
        foreach (EtwProviderInfo provider in filteredProviders)
        {
            Providers.Add(provider);
        }

        SelectedProvider = previousSelection is not null && Providers.Contains(previousSelection)
            ? previousSelection
            : Providers.FirstOrDefault();

        OnPropertyChanged(nameof(ProviderCountText));
    }
}
