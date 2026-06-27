using EtwSuite.Core;
using EtwSuite.Etw;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EtwSuite.Tests;

[TestClass]
public sealed class EtwProviderCatalogTests
{
    [TestMethod]
    public void MergeWithTraceLoggingProviders_AddsCachedTraceLoggingProviders()
    {
        var traceLoggingProvider = new TraceLoggingProviderInfo(
            "TraceLogging-Provider",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            null,
            @"C:\Windows\System32\provider.dll",
            [],
            [],
            FromCache: true);

        IReadOnlyList<EtwProviderInfo> providers = EtwProviderCatalog.MergeWithTraceLoggingProviders(
            [],
            [traceLoggingProvider]);

        Assert.AreEqual(1, providers.Count);
        Assert.AreEqual(traceLoggingProvider.Name, providers[0].Name);
        Assert.AreEqual(traceLoggingProvider.Id, providers[0].Id);
        Assert.AreEqual(EtwProviderSchemaSource.TraceLogging, providers[0].SchemaSource);
    }

    [TestMethod]
    public void MergeWithTraceLoggingProviders_KeepsEnumeratedProviderWhenGuidAlreadyExists()
    {
        var enumeratedProvider = new EtwProviderInfo(
            "Enumerated Provider",
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            EtwProviderSchemaSource.XmlManifest);
        var traceLoggingProvider = new TraceLoggingProviderInfo(
            "TraceLogging Provider",
            enumeratedProvider.Id,
            null,
            @"C:\Windows\System32\provider.dll",
            [],
            [],
            FromCache: true);

        IReadOnlyList<EtwProviderInfo> providers = EtwProviderCatalog.MergeWithTraceLoggingProviders(
            [enumeratedProvider],
            [traceLoggingProvider]);

        Assert.AreEqual(1, providers.Count);
        Assert.AreEqual(enumeratedProvider, providers[0]);
    }

    [TestMethod]
    public async Task GetProviderSchemaAsync_ReturnsTraceLoggingDiagnosticWithoutStaticEvents()
    {
        var provider = new EtwProviderInfo(
            "TraceLogging-Provider",
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            EtwProviderSchemaSource.TraceLogging);
        var catalog = new EtwProviderCatalog();

        EtwProviderSchema schema = await catalog.GetProviderSchemaAsync(provider, CancellationToken.None);

        Assert.AreEqual(0, schema.Events.Count);
        CollectionAssert.Contains(
            schema.Diagnostics.ToArray(),
            EtwProviderCatalog.TraceLoggingStaticSchemaDiagnostic);
    }

    [TestMethod]
    public async Task GetProviderSchemaAsync_ReturnsCachedTraceLoggingSchema()
    {
        var provider = new EtwProviderInfo(
            "TraceLogging-Provider",
            Guid.Parse("77777777-7777-7777-7777-777777777777"),
            EtwProviderSchemaSource.TraceLogging);
        var traceLoggingProvider = new TraceLoggingProviderInfo(
            provider.Name,
            provider.Id,
            null,
            @"C:\Windows\System32\provider.dll",
            [
                new TraceLoggingEventSchema(
                    "StaticEvent",
                    11,
                    5,
                    2,
                    0x4000,
                    [new EtwSchemaParameter("Field", "UInt32")],
                    @"C:\Windows\System32\provider.dll")
            ],
            [],
            FromCache: true);
        var catalog = new EtwProviderCatalog(new FakeTraceLoggingProviderCache(
            new TraceLoggingScanResult([traceLoggingProvider], [])));

        EtwProviderSchema schema = await catalog.GetProviderSchemaAsync(provider, CancellationToken.None);

        Assert.AreEqual(1, schema.Events.Count);
        Assert.AreEqual("StaticEvent", schema.Events[0].Name);
        Assert.AreEqual((byte)11, schema.Events[0].Channel);
        Assert.AreEqual(0x4000UL, schema.Events[0].Keyword);
        Assert.AreEqual("Field", schema.Events[0].Parameters[0].Name);
    }

    private sealed class FakeTraceLoggingProviderCache(TraceLoggingScanResult cachedResult) : ITraceLoggingProviderCache
    {
        public Task<IReadOnlyList<TraceLoggingScanPath>> LoadConfiguredPathsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<TraceLoggingScanPath>>([]);
        }

        public Task SaveConfiguredPathsAsync(
            IReadOnlyList<TraceLoggingScanPath> paths,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<TraceLoggingScanResult> LoadCachedResultAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(cachedResult);
        }

        public Task SaveCachedResultAsync(
            TraceLoggingScanResult result,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
