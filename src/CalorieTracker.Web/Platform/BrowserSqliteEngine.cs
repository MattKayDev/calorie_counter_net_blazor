using System.Text.Json;
using CalorieTracker.Data.Sqlite;
using Microsoft.JSInterop;

namespace CalorieTracker.Web.Platform;

public sealed class BrowserSqliteEngine : ISqliteEngine
{
    private readonly IJSRuntime _js;

    public BrowserSqliteEngine(IJSRuntime js)
    {
        _js = js;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _js.InvokeVoidAsync("sqliteBridge.init", ct);
    }

    public async Task ExecuteAsync(string sql, object?[]? parameters = null, CancellationToken ct = default)
    {
        await _js.InvokeVoidAsync("sqliteBridge.execute", ct, sql, parameters ?? Array.Empty<object?>());
    }

    public async Task<List<Dictionary<string, JsonElement>>> QueryAsync(string sql, object?[]? parameters = null, CancellationToken ct = default)
    {
        return await _js.InvokeAsync<List<Dictionary<string, JsonElement>>>(
            "sqliteBridge.query", ct, sql, parameters ?? Array.Empty<object?>());
    }

    public async Task<byte[]> ExportBytesAsync(CancellationToken ct = default)
    {
        var base64 = await _js.InvokeAsync<string>("sqliteBridge.exportBase64", ct);
        return Convert.FromBase64String(base64);
    }

    public async Task ImportBytesAsync(byte[] bytes, CancellationToken ct = default)
    {
        var base64 = Convert.ToBase64String(bytes);
        await _js.InvokeVoidAsync("sqliteBridge.importBase64", ct, base64);
    }
}
