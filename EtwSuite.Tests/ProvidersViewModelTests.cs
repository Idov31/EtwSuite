using EtwSuite.Core;
using EtwSuite.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EtwSuite.Tests;

[TestClass]
public sealed class ProvidersViewModelTests
{
    [TestMethod]
    public async Task LoadSelectedProviderSchemaAsync_KeepsTraceLoggingProviderVisibleWhenStaticSchemaIsEmpty()
    {
        var provider = new EtwProviderInfo(
            "TraceLogging-Provider",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            EtwProviderSchemaSource.TraceLogging);
        var viewModel = new ProvidersViewModel(new FakeProviderCatalog([provider]));

        await viewModel.LoadProvidersAsync(CancellationToken.None);
        await viewModel.LoadSelectedProviderSchemaAsync(CancellationToken.None);

        Assert.AreEqual(1, viewModel.Providers.Count);
        Assert.AreEqual(provider, viewModel.Providers[0]);
        Assert.AreEqual(provider, viewModel.SelectedProvider);
    }

    [TestMethod]
    public async Task LoadSelectedProviderSchemaAsync_KeepsSeededTraceLoggingProviderVisibleWhenStaticSchemaIsEmpty()
    {
        var provider = new EtwProviderInfo(
            "AttackSurfaceMonitor",
            Guid.Parse("c4e507b1-7224-4737-bde0-ced9284e7073"),
            EtwProviderSchemaSource.TraceLogging);
        var viewModel = new ProvidersViewModel(new FakeProviderCatalog([provider]));

        await viewModel.LoadProvidersAsync(CancellationToken.None);
        await viewModel.LoadSelectedProviderSchemaAsync(CancellationToken.None);

        Assert.AreEqual(1, viewModel.Providers.Count);
        Assert.AreEqual(provider, viewModel.Providers[0]);
        Assert.AreEqual(provider, viewModel.SelectedProvider);
    }

    [TestMethod]
    public async Task LoadSelectedProviderSchemaAsync_HidesManifestProviderWhenStaticSchemaIsEmpty()
    {
        var provider = new EtwProviderInfo(
            "Manifest-Provider",
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            EtwProviderSchemaSource.XmlManifest);
        var viewModel = new ProvidersViewModel(new FakeProviderCatalog([provider]));

        await viewModel.LoadProvidersAsync(CancellationToken.None);
        await viewModel.LoadSelectedProviderSchemaAsync(CancellationToken.None);

        Assert.AreEqual(0, viewModel.Providers.Count);
        Assert.IsNull(viewModel.SelectedProvider);
    }

    [TestMethod]
    public async Task LoadSelectedProviderSchemaAsync_HidesWbemProviderWhenStaticSchemaIsEmpty()
    {
        var provider = new EtwProviderInfo(
            "Wbem-Provider",
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            EtwProviderSchemaSource.Wbem);
        var viewModel = new ProvidersViewModel(new FakeProviderCatalog([provider]));

        await viewModel.LoadProvidersAsync(CancellationToken.None);
        await viewModel.LoadSelectedProviderSchemaAsync(CancellationToken.None);

        Assert.AreEqual(0, viewModel.Providers.Count);
        Assert.IsNull(viewModel.SelectedProvider);
    }

    [TestMethod]
    public async Task LoadProvidersAsync_HidesUnknownProvidersUntilShowMissingProvidersIsEnabled()
    {
        var provider = new EtwProviderInfo(
            "Unknown-Provider",
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            EtwProviderSchemaSource.Unknown);
        var viewModel = new ProvidersViewModel(new FakeProviderCatalog([provider]));

        await viewModel.LoadProvidersAsync(CancellationToken.None);

        Assert.AreEqual(0, viewModel.Providers.Count);
        Assert.IsNull(viewModel.SelectedProvider);

        viewModel.ShowMissingProviders = true;

        Assert.AreEqual(1, viewModel.Providers.Count);
        Assert.AreEqual(provider, viewModel.Providers[0]);
    }

    [TestMethod]
    public void IsMissingStaticSchema_ReturnsFalseForTraceLoggingProviderWithEmptySchema()
    {
        var provider = new EtwProviderInfo(
            "TraceLogging-Provider",
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            EtwProviderSchemaSource.TraceLogging);
        var schema = new EtwProviderSchema(provider, [], []);

        Assert.IsFalse(ProvidersViewModel.IsMissingStaticSchema(provider, schema));
    }

    private sealed class FakeProviderCatalog(IReadOnlyList<EtwProviderInfo> providers) : IEtwProviderCatalog
    {
        public Task<IReadOnlyList<EtwProviderInfo>> EnumerateProvidersAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(providers);
        }

        public Task<EtwProviderSchema> GetProviderSchemaAsync(
            EtwProviderInfo provider,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new EtwProviderSchema(provider, [], []));
        }
    }
}
