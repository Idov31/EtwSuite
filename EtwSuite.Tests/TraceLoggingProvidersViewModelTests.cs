using EtwSuite.Core;
using EtwSuite.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EtwSuite.Tests;

[TestClass]
public sealed class TraceLoggingProvidersViewModelTests
{
    [TestMethod]
    public async Task ProviderSearchText_FiltersProvidersByNameGuidGroupSourceAndCache()
    {
        var groupId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var providerOne = new TraceLoggingProviderInfo(
            "TraceLogging.Provider.One",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            groupId,
            @"C:\Windows\System32\provider-one.dll",
            [],
            [],
            FromCache: true);
        var providerTwo = new TraceLoggingProviderInfo(
            "TraceLogging.Provider.Two",
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            null,
            @"C:\Windows\System32\provider-two.sys",
            [],
            [],
            FromCache: false);
        var viewModel = new TraceLoggingProvidersViewModel(
            new FakeTraceLoggingProviderScanner(new TraceLoggingScanResult([providerOne, providerTwo], [])),
            new FakeTraceLoggingProviderCache());

        await viewModel.AddPathAsync(@"C:\Windows\System32", TraceLoggingScanPathKind.Folder, CancellationToken.None);
        await viewModel.RefreshAsync(CancellationToken.None);

        viewModel.ProviderSearchText = "provider-one";
        Assert.AreEqual(1, viewModel.Providers.Count);
        Assert.AreEqual("TraceLogging.Provider.One", viewModel.Providers[0].Name);

        viewModel.ProviderSearchText = groupId.ToString("D");
        Assert.AreEqual(1, viewModel.Providers.Count);
        Assert.AreEqual("TraceLogging.Provider.One", viewModel.Providers[0].Name);

        viewModel.ProviderSearchText = "provider-two.sys";
        Assert.AreEqual(1, viewModel.Providers.Count);
        Assert.AreEqual("TraceLogging.Provider.Two", viewModel.Providers[0].Name);

        viewModel.ProviderSearchText = "Cached";
        Assert.AreEqual(1, viewModel.Providers.Count);
        Assert.AreEqual("TraceLogging.Provider.One", viewModel.Providers[0].Name);
    }

    [TestMethod]
    public async Task ProviderSearchText_PreservesSelectionWhenVisibleAndFallsBackWhenHidden()
    {
        var providerOne = new TraceLoggingProviderInfo(
            "Alpha.Provider",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            null,
            @"C:\Windows\System32\alpha.dll",
            [],
            [],
            FromCache: false);
        var providerTwo = new TraceLoggingProviderInfo(
            "Beta.Provider",
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            null,
            @"C:\Windows\System32\beta.dll",
            [],
            [],
            FromCache: false);
        var viewModel = new TraceLoggingProvidersViewModel(
            new FakeTraceLoggingProviderScanner(new TraceLoggingScanResult([providerOne, providerTwo], [])),
            new FakeTraceLoggingProviderCache());

        await viewModel.AddPathAsync(@"C:\Windows\System32", TraceLoggingScanPathKind.Folder, CancellationToken.None);
        await viewModel.RefreshAsync(CancellationToken.None);
        viewModel.SelectedProvider = viewModel.Providers[1];

        viewModel.ProviderSearchText = "Provider";
        Assert.AreEqual("Beta.Provider", viewModel.SelectedProvider?.Name);

        viewModel.ProviderSearchText = "Alpha";
        Assert.AreEqual("Alpha.Provider", viewModel.SelectedProvider?.Name);
    }

    [TestMethod]
    public async Task SchemaSearchText_FiltersSelectedProviderEvents()
    {
        var provider = new TraceLoggingProviderInfo(
            "TraceLogging.Provider",
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            null,
            @"C:\Windows\System32\provider.dll",
            [
                new TraceLoggingEventSchema(
                    "FirstEvent",
                    11,
                    5,
                    2,
                    0x4000,
                    [new EtwSchemaParameter("ProcessId", "UInt32")],
                    @"C:\Windows\System32\provider.dll"),
                new TraceLoggingEventSchema(
                    "SecondEvent",
                    11,
                    4,
                    1,
                    0x8000,
                    [new EtwSchemaParameter("Path", "UnicodeString")],
                    @"C:\Windows\System32\provider.dll")
            ],
            [],
            FromCache: false);
        var viewModel = new TraceLoggingProvidersViewModel(
            new FakeTraceLoggingProviderScanner(new TraceLoggingScanResult([provider], [])),
            new FakeTraceLoggingProviderCache());

        await viewModel.AddPathAsync(@"C:\Windows\System32\provider.dll", TraceLoggingScanPathKind.File, CancellationToken.None);
        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.AreEqual(2, viewModel.Events.Count);

        viewModel.SchemaSearchText = "ProcessId";
        Assert.AreEqual(1, viewModel.Events.Count);
        Assert.AreEqual("FirstEvent", viewModel.Events[0].Name);

        viewModel.SchemaSearchText = "UnicodeString";
        Assert.AreEqual(1, viewModel.Events.Count);
        Assert.AreEqual("SecondEvent", viewModel.Events[0].Name);
    }

    [TestMethod]
    public async Task SchemaSearchText_FiltersByOpcodeAndLevel()
    {
        var provider = new TraceLoggingProviderInfo(
            "TraceLogging.Provider",
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            null,
            @"C:\Windows\System32\provider.dll",
            [
                new TraceLoggingEventSchema(
                    "Alpha",
                    11,
                    5,
                    7,
                    0,
                    [],
                    @"C:\Windows\System32\provider.dll"),
                new TraceLoggingEventSchema(
                    "Beta",
                    11,
                    2,
                    1,
                    0,
                    [],
                    @"C:\Windows\System32\provider.dll")
            ],
            [],
            FromCache: false);
        var viewModel = new TraceLoggingProvidersViewModel(
            new FakeTraceLoggingProviderScanner(new TraceLoggingScanResult([provider], [])),
            new FakeTraceLoggingProviderCache());

        await viewModel.AddPathAsync(@"C:\Windows\System32\provider.dll", TraceLoggingScanPathKind.File, CancellationToken.None);
        await viewModel.RefreshAsync(CancellationToken.None);

        viewModel.SchemaSearchText = "7";
        Assert.AreEqual(1, viewModel.Events.Count);
        Assert.AreEqual("Alpha", viewModel.Events[0].Name);

        viewModel.SchemaSearchText = "2";
        Assert.AreEqual(1, viewModel.Events.Count);
        Assert.AreEqual("Beta", viewModel.Events[0].Name);
    }

    private sealed class FakeTraceLoggingProviderScanner(TraceLoggingScanResult result) : ITraceLoggingProviderScanner
    {
        public Task<TraceLoggingScanResult> ScanAsync(
            IReadOnlyList<TraceLoggingScanPath> paths,
            bool useCache,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class FakeTraceLoggingProviderCache : ITraceLoggingProviderCache
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
            return Task.FromResult(new TraceLoggingScanResult([], []));
        }

        public Task SaveCachedResultAsync(
            TraceLoggingScanResult result,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
