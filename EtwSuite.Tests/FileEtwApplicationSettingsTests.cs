using EtwSuite.Core;
using EtwSuite.Etw;
using EtwSuite.Etw.TraceLogging;
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

    [TestMethod]
    public async Task SaveThemeModeAsync_PreservesTraceLoggingPaths()
    {
        string settingsPath = CreateSettingsPath();
        string cachePath = Path.Combine(Path.GetDirectoryName(settingsPath)!, "tracelogging-cache.json");
        try
        {
            var traceLoggingCache = new FileTraceLoggingProviderCache(settingsPath, cachePath);
            var settings = new FileEtwSessionTemplateSettings(settingsPath);
            var paths = new[]
            {
                new TraceLoggingScanPath(@"C:\Windows\System32\ntoskrnl.exe", TraceLoggingScanPathKind.File)
            };

            await traceLoggingCache.SaveConfiguredPathsAsync(paths, CancellationToken.None);
            await settings.SaveThemeModeAsync(AppThemeMode.Dark, CancellationToken.None);

            IReadOnlyList<TraceLoggingScanPath> loadedPaths =
                await traceLoggingCache.LoadConfiguredPathsAsync(CancellationToken.None);
            Assert.AreEqual(1, loadedPaths.Count);
            Assert.AreEqual(paths[0], loadedPaths[0]);
        }
        finally
        {
            DeleteSettingsPath(settingsPath);
        }
    }

    [TestMethod]
    public async Task FileTraceLoggingProviderCache_RoundTripsCachedResult()
    {
        string settingsPath = CreateSettingsPath();
        string cachePath = Path.Combine(Path.GetDirectoryName(settingsPath)!, "tracelogging-cache.json");
        try
        {
            var cache = new FileTraceLoggingProviderCache(settingsPath, cachePath);
            var provider = new TraceLoggingProviderInfo(
                "TraceLogging.Provider",
                Guid.Parse("99999999-9999-9999-9999-999999999999"),
                null,
                @"C:\Windows\System32\provider.dll",
                [],
                [],
                FromCache: false,
                SourceLength: 10,
                SourceLastWriteTimeUtc: DateTimeOffset.UnixEpoch);

            await cache.SaveCachedResultAsync(new TraceLoggingScanResult([provider], []), CancellationToken.None);

            TraceLoggingScanResult loaded = await cache.LoadCachedResultAsync(CancellationToken.None);
            Assert.AreEqual(1, loaded.Providers.Count);
            Assert.AreEqual(provider.Name, loaded.Providers[0].Name);
            Assert.IsTrue(loaded.Providers[0].FromCache);
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
