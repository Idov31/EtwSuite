using EtwSuite.Core;
using EtwSuite.Etw;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EtwSuite.Tests;

[TestClass]
public sealed class SqliteEtwSessionTemplateStoreTests
{
    [TestMethod]
    public async Task InitializeAsync_CreatesSchema()
    {
        string databasePath = CreateTempDatabasePath();
        try
        {
            var store = new SqliteEtwSessionTemplateStore();
            await store.InitializeAsync(databasePath, CancellationToken.None);

            IReadOnlyList<EtwSessionTemplate> templates = await store.ListAsync(CancellationToken.None);

            Assert.AreEqual(0, templates.Count);
            Assert.IsTrue(File.Exists(databasePath));
        }
        finally
        {
            DeleteIfExists(databasePath);
        }
    }

    [TestMethod]
    public async Task SaveAsync_ListsSavedTemplateAndPreservesFilter()
    {
        string databasePath = CreateTempDatabasePath();
        try
        {
            var store = new SqliteEtwSessionTemplateStore();
            await store.InitializeAsync(databasePath, CancellationToken.None);

            EtwSessionTemplate saved = await store.SaveAsync(CreateTemplate("Session A"), CancellationToken.None);
            IReadOnlyList<EtwSessionTemplate> templates = await store.ListAsync(CancellationToken.None);

            Assert.AreNotEqual(0, saved.Id);
            Assert.AreEqual(1, templates.Count);
            Assert.AreEqual("Session A", templates[0].Name);
            Assert.AreEqual(EtwFilterMode.SQL, templates[0].EventFilterMode);
            Assert.AreEqual("event_id = 1", templates[0].EventFilterText);
        }
        finally
        {
            DeleteIfExists(databasePath);
        }
    }

    [TestMethod]
    public async Task SaveAsync_UpdatesExistingTemplate()
    {
        string databasePath = CreateTempDatabasePath();
        try
        {
            var store = new SqliteEtwSessionTemplateStore();
            await store.InitializeAsync(databasePath, CancellationToken.None);
            EtwSessionTemplate saved = await store.SaveAsync(CreateTemplate("Session A"), CancellationToken.None);

            EtwSessionTemplate updated = await store.SaveAsync(
                saved with
                {
                    Name = "Session B",
                    EventFilterMode = EtwFilterMode.Basic,
                    EventFilterText = "powershell",
                },
                CancellationToken.None);

            EtwSessionTemplate? loaded = await store.GetAsync(saved.Id, CancellationToken.None);

            Assert.AreEqual(saved.Id, updated.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual("Session B", loaded.Name);
            Assert.AreEqual(EtwFilterMode.Basic, loaded.EventFilterMode);
            Assert.AreEqual("powershell", loaded.EventFilterText);
        }
        finally
        {
            DeleteIfExists(databasePath);
        }
    }

    [TestMethod]
    public async Task DeleteAsync_RemovesTemplate()
    {
        string databasePath = CreateTempDatabasePath();
        try
        {
            var store = new SqliteEtwSessionTemplateStore();
            await store.InitializeAsync(databasePath, CancellationToken.None);
            EtwSessionTemplate saved = await store.SaveAsync(CreateTemplate("Session A"), CancellationToken.None);

            await store.DeleteAsync(saved.Id, CancellationToken.None);
            IReadOnlyList<EtwSessionTemplate> templates = await store.ListAsync(CancellationToken.None);

            Assert.AreEqual(0, templates.Count);
        }
        finally
        {
            DeleteIfExists(databasePath);
        }
    }

    private static EtwSessionTemplate CreateTemplate(string name)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new EtwSessionTemplate(
            0,
            name,
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            "Provider-A",
            EtwFilterMode.SQL,
            "event_id = 1",
            now,
            now);
    }

    private static string CreateTempDatabasePath()
    {
        return Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sqlite");
    }

    private static void DeleteIfExists(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}
