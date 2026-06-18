using EtwSuite.Core;
using EtwSuite.Etw;
using EtwSuite.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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
            SetSystemBackdrop();
            SetWindowIcon();

            var providerCatalog = new EtwProviderCatalog();
            var recordingReader = new TraceEventRecordingReader();
            var sessionTemplateStore = new SqliteEtwSessionTemplateStore();
            var sessionTemplateSettings = new FileEtwSessionTemplateSettings();
            ProvidersViewModel = new ProvidersViewModel(providerCatalog);
            ConsumeProviderViewModel = new ConsumeProviderViewModel(providerCatalog);
            OpenRecordingViewModel = new OpenRecordingViewModel(recordingReader);
            SavedSessionsViewModel = new SavedSessionsViewModel(
                sessionTemplateStore,
                sessionTemplateSettings,
                ConsumeProviderViewModel);

            ListProvidersView.DataContext = ProvidersViewModel;
            ConsumeProviderView.DataContext = ConsumeProviderViewModel;
            OpenRecordingView.DataContext = OpenRecordingViewModel;
            SavedSessionsView.DataContext = SavedSessionsViewModel;
            ProvidersViewModel.PropertyChanged += ProvidersViewModel_PropertyChanged;

            Closed += MainWindow_Closed;
        }

        public ProvidersViewModel ProvidersViewModel { get; }

        public ConsumeProviderViewModel ConsumeProviderViewModel { get; }

        public OpenRecordingViewModel OpenRecordingViewModel { get; }

        public SavedSessionsViewModel SavedSessionsViewModel { get; }

        private void SetWindowIcon()
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "EtwSuite_Icon.ico");
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }
        }

        private void SetSystemBackdrop()
        {
            try
            {
                SystemBackdrop = new MicaBackdrop();
            }
            catch (Exception)
            {
                SystemBackdrop = null;
            }
        }

        private async void Root_Loaded(object sender, RoutedEventArgs e)
        {
            Root.Loaded -= Root_Loaded;

            try
            {
                await ProvidersViewModel.LoadProvidersAsync(_loadCancellation.Token);
                await SavedSessionsViewModel.InitializeAsync(_loadCancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }

            try
            {
                await ConsumeProviderViewModel.LoadProvidersAsync(_loadCancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                ConsumeProviderViewModel.ReportError(ex.Message);
            }
        }

        private async void Root_SelectionChanged(
            NavigationView sender,
            NavigationViewSelectionChangedEventArgs args)
        {
            string? tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
            ListProvidersView.Visibility = tag == "ListProviders" ? Visibility.Visible : Visibility.Collapsed;
            ConsumeProviderView.Visibility = tag == "ConsumeProvider" ? Visibility.Visible : Visibility.Collapsed;
            OpenRecordingView.Visibility = tag == "OpenRecording" ? Visibility.Visible : Visibility.Collapsed;
            SavedSessionsView.Visibility = tag == "SavedSessions" ? Visibility.Visible : Visibility.Collapsed;
            HelpView.Visibility = tag == "Help" ? Visibility.Visible : Visibility.Collapsed;
            if (tag == "SavedSessions" && !SavedSessionsViewModel.HasDatabase)
            {
                await PromptForSavedSessionsDatabaseAsync();
            }
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
            catch (Exception ex)
            {
                ConsumeProviderViewModel.ReportError(ex.Message);
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

        private void ConsumeProviderFromListFlyoutItem_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not EtwProviderInfo provider)
            {
                return;
            }

            ConsumeProviderViewModel.SelectProvider(provider);
            Root.SelectedItem = ConsumeProviderNavigationItem;
            UpdateConsumeProviderSearchText();
            UpdateConsumeProviderMatchesVisibility();
        }

        private void ConsumeSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ConsumeProviderViewModel.SearchText = ((TextBox)sender).Text;
            UpdateConsumeProviderMatchesVisibility();
        }

        private void EventFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ConsumeProviderViewModel.EventFilterText = ((TextBox)sender).Text;
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
                    string? etlPath = null;
                    if (ConsumeProviderViewModel.IsEtlRecordingEnabled)
                    {
                        nint ownerHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
                        etlPath = ShowSaveDialog(
                            ownerHandle,
                            "Record ETL",
                            ConsumeProviderViewModel.GetDefaultEtlRecordingFileName(),
                            ".etl",
                            "ETL");
                        if (string.IsNullOrWhiteSpace(etlPath))
                        {
                            return;
                        }

                        ConsumeProviderViewModel.EtlRecordingPath = etlPath;
                    }

                    await ConsumeProviderViewModel.StartAsync(etlPath, _loadCancellation.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async void OpenLastRecordingButton_Click(object sender, RoutedEventArgs e)
        {
            string? path = ConsumeProviderViewModel.LastRecordedEtlPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                Root.SelectedItem = OpenRecordingNavigationItem;
                await OpenRecordingViewModel.OpenAsync(path, _loadCancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async void OpenRecordingFileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                nint ownerHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
                string? selectedPath = ShowOpenDialog(ownerHandle);
                if (string.IsNullOrWhiteSpace(selectedPath))
                {
                    return;
                }

                await OpenRecordingViewModel.OpenAsync(selectedPath, _loadCancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                OpenRecordingViewModel.ReportError(ex.Message);
            }
        }

        private void OpenRecordingEventFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            OpenRecordingViewModel.EventFilterText = ((TextBox)sender).Text;
        }

        private void OpenRecordingEventsPerPageTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(((TextBox)sender).Text, out int eventsPerPage))
            {
                OpenRecordingViewModel.EventsPerPage = eventsPerPage;
            }
        }

        private void OpenRecordingPreviousEventsPageButton_Click(object sender, RoutedEventArgs e)
        {
            OpenRecordingViewModel.GoToPreviousPage();
        }

        private void OpenRecordingNextEventsPageButton_Click(object sender, RoutedEventArgs e)
        {
            OpenRecordingViewModel.GoToNextPage();
        }

        private async void CreateSavedSessionsDatabaseButton_Click(object sender, RoutedEventArgs e)
        {
            await CreateSavedSessionsDatabaseAsync();
        }

        private async void OpenSavedSessionsDatabaseButton_Click(object sender, RoutedEventArgs e)
        {
            await OpenSavedSessionsDatabaseAsync();
        }

        private async void SaveCurrentSessionButton_Click(object sender, RoutedEventArgs e)
        {
            string? name = await PromptForTemplateNameAsync(SavedSessionsViewModel.DefaultTemplateName);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            await SavedSessionsViewModel.SaveCurrentSessionAsync(name, _loadCancellation.Token);
        }

        private void LoadSavedSessionButton_Click(object sender, RoutedEventArgs e)
        {
            if (!SavedSessionsViewModel.TryLoadSelectedSession())
            {
                return;
            }

            Root.SelectedItem = ConsumeProviderNavigationItem;
            UpdateConsumeProviderSearchText();
            EventFilterTextBox.Text = ConsumeProviderViewModel.EventFilterText;
            UpdateConsumeProviderMatchesVisibility();
        }

        private async void DeleteSavedSessionButton_Click(object sender, RoutedEventArgs e)
        {
            SavedSessionTemplateViewModel? selectedTemplate = SavedSessionsViewModel.SelectedTemplate;
            if (selectedTemplate is null)
            {
                return;
            }

            bool confirmed = await ConfirmAsync(
                "Delete saved session?",
                $"Delete '{selectedTemplate.Name}'?");
            if (!confirmed)
            {
                return;
            }

            await SavedSessionsViewModel.DeleteSelectedSessionAsync(_loadCancellation.Token);
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
                string? selectedPath = ShowSaveDialog(ownerHandle, "Export events", suggestedName, extension, format);
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

        private async Task PromptForSavedSessionsDatabaseAsync()
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Title = "Saved sessions database",
                Content = "Create a new SQLite database or open an existing saved sessions database.",
                PrimaryButtonText = "Create",
                SecondaryButtonText = "Open",
                CloseButtonText = "Cancel",
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await CreateSavedSessionsDatabaseAsync();
            }
            else if (result == ContentDialogResult.Secondary)
            {
                await OpenSavedSessionsDatabaseAsync();
            }
        }

        private async Task CreateSavedSessionsDatabaseAsync()
        {
            try
            {
                nint ownerHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
                string? path = ShowSaveDialog(
                    ownerHandle,
                    "Create saved sessions database",
                    "EtwSuite.SavedSessions.sqlite",
                    ".sqlite",
                    "SQLite");
                if (!string.IsNullOrWhiteSpace(path))
                {
                    await SavedSessionsViewModel.OpenDatabaseAsync(path, remember: true, _loadCancellation.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                SavedSessionsViewModel.ReportError(ex.Message);
            }
        }

        private async Task OpenSavedSessionsDatabaseAsync()
        {
            try
            {
                nint ownerHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
                string? path = ShowOpenDialog(
                    ownerHandle,
                    "Open saved sessions database",
                    new[]
                    {
                        new ComDlgFilterSpec { Name = "SQLite database", Spec = "*.sqlite;*.sqlite3;*.db" },
                        new ComDlgFilterSpec { Name = "All files", Spec = "*.*" },
                    });
                if (!string.IsNullOrWhiteSpace(path))
                {
                    await SavedSessionsViewModel.OpenDatabaseAsync(path, remember: true, _loadCancellation.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                SavedSessionsViewModel.ReportError(ex.Message);
            }
        }

        private async Task<string?> PromptForTemplateNameAsync(string defaultName)
        {
            var nameTextBox = new TextBox
            {
                Header = "Name",
                Text = defaultName,
                SelectionStart = 0,
                SelectionLength = defaultName.Length,
            };

            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Title = "Save current session",
                Content = nameTextBox,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
            };

            ContentDialogResult result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? nameTextBox.Text : null;
        }

        private async Task<bool> ConfirmAsync(string title, string content)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Title = title,
                Content = content,
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private static string? ShowSaveDialog(
            nint ownerHandle,
            string title,
            string suggestedFileName,
            string extension,
            string format)
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
                dialog.SetTitle(title);
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

        private static string? ShowOpenDialog(nint ownerHandle)
        {
            return ShowOpenDialog(
                ownerHandle,
                "Open recording",
                new[]
                {
                    new ComDlgFilterSpec { Name = "Supported recordings", Spec = "*.etl;*.json;*.csv" },
                    new ComDlgFilterSpec { Name = "ETL trace", Spec = "*.etl" },
                    new ComDlgFilterSpec { Name = "EtwSuite exports", Spec = "*.json;*.csv" },
                    new ComDlgFilterSpec { Name = "All files", Spec = "*.*" },
                });
        }

        private static string? ShowOpenDialog(
            nint ownerHandle,
            string title,
            ComDlgFilterSpec[] filterSpecs)
        {
            Type? dialogType = Type.GetTypeFromCLSID(new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7"));
            if (dialogType is null)
            {
                throw new InvalidOperationException("The file open dialog type could not be loaded.");
            }

            object dialogObject = Activator.CreateInstance(dialogType)
                ?? throw new InvalidOperationException("Failed to create file open dialog.");
            var dialog = (IFileDialog)dialogObject;

            IShellItem? result = null;
            try
            {
                dialog.SetTitle(title);
                dialog.SetOptions(FileOpenOptions.ForceFileSystem | FileOpenOptions.PathMustExist | FileOpenOptions.FileMustExist);
                dialog.SetFileTypes((uint)filterSpecs.Length, filterSpecs);

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
            FileMustExist = 0x00001000,
        }

        internal enum ShellItemDisplayName : uint
        {
            FileSystemPath = 0x80058000,
        }
    }
}
