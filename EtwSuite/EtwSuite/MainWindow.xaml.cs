using EtwSuite.Core;
using EtwSuite.Etw;
using EtwSuite.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;

namespace EtwSuite
{
    public sealed partial class MainWindow : Window
    {
        private readonly CancellationTokenSource _loadCancellation = new();
        private CancellationTokenSource? _schemaLoadCancellation;

        public MainWindow()
        {
            InitializeComponent();

            var providerCatalog = new EtwProviderCatalog();
            ProvidersViewModel = new ProvidersViewModel(providerCatalog);
            ConsumeProviderViewModel = new ConsumeProviderViewModel(providerCatalog);

            ListProvidersView.DataContext = ProvidersViewModel;
            ConsumeProviderView.DataContext = ConsumeProviderViewModel;
            ProvidersViewModel.PropertyChanged += ProvidersViewModel_PropertyChanged;

            Closed += MainWindow_Closed;
        }

        public ProvidersViewModel ProvidersViewModel { get; }

        public ConsumeProviderViewModel ConsumeProviderViewModel { get; }

        private async void Root_Loaded(object sender, RoutedEventArgs e)
        {
            Root.Loaded -= Root_Loaded;

            try
            {
                await ProvidersViewModel.LoadProvidersAsync(_loadCancellation.Token);
                await ConsumeProviderViewModel.LoadProvidersAsync(_loadCancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void Root_SelectionChanged(
            NavigationView sender,
            NavigationViewSelectionChangedEventArgs args)
        {
            string? tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
            ListProvidersView.Visibility = tag == "ListProviders" ? Visibility.Visible : Visibility.Collapsed;
            ConsumeProviderView.Visibility = tag == "ConsumeProvider" ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            EtwProviderInfo? previousProvider = ProvidersViewModel.SelectedProvider;
            ProvidersViewModel.SearchText = ((TextBox)sender).Text;

            if (ProvidersViewModel.SelectedProvider is not null &&
                ProvidersViewModel.SelectedProvider != previousProvider)
            {
                try
                {
                    await LoadSelectedProviderSchemaAsync();
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        private async void ProvidersViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ProvidersViewModel.SelectedProvider))
            {
                return;
            }

            try
            {
                await LoadSelectedProviderSchemaAsync();
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void SchemaSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ProvidersViewModel.SelectedProviderDetails is not null)
            {
                ProvidersViewModel.SelectedProviderDetails.SchemaSearchText = ((TextBox)sender).Text;
            }
        }

        private async void ProvidersListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                await LoadSelectedProviderSchemaAsync();
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void ConsumeSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ConsumeProviderViewModel.SearchText = ((TextBox)sender).Text;
        }

        private async void StartStopConsumingButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ConsumeProviderViewModel.CanStop)
                {
                    await ConsumeProviderViewModel.StopAsync();
                }
                else
                {
                    await ConsumeProviderViewModel.StartAsync(_loadCancellation.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            ProvidersViewModel.PropertyChanged -= ProvidersViewModel_PropertyChanged;
            _loadCancellation.Cancel();
            _schemaLoadCancellation?.Cancel();
            _schemaLoadCancellation?.Dispose();
            _loadCancellation.Dispose();
            await ConsumeProviderViewModel.DisposeAsync();
        }

        private async Task LoadSelectedProviderSchemaAsync()
        {
            _schemaLoadCancellation?.Cancel();
            _schemaLoadCancellation?.Dispose();
            _schemaLoadCancellation = CancellationTokenSource.CreateLinkedTokenSource(_loadCancellation.Token);

            await ProvidersViewModel.LoadSelectedProviderSchemaAsync(_schemaLoadCancellation.Token);
        }
    }
}
