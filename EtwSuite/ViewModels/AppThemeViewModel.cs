using EtwSuite.Core;

namespace EtwSuite.ViewModels;

public sealed class AppThemeViewModel : ObservableObject
{
    private readonly IEtwApplicationSettings _settings;
    private AppThemeMode _selectedThemeMode = AppThemeMode.System;

    public AppThemeViewModel(IEtwApplicationSettings settings)
    {
        _settings = settings;
    }

    public IReadOnlyList<AppThemeMode> ThemeModes { get; } =
        new[] { AppThemeMode.System, AppThemeMode.Light, AppThemeMode.Dark };

    public AppThemeMode SelectedThemeMode
    {
        get => _selectedThemeMode;
        set => SetProperty(ref _selectedThemeMode, value);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        SelectedThemeMode = await _settings.LoadThemeModeAsync(cancellationToken);
    }

    public Task SaveThemeModeAsync(CancellationToken cancellationToken)
    {
        return _settings.SaveThemeModeAsync(SelectedThemeMode, cancellationToken);
    }
}
