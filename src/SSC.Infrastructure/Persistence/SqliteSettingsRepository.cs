using System.Globalization;
using SSC.Application.Abstractions;

namespace SSC.Infrastructure.Persistence;

public sealed class SqliteSettingsRepository : ISettingsRepository
{
    private readonly SqliteConnectionFactory _factory;
    private readonly IClock _clock;

    public SqliteSettingsRepository(SqliteConnectionFactory factory, IClock clock)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public string? Read(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", key);

        return command.ExecuteScalar() as string;
    }

    public void Write(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        using var connection = _factory.Create();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO settings(key, value, updated_at)
            VALUES ($key, $value, $updatedAt)
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$updatedAt", _clock.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }
}
