using System.Globalization;
using System.Text.Json;

namespace CalorieTracker.Data.Sqlite;

/// <summary>
/// Platform-agnostic SQLite execution engine.
/// Implemented via sql.js + IndexedDB in Blazor WebAssembly,
/// and via Microsoft.Data.Sqlite in .NET MAUI / tests.
/// </summary>
public interface ISqliteEngine
{
    Task InitializeAsync(CancellationToken ct = default);
    Task ExecuteAsync(string sql, object?[]? parameters = null, CancellationToken ct = default);
    Task<List<Dictionary<string, JsonElement>>> QueryAsync(string sql, object?[]? parameters = null, CancellationToken ct = default);
    Task<byte[]> ExportBytesAsync(CancellationToken ct = default);
    Task ImportBytesAsync(byte[] bytes, CancellationToken ct = default);
}

internal static class RowReader
{
    public static string GetString(Dictionary<string, JsonElement> row, string col, string fallback = "")
    {
        if (!row.TryGetValue(col, out var el) || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return fallback;
        return el.ValueKind == JsonValueKind.String ? el.GetString() ?? fallback : el.ToString();
    }

    public static string? GetNullableString(Dictionary<string, JsonElement> row, string col)
    {
        if (!row.TryGetValue(col, out var el) || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
    }

    public static double GetDouble(Dictionary<string, JsonElement> row, string col, double fallback = 0)
    {
        if (!row.TryGetValue(col, out var el) || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return fallback;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d))
            return d;
        if (el.ValueKind == JsonValueKind.String && double.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return fallback;
    }

    public static double? GetNullableDouble(Dictionary<string, JsonElement> row, string col)
    {
        if (!row.TryGetValue(col, out var el) || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d))
            return d;
        if (el.ValueKind == JsonValueKind.String && double.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    public static int GetInt(Dictionary<string, JsonElement> row, string col, int fallback = 0)
    {
        if (!row.TryGetValue(col, out var el) || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return fallback;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i))
            return i;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d))
            return (int)d;
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return fallback;
    }

    public static bool GetBool(Dictionary<string, JsonElement> row, string col) => GetInt(row, col) != 0;

    public static Guid GetGuid(Dictionary<string, JsonElement> row, string col) =>
        Guid.TryParse(GetString(row, col), out var g) ? g : Guid.Empty;

    public static Guid? GetNullableGuid(Dictionary<string, JsonElement> row, string col)
    {
        var s = GetNullableString(row, col);
        return !string.IsNullOrWhiteSpace(s) && Guid.TryParse(s, out var g) ? g : null;
    }

    public static DateOnly GetDateOnly(Dictionary<string, JsonElement> row, string col) =>
        DateOnly.TryParse(GetString(row, col), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : DateOnly.FromDateTime(DateTime.Today);

    public static DateTime GetDateTime(Dictionary<string, JsonElement> row, string col) =>
        DateTime.TryParse(GetString(row, col), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            ? dt
            : DateTime.UtcNow;
}
