using System.Globalization;
using EtwSuite.Core;
using Microsoft.Data.Sqlite;

namespace EtwSuite.Etw;

public sealed class SqliteEtwSessionTemplateStore : IEtwSessionTemplateStore
{
    public string? DatabasePath { get; private set; }

    public async Task InitializeAsync(string databasePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Select a saved sessions database.", nameof(databasePath));
        }

        string fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? ".");
        DatabasePath = fullPath;

        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await ExecuteNonQueryAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS schema_version (
                version INTEGER NOT NULL
            );

            INSERT INTO schema_version (version)
            SELECT 1
            WHERE NOT EXISTS (SELECT 1 FROM schema_version);

            CREATE TABLE IF NOT EXISTS session_templates (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE COLLATE NOCASE,
                provider_id TEXT NOT NULL,
                provider_name TEXT NOT NULL,
                event_filter_mode TEXT NOT NULL,
                event_filter_text TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_session_templates_updated_at
            ON session_templates(updated_at DESC);
            """,
            cancellationToken);
    }

    public async Task<IReadOnlyList<EtwSessionTemplate>> ListAsync(CancellationToken cancellationToken)
    {
        EnsureInitialized();

        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, name, provider_id, provider_name, event_filter_mode, event_filter_text, created_at, updated_at
            FROM session_templates
            ORDER BY updated_at DESC, name COLLATE NOCASE ASC
            """;

        var templates = new List<EtwSessionTemplate>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            templates.Add(ReadTemplate(reader));
        }

        return templates;
    }

    public async Task<EtwSessionTemplate?> GetAsync(long id, CancellationToken cancellationToken)
    {
        EnsureInitialized();

        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, name, provider_id, provider_name, event_filter_mode, event_filter_text, created_at, updated_at
            FROM session_templates
            WHERE id = $id
            """;
        command.Parameters.AddWithValue("$id", id);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadTemplate(reader) : null;
    }

    public async Task<EtwSessionTemplate> SaveAsync(EtwSessionTemplate template, CancellationToken cancellationToken)
    {
        EnsureInitialized();

        DateTimeOffset now = DateTimeOffset.UtcNow;
        EtwSessionTemplate normalized = template with
        {
            Name = template.Name.Trim(),
            ProviderName = template.ProviderName.Trim(),
            EventFilterText = template.EventFilterText.Trim(),
            CreatedAt = template.Id == 0 ? now : template.CreatedAt,
            UpdatedAt = now,
        };

        if (string.IsNullOrWhiteSpace(normalized.Name))
        {
            throw new InvalidOperationException("Template name is required.");
        }

        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        if (normalized.Id == 0)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO session_templates
                    (name, provider_id, provider_name, event_filter_mode, event_filter_text, created_at, updated_at)
                VALUES
                    ($name, $provider_id, $provider_name, $event_filter_mode, $event_filter_text, $created_at, $updated_at)
                RETURNING id
                """;
            AddTemplateParameters(command, normalized);
            object? id = await command.ExecuteScalarAsync(cancellationToken);
            return normalized with { Id = Convert.ToInt64(id, CultureInfo.InvariantCulture) };
        }

        await using SqliteCommand updateCommand = connection.CreateCommand();
        updateCommand.CommandText =
            """
            UPDATE session_templates
            SET name = $name,
                provider_id = $provider_id,
                provider_name = $provider_name,
                event_filter_mode = $event_filter_mode,
                event_filter_text = $event_filter_text,
                updated_at = $updated_at
            WHERE id = $id
            """;
        AddTemplateParameters(updateCommand, normalized);
        updateCommand.Parameters.AddWithValue("$id", normalized.Id);
        int changed = await updateCommand.ExecuteNonQueryAsync(cancellationToken);

        if (changed == 0)
        {
            throw new InvalidOperationException("The saved session no longer exists.");
        }

        return normalized;
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        EnsureInitialized();

        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM session_templates WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private SqliteConnection CreateConnection()
    {
        EnsureInitialized();
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        };

        return new SqliteConnection(builder.ToString());
    }

    private void EnsureInitialized()
    {
        if (string.IsNullOrWhiteSpace(DatabasePath))
        {
            throw new InvalidOperationException("Open or create a saved sessions database first.");
        }
    }

    private static void AddTemplateParameters(SqliteCommand command, EtwSessionTemplate template)
    {
        command.Parameters.AddWithValue("$name", template.Name);
        command.Parameters.AddWithValue("$provider_id", template.ProviderId.ToString("D"));
        command.Parameters.AddWithValue("$provider_name", template.ProviderName);
        command.Parameters.AddWithValue("$event_filter_mode", template.EventFilterMode.ToString());
        command.Parameters.AddWithValue("$event_filter_text", template.EventFilterText);
        command.Parameters.AddWithValue("$created_at", template.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updated_at", template.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
    }

    private static EtwSessionTemplate ReadTemplate(SqliteDataReader reader)
    {
        return new EtwSessionTemplate(
            reader.GetInt64(0),
            reader.GetString(1),
            Guid.Parse(reader.GetString(2)),
            reader.GetString(3),
            Enum.TryParse(reader.GetString(4), ignoreCase: true, out EtwFilterMode mode) ? mode : EtwFilterMode.Basic,
            reader.GetString(5),
            DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }
}
