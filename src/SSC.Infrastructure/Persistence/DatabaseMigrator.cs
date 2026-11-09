using System.Globalization;
using Microsoft.Data.Sqlite;

namespace SSC.Infrastructure.Persistence;

public sealed class DatabaseMigrator
{
    private static readonly Migration[] Migrations =
    {
        new Migration(1, "initial-schema", ApplyInitialSchema),
        new Migration(2, "history-fts", ApplyHistoryFts),
        new Migration(3, "history-favorite", ApplyHistoryFavorite)
    };

    private readonly SqliteConnectionFactory _factory;

    public DatabaseMigrator(SqliteConnectionFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public void Migrate()
    {
        using var connection = _factory.Create();
        EnableWriteAheadLog(connection);
        using var transaction = connection.BeginTransaction();

        EnsureMigrationTable(connection, transaction);
        var applied = GetAppliedVersions(connection, transaction);

        foreach (var migration in Migrations.OrderBy(static migration => migration.Version))
        {
            if (applied.Contains(migration.Version))
            {
                continue;
            }

            migration.Apply(connection, transaction);

            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                "INSERT INTO schema_migrations(version, applied_at) VALUES ($version, $appliedAt);";
            insert.Parameters.AddWithValue("$version", migration.Version);
            insert.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void EnableWriteAheadLog(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL;";
        command.ExecuteScalar();
    }

    private static void EnsureMigrationTable(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER PRIMARY KEY,
                applied_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static HashSet<int> GetAppliedVersions(SqliteConnection connection, SqliteTransaction transaction)
    {
        var versions = new HashSet<int>();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT version FROM schema_migrations;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            versions.Add(reader.GetInt32(0));
        }

        return versions;
    }

    private static void ApplyInitialSchema(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS history_entries (
                id TEXT PRIMARY KEY,
                kind INTEGER NOT NULL,
                title TEXT NOT NULL,
                file_path TEXT NOT NULL,
                created_at TEXT NOT NULL,
                width INTEGER NOT NULL,
                height INTEGER NOT NULL,
                duration_ms INTEGER NULL,
                tags_json TEXT NOT NULL DEFAULT '[]'
            );

            CREATE INDEX IF NOT EXISTS ix_history_entries_created_at
                ON history_entries(created_at DESC);
            """;
        command.ExecuteNonQuery();
    }

    private sealed record Migration(int Version, string Name, Action<SqliteConnection, SqliteTransaction> Apply);

    private static void ApplyHistoryFavorite(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            ALTER TABLE history_entries ADD COLUMN is_favorite INTEGER NOT NULL DEFAULT 0;
            CREATE INDEX IF NOT EXISTS ix_history_entries_favorite
                ON history_entries(is_favorite, created_at DESC);
            """;
        command.ExecuteNonQuery();
    }

    private static void ApplyHistoryFts(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            ALTER TABLE history_entries ADD COLUMN ocr_text TEXT NOT NULL DEFAULT '';

            CREATE VIRTUAL TABLE history_fts USING fts5(
                id UNINDEXED,
                title,
                tags,
                ocr_text,
                tokenize = 'unicode61 remove_diacritics 2',
                prefix = '2 3'
            );

            INSERT INTO history_fts(id, title, tags, ocr_text)
                SELECT id, title, tags_json, ocr_text FROM history_entries;

            CREATE TRIGGER history_entries_ai AFTER INSERT ON history_entries BEGIN
                INSERT INTO history_fts(id, title, tags, ocr_text)
                VALUES (new.id, new.title, new.tags_json, new.ocr_text);
            END;

            CREATE TRIGGER history_entries_ad AFTER DELETE ON history_entries BEGIN
                DELETE FROM history_fts WHERE id = old.id;
            END;

            CREATE TRIGGER history_entries_au AFTER UPDATE ON history_entries BEGIN
                DELETE FROM history_fts WHERE id = old.id;
                INSERT INTO history_fts(id, title, tags, ocr_text)
                VALUES (new.id, new.title, new.tags_json, new.ocr_text);
            END;
            """;
        command.ExecuteNonQuery();
    }
}
