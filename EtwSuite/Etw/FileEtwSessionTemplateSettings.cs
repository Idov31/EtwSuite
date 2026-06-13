using System.Text.Json;
using EtwSuite.Core;

namespace EtwSuite.Etw;

public sealed class FileEtwSessionTemplateSettings(string settingsPath) : IEtwSessionTemplateSettings
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
        if (!File.Exists(_settingsPath))
        {
            return null;
        }

        await using FileStream stream = File.OpenRead(_settingsPath);
        SettingsDto? settings = await JsonSerializer.DeserializeAsync<SettingsDto>(stream, cancellationToken: cancellationToken);
        return string.IsNullOrWhiteSpace(settings?.SavedSessionsDatabasePath)
            ? null
            : settings.SavedSessionsDatabasePath;
    }

    public async Task SaveDatabasePathAsync(string databasePath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath) ?? ".");
        await using FileStream stream = File.Create(_settingsPath);
        var settings = new SettingsDto { SavedSessionsDatabasePath = databasePath };
        await JsonSerializer.SerializeAsync(stream, settings, cancellationToken: cancellationToken);
    }

    private sealed class SettingsDto
    {
        public string? SavedSessionsDatabasePath { get; set; }
    }
}
