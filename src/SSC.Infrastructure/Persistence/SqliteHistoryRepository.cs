using System.Globalization;
using System.Text.Json;
using SSC.Application.Abstractions;
using SSC.Domain.Capture;
using SSC.Domain.History;

namespace SSC.Infrastructure.Persistence;

public sealed class SqliteHistoryRepository : IHistoryRepository
{
    private readonly SqliteConnectionFactory _factory;

    public SqliteHistoryRepository(SqliteConnectionFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public IReadOnlyList<HistoryEntry> List(int limit = 100, int offset = 0)
    {
        limit = Math.Clamp(limit, 1, 1000);
        offset = Math.Max(0, offset);

        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, kind, title, file_path, created_at, width, height, duration_ms, tags_json, ocr_text
            FROM history_entries
            ORDER BY created_at DESC
            LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);

        var entries = new List<HistoryEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(Map(reader));
        }

        return entries;
    }

    public IReadOnlyList<HistoryEntry> Query(HistoryQuery query)
    {
        var normalized = query.Normalize();
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        var useMatch = !string.IsNullOrWhiteSpace(normalized.Search);

        command.CommandText = useMatch
            ? """
              SELECT e.id, e.kind, e.title, e.file_path, e.created_at, e.width, e.height, e.duration_ms, e.tags_json, e.ocr_text
              FROM history_fts f
              JOIN history_entries e ON e.id = f.id
              WHERE history_fts MATCH $match
              """
            : """
              SELECT e.id, e.kind, e.title, e.file_path, e.created_at, e.width, e.height, e.duration_ms, e.tags_json, e.ocr_text
              FROM history_entries e
              WHERE 1 = 1
              """;

        if (useMatch)
        {
            command.Parameters.AddWithValue("$match", BuildMatch(normalized.Search!));
        }

        if (normalized.Kind is not null)
        {
            command.CommandText += " AND e.kind = $kind";
            command.Parameters.AddWithValue("$kind", (int)normalized.Kind.Value);
        }

        if (normalized.Since is not null)
        {
            command.CommandText += " AND e.created_at >= $since";
            command.Parameters.AddWithValue(
                "$since",
                normalized.Since.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        }

        command.CommandText += " ORDER BY e.created_at DESC LIMIT $limit OFFSET $offset;";
        command.Parameters.AddWithValue("$limit", normalized.Limit);
        command.Parameters.AddWithValue("$offset", normalized.Offset);

        var entries = new List<HistoryEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(Map(reader));
        }

        return entries;
    }

    public int Count(HistoryQuery query)
    {
        var normalized = query.Normalize();
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        var useMatch = !string.IsNullOrWhiteSpace(normalized.Search);

        command.CommandText = useMatch
            ? """
              SELECT COUNT(*)
              FROM history_fts f
              JOIN history_entries e ON e.id = f.id
              WHERE history_fts MATCH $match
              """
            : "SELECT COUNT(*) FROM history_entries e WHERE 1 = 1";

        if (useMatch)
        {
            command.Parameters.AddWithValue("$match", BuildMatch(normalized.Search!));
        }

        if (normalized.Kind is not null)
        {
            command.CommandText += " AND e.kind = $kind";
            command.Parameters.AddWithValue("$kind", (int)normalized.Kind.Value);
        }

        if (normalized.Since is not null)
        {
            command.CommandText += " AND e.created_at >= $since";
            command.Parameters.AddWithValue(
                "$since",
                normalized.Since.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        }

        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public HistoryEntry? Get(Guid id)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, kind, title, file_path, created_at, width, height, duration_ms, tags_json, ocr_text
            FROM history_entries
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));

        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void Add(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO history_entries
                (id, kind, title, file_path, created_at, width, height, duration_ms, tags_json, ocr_text)
            VALUES
                ($id, $kind, $title, $filePath, $createdAt, $width, $height, $durationMs, $tagsJson, $ocrText);
            """;
        Bind(command, entry);
        command.ExecuteNonQuery();
    }

    public void Update(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE history_entries SET
                kind = $kind,
                title = $title,
                file_path = $filePath,
                created_at = $createdAt,
                width = $width,
                height = $height,
                duration_ms = $durationMs,
                tags_json = $tagsJson,
                ocr_text = $ocrText
            WHERE id = $id;
            """;
        Bind(command, entry);
        command.ExecuteNonQuery();
    }

    public void Delete(Guid id)
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM history_entries WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.ExecuteNonQuery();
    }

    public int Count()
    {
        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM history_entries;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void Bind(Microsoft.Data.Sqlite.SqliteCommand command, HistoryEntry entry)
    {
        command.Parameters.AddWithValue("$id", entry.Id.ToString("D"));
        command.Parameters.AddWithValue("$kind", (int)entry.Kind);
        command.Parameters.AddWithValue("$title", entry.Title);
        command.Parameters.AddWithValue("$filePath", entry.FilePath);
        command.Parameters.AddWithValue("$createdAt", entry.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$width", entry.Width);
        command.Parameters.AddWithValue("$height", entry.Height);
        command.Parameters.AddWithValue(
            "$durationMs",
            entry.Duration is null ? DBNull.Value : (long)entry.Duration.Value.TotalMilliseconds);
        command.Parameters.AddWithValue("$tagsJson", JsonSerializer.Serialize(entry.Tags));
        command.Parameters.AddWithValue("$ocrText", entry.OcrText);
    }

    private static HistoryEntry Map(Microsoft.Data.Sqlite.SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(reader.GetString(0)),
        Kind = (CaptureKind)reader.GetInt32(1),
        Title = reader.GetString(2),
        FilePath = reader.GetString(3),
        CreatedAt = DateTimeOffset.Parse(
            reader.GetString(4),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind),
        Width = reader.GetInt32(5),
        Height = reader.GetInt32(6),
        Duration = reader.IsDBNull(7) ? null : TimeSpan.FromMilliseconds(reader.GetInt64(7)),
        Tags = JsonSerializer.Deserialize<string[]>(reader.GetString(8)) ?? Array.Empty<string>(),
        OcrText = reader.GetString(9)
    };

    private static string BuildMatch(string search)
    {
        var terms = search
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(term => "\"" + term.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"*")
            .ToArray();
        return terms.Length == 0 ? "\"\"" : string.Join(" AND ", terms);
    }
}
