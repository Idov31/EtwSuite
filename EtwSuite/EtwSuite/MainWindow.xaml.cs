using EtwSuite.Core;
using EtwSuite.Etw;
using EtwSuite.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.System;

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

        private async void ProviderSearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter)
            {
                return;
            }

            e.Handled = true;
            if (ProvidersViewModel.SelectedProvider is null)
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

        private void SchemaSearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
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
            UpdateConsumeProviderMatchesVisibility();
        }

        private void ConsumeSearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter)
            {
                return;
            }

            e.Handled = true;
            ConsumeProviderViewModel.SelectFirstMatchingProvider();
            UpdateConsumeProviderSearchText();
            UpdateConsumeProviderMatchesVisibility();
        }

        private void ConsumeProvidersListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateConsumeProviderSearchText();
            UpdateConsumeProviderMatchesVisibility();
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

        private void EventsPerPageTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(((TextBox)sender).Text, out int eventsPerPage))
            {
                ConsumeProviderViewModel.EventsPerPage = eventsPerPage;
            }
        }

        private void PreviousEventsPageButton_Click(object sender, RoutedEventArgs e)
        {
            ConsumeProviderViewModel.GoToPreviousPage();
        }

        private void NextEventsPageButton_Click(object sender, RoutedEventArgs e)
        {
            ConsumeProviderViewModel.GoToNextPage();
        }

        private async void ExportEventsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string extension = ConsumeProviderViewModel.GetExportFileExtension();
                string format = ConsumeProviderViewModel.SelectedExportFormat.ToUpperInvariant();
                string suggestedName = ConsumeProviderViewModel.GetDefaultExportFileName();
                nint ownerHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
                string? selectedPath = ShowSaveDialog(ownerHandle, suggestedName, extension, format);
                if (string.IsNullOrWhiteSpace(selectedPath))
                {
                    return;
                }

                await ConsumeProviderViewModel.ExportAsync(selectedPath, _loadCancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                ConsumeProviderViewModel.ReportError(ex.Message);
            }
        }

        private static string? ShowSaveDialog(nint ownerHandle, string suggestedFileName, string extension, string format)
        {
            Type? dialogType = Type.GetTypeFromCLSID(new Guid("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B"));
            if (dialogType is null)
            {
                throw new InvalidOperationException("The file save dialog type could not be loaded.");
            }

            object dialogObject = Activator.CreateInstance(dialogType)
                ?? throw new InvalidOperationException("Failed to create file save dialog.");
            var dialog = (IFileDialog)dialogObject;

            IShellItem? result = null;
            try
            {
                dialog.SetTitle("Export events");
                dialog.SetFileName(suggestedFileName);
                dialog.SetDefaultExtension(extension.TrimStart('.'));
                dialog.SetOptions(FileOpenOptions.ForceFileSystem | FileOpenOptions.PathMustExist | FileOpenOptions.OverwritePrompt);
                dialog.SetFileTypes(
                    1,
                    new[]
                    {
                        new ComDlgFilterSpec
                        {
                            Name = $"{format} file",
                            Spec = $"*{extension}",
                        },
                    });

                int showResult = dialog.Show(ownerHandle);
                if (showResult == unchecked((int)0x800704C7))
                {
                    return null;
                }

                Marshal.ThrowExceptionForHR(showResult);
                dialog.GetResult(out result);
                result.GetDisplayName(ShellItemDisplayName.FileSystemPath, out nint filePathPtr);
                try
                {
                    return Marshal.PtrToStringUni(filePathPtr);
                }
                finally
                {
                    if (filePathPtr != nint.Zero)
                    {
                        Marshal.FreeCoTaskMem(filePathPtr);
                    }
                }
            }
            finally
            {
                if (result is not null)
                {
                    Marshal.ReleaseComObject(result);
                }

                Marshal.ReleaseComObject(dialog);
            }
        }

        private void UpdateConsumeProviderSearchText()
        {
            string? selectedName = ConsumeProviderViewModel.SelectedProvider?.Name;
            if (string.IsNullOrWhiteSpace(selectedName) || ConsumeProviderSearchTextBox.Text == selectedName)
            {
                return;
            }

            ConsumeProviderSearchTextBox.Text = selectedName;
            ConsumeProviderSearchTextBox.SelectionStart = ConsumeProviderSearchTextBox.Text.Length;
        }

        private void UpdateConsumeProviderMatchesVisibility()
        {
            string searchText = ConsumeProviderSearchTextBox.Text.Trim();
            string? selectedName = ConsumeProviderViewModel.SelectedProvider?.Name;
            bool hasOpenSearch = !string.IsNullOrWhiteSpace(searchText) &&
                !string.Equals(searchText, selectedName, StringComparison.CurrentCultureIgnoreCase);

            ConsumeProviderMatchesListView.Visibility = hasOpenSearch
                ? Visibility.Visible
                : Visibility.Collapsed;
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

        [ComImport]
        [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IFileDialog
        {
            [PreserveSig]
            int Show(nint parent);
            void SetFileTypes(uint fileTypeCount, [MarshalAs(UnmanagedType.LPArray)] ComDlgFilterSpec[] filterSpecs);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(nint eventSink, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(FileOpenOptions options);
            void GetOptions(out FileOpenOptions options);
            void SetDefaultFolder(IShellItem shellItem);
            void SetFolder(IShellItem shellItem);
            void GetFolder(out IShellItem shellItem);
            void GetCurrentSelection(out IShellItem shellItem);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem shellItem);
            void AddPlace(IShellItem shellItem, int alignment);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string defaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(nint filter);
        }

        [ComImport]
        [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IShellItem
        {
            void BindToHandler(nint bindContext, ref Guid handlerGuid, ref Guid interfaceGuid, out nint handler);
            void GetParent(out IShellItem parent);
            void GetDisplayName(ShellItemDisplayName sigdnName, out nint name);
            void GetAttributes(uint requestedAttributes, out uint attributes);
            void Compare(IShellItem shellItem, uint hint, out int order);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct ComDlgFilterSpec
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string Name;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string Spec;
        }

        [Flags]
        internal enum FileOpenOptions : uint
        {
            OverwritePrompt = 0x00000002,
            PathMustExist = 0x00000800,
            ForceFileSystem = 0x00000040,
        }

        internal enum ShellItemDisplayName : uint
        {
            FileSystemPath = 0x80058000,
        }
    }
}
