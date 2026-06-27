using System.Text.Json;
using EtwSuite.Core;

namespace EtwSuite.Etw;

public sealed class FileEtwSessionTemplateSettings(string settingsPath) : IEtwApplicationSettings
{
    private readonly string _settingsPath = settingsPath;

    public FileEtwSessionTemplateSettings()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EtwSuite",
            "settings.json"))
    {
    }

    public async Task<string?> LoadDatabasePathAsync(CancellationToken cancellationToken)
    {
        SettingsDto settings = await LoadSettingsAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(settings.SavedSessionsDatabasePath)
            ? null
            : settings.SavedSessionsDatabasePath;
    }

    public async Task SaveDatabasePathAsync(string databasePath, CancellationToken cancellationToken)
    {
        SettingsDto settings = await LoadSettingsAsync(cancellationToken);
        settings.SavedSessionsDatabasePath = databasePath;
        await SaveSettingsAsync(settings, cancellationToken);
    }

    public async Task<AppThemeMode> LoadThemeModeAsync(CancellationToken cancellationToken)
    {
        SettingsDto settings = await LoadSettingsAsync(cancellationToken);
        return Enum.TryParse(settings.ThemeMode, ignoreCase: true, out AppThemeMode themeMode)
            ? themeMode
            : AppThemeMode.System;
    }

    public async Task SaveThemeModeAsync(AppThemeMode themeMode, CancellationToken cancellationToken)
    {
        SettingsDto settings = await LoadSettingsAsync(cancellationToken);
        settings.ThemeMode = themeMode.ToString();
        await SaveSettingsAsync(settings, cancellationToken);
    }

    private async Task<SettingsDto> LoadSettingsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath))
        {
            return new SettingsDto();
        }

        await using FileStream stream = File.OpenRead(_settingsPath);
        return await JsonSerializer.DeserializeAsync<SettingsDto>(stream, cancellationToken: cancellationToken)
            ?? new SettingsDto();
    }

    private async Task SaveSettingsAsync(SettingsDto settings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath) ?? ".");
        await using FileStream stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, cancellationToken: cancellationToken);
    }

    private sealed class SettingsDto
    {
        public string? SavedSessionsDatabasePath { get; set; }

        public string? ThemeMode { get; set; }

        public List<TraceLoggingScanPathDto> TraceLoggingPaths { get; set; } = [];
    }

    private sealed class TraceLoggingScanPathDto
    {
        public string Path { get; set; } = string.Empty;

        public TraceLoggingScanPathKind Kind { get; set; }
    }
}
