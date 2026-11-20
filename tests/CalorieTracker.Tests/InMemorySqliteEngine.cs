using System.Text.Json;
using CalorieTracker.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace CalorieTracker.Tests;

/// <summary>
/// Real SQLite engine backed by Microsoft.Data.Sqlite (in-memory) for fast, authentic integration tests.
/// This is also the exact pattern a future .NET MAUI Blazor Hybrid host can use for native SQLite!
/// </summary>
internal class InMemorySqliteEngine : ISqliteEngine, IDisposable
{
    private readonly SqliteConnection _conn;

    public InMemorySqliteEngine()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
    }

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task ExecuteAsync(string sql, object?[]? parameters = null, CancellationToken ct = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        BindParameters(cmd, parameters);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<Dictionary<string, JsonElement>>> QueryAsync(string sql, object?[]? parameters = null, CancellationToken ct = default)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        BindParameters(cmd, parameters);

        var rows = new List<Dictionary<string, JsonElement>>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                dict[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            var json = JsonSerializer.Serialize(dict);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
            rows.Add(parsed);
        }
        return rows;
    }

    public Task<byte[]> ExportBytesAsync(CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
    public Task ImportBytesAsync(byte[] bytes, CancellationToken ct = default) => Task.CompletedTask;

    private static void BindParameters(SqliteCommand cmd, object?[]? parameters)
    {
        if (parameters is null) return;
        for (var i = 0; i < parameters.Length; i++)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = $"$p{i}";
            p.Value = parameters[i] ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        // Replace positional ? placeholders with $p0, $p1, ...
        if (parameters.Length > 0 && cmd.CommandText.Contains('?'))
        {
            var idx = 0;
            var chars = cmd.CommandText;
            var sb = new System.Text.StringBuilder(chars.Length + parameters.Length * 3);
            foreach (var c in chars)
            {
                if (c == '?' && idx < parameters.Length)
                    sb.Append($"$p{idx++}");
                else
                    sb.Append(c);
            }
            cmd.CommandText = sb.ToString();
        }
    }

    public void Dispose() => _conn.Dispose();
}

internal sealed class InMemoryPersonalSqliteEngine : InMemorySqliteEngine, IPersonalSqliteEngine { }
internal sealed class InMemoryFoodsSqliteEngine : InMemorySqliteEngine, IFoodsSqliteEngine { }
