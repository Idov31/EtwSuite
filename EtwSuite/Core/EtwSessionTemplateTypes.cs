namespace EtwSuite.Core;

public sealed record EtwSessionTemplate(
    long Id,
    string Name,
    Guid ProviderId,
    string ProviderName,
    EtwFilterMode EventFilterMode,
    string EventFilterText,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public interface IEtwSessionTemplateStore
{
    string? DatabasePath { get; }

    Task InitializeAsync(string databasePath, CancellationToken cancellationToken);

    Task<IReadOnlyList<EtwSessionTemplate>> ListAsync(CancellationToken cancellationToken);

    Task<EtwSessionTemplate?> GetAsync(long id, CancellationToken cancellationToken);

    Task<EtwSessionTemplate> SaveAsync(EtwSessionTemplate template, CancellationToken cancellationToken);

    Task DeleteAsync(long id, CancellationToken cancellationToken);
}

public interface IEtwSessionTemplateSettings
{
    Task<string?> LoadDatabasePathAsync(CancellationToken cancellationToken);

    Task SaveDatabasePathAsync(string databasePath, CancellationToken cancellationToken);
}
