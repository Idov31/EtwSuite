using EtwSuite.Etw;
using EtwSuite.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EtwSuite
{
    public sealed partial class MainWindow : Window
    {
        private readonly CancellationTokenSource _loadCancellation = new();
        private CancellationTokenSource? _schemaLoadCancellation;

        public MainWindow()
        {
            InitializeComponent();

            ViewModel = new ProvidersViewModel(new EtwProviderCatalog());
            Root.DataContext = ViewModel;

            Closed += MainWindow_Closed;
        }

        public ProvidersViewModel ViewModel { get; }

        private async void Root_Loaded(object sender, RoutedEventArgs e)
        {
            Root.Loaded -= Root_Loaded;

            try
            {
                await ViewModel.LoadProvidersAsync(_loadCancellation.Token);
                await LoadSelectedProviderSchemaAsync();
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ViewModel.SearchText = ((TextBox)sender).Text;
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

        private void SchemaSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ViewModel.SelectedProviderDetails is not null)
            {
                ViewModel.SelectedProviderDetails.SchemaSearchText = ((TextBox)sender).Text;
            }
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            _loadCancellation.Cancel();
            _schemaLoadCancellation?.Cancel();
            _schemaLoadCancellation?.Dispose();
            _loadCancellation.Dispose();
        }

        private async Task LoadSelectedProviderSchemaAsync()
        {
            _schemaLoadCancellation?.Cancel();
            _schemaLoadCancellation?.Dispose();
            _schemaLoadCancellation = CancellationTokenSource.CreateLinkedTokenSource(_loadCancellation.Token);

            await ViewModel.LoadSelectedProviderSchemaAsync(_schemaLoadCancellation.Token);
        }
    }
}
