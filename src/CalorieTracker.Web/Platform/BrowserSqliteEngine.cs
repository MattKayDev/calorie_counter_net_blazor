using System.Text.Json;
using CalorieTracker.Data.Sqlite;
using Microsoft.JSInterop;

namespace CalorieTracker.Web.Platform;

public abstract class BrowserSqliteEngineBase : ISqliteEngine
{
    private readonly IJSRuntime _js;
    private readonly string _dbName;

    protected BrowserSqliteEngineBase(IJSRuntime js, string dbName)
    {
        _js = js;
        _dbName = dbName;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _js.InvokeVoidAsync("sqliteBridge.init", ct, _dbName);
    }

    public async Task ExecuteAsync(string sql, object?[]? parameters = null, CancellationToken ct = default)
    {
        await _js.InvokeVoidAsync("sqliteBridge.execute", ct, _dbName, sql, parameters ?? Array.Empty<object?>());
    }

    public async Task<List<Dictionary<string, JsonElement>>> QueryAsync(string sql, object?[]? parameters = null, CancellationToken ct = default)
    {
        return await _js.InvokeAsync<List<Dictionary<string, JsonElement>>>(
            "sqliteBridge.query", ct, _dbName, sql, parameters ?? Array.Empty<object?>());
    }

    public async Task<byte[]> ExportBytesAsync(CancellationToken ct = default)
    {
        var base64 = await _js.InvokeAsync<string>("sqliteBridge.exportBase64", ct, _dbName);
        return Convert.FromBase64String(base64);
    }

    public async Task ImportBytesAsync(byte[] bytes, CancellationToken ct = default)
    {
        var base64 = Convert.ToBase64String(bytes);
        await _js.InvokeVoidAsync("sqliteBridge.importBase64", ct, _dbName, base64);
    }
}

public sealed class PersonalBrowserSqliteEngine : BrowserSqliteEngineBase, IPersonalSqliteEngine
{
    public PersonalBrowserSqliteEngine(IJSRuntime js) : base(js, "personal") { }
}

public sealed class FoodsBrowserSqliteEngine : BrowserSqliteEngineBase, IFoodsSqliteEngine
{
    public FoodsBrowserSqliteEngine(IJSRuntime js) : base(js, "foods") { }
}
