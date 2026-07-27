namespace EtwSuite.Core;

public enum AppThemeMode
{
    System,
    Light,
    Dark
}

public interface IEtwApplicationSettings : IEtwSessionTemplateSettings
{
    Task<AppThemeMode> LoadThemeModeAsync(CancellationToken cancellationToken);

    Task SaveThemeModeAsync(AppThemeMode themeMode, CancellationToken cancellationToken);
}
