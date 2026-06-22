using EtwSuite.Core;
using EtwSuite.Etw;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EtwSuite.Tests;

[TestClass]
public sealed class FileEtwApplicationSettingsTests
{
    [TestMethod]
    public async Task SaveThemeModeAsync_PreservesDatabasePath()
    {
        string settingsPath = CreateSettingsPath();
        try
        {
            var settings = new FileEtwSessionTemplateSettings(settingsPath);
            const string databasePath = @"C:\Data\EtwSuite.SavedSessions.sqlite";

            await settings.SaveDatabasePathAsync(databasePath, CancellationToken.None);
            await settings.SaveThemeModeAsync(AppThemeMode.Dark, CancellationToken.None);

            Assert.AreEqual(databasePath, await settings.LoadDatabasePathAsync(CancellationToken.None));
            Assert.AreEqual(AppThemeMode.Dark, await settings.LoadThemeModeAsync(CancellationToken.None));
        }
        finally
        {
            DeleteSettingsPath(settingsPath);
        }
    }

    [TestMethod]
    public async Task SaveDatabasePathAsync_PreservesThemeMode()
    {
        string settingsPath = CreateSettingsPath();
        try
        {
            var settings = new FileEtwSessionTemplateSettings(settingsPath);
            const string databasePath = @"C:\Data\EtwSuite.SavedSessions.sqlite";

            await settings.SaveThemeModeAsync(AppThemeMode.Light, CancellationToken.None);
            await settings.SaveDatabasePathAsync(databasePath, CancellationToken.None);

            Assert.AreEqual(AppThemeMode.Light, await settings.LoadThemeModeAsync(CancellationToken.None));
            Assert.AreEqual(databasePath, await settings.LoadDatabasePathAsync(CancellationToken.None));
        }
        finally
        {
            DeleteSettingsPath(settingsPath);
        }
    }

    [TestMethod]
    public async Task LoadThemeModeAsync_DefaultsToSystemWhenUnset()
    {
        string settingsPath = CreateSettingsPath();
        try
        {
            var settings = new FileEtwSessionTemplateSettings(settingsPath);

            Assert.AreEqual(AppThemeMode.System, await settings.LoadThemeModeAsync(CancellationToken.None));
        }
        finally
        {
            DeleteSettingsPath(settingsPath);
        }
    }

    private static string CreateSettingsPath()
    {
        return Path.Combine(Path.GetTempPath(), "EtwSuite.Tests", Guid.NewGuid().ToString("N"), "settings.json");
    }

    private static void DeleteSettingsPath(string settingsPath)
    {
        string? directory = Path.GetDirectoryName(settingsPath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
