namespace EtwSuite.Core;

public interface IEtwProviderCatalog
{
    Task<IReadOnlyList<EtwProviderInfo>> EnumerateProvidersAsync(CancellationToken cancellationToken);

    Task<EtwProviderSchema> GetProviderSchemaAsync(
        EtwProviderInfo provider,
        CancellationToken cancellationToken);
}
