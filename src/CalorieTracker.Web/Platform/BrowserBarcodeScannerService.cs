using CalorieTracker.Services;
using Microsoft.JSInterop;

namespace CalorieTracker.Web.Platform;

public sealed class BrowserBarcodeScannerService : IBarcodeScannerService, IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private DotNetObjectReference<BrowserBarcodeScannerService>? _dotNetRef;
    private Func<string, Task>? _callback;

    public BrowserBarcodeScannerService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task StartAsync(string containerElementId, Func<string, Task> onBarcodeDetected)
    {
        _callback = onBarcodeDetected;
        _dotNetRef?.Dispose();
        _dotNetRef = DotNetObjectReference.Create(this);
        await _js.InvokeVoidAsync("barcodeBridge.start", containerElementId, _dotNetRef);
    }

    [JSInvokable]
    public async Task OnBarcodeScanned(string barcode)
    {
        if (_callback is not null)
        {
            await _callback(barcode);
        }
    }

    public async Task StopAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("barcodeBridge.stop");
        }
        catch
        {
            // ignore when circuit/browser disconnects
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _dotNetRef?.Dispose();
    }
}

public sealed class BrowserFileDownloadService : IFileDownloadService
{
    private readonly IJSRuntime _js;

    public BrowserFileDownloadService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task DownloadBytesAsync(string fileName, string contentType, byte[] bytes)
    {
        var base64 = Convert.ToBase64String(bytes);
        await _js.InvokeVoidAsync("fileDownloadBridge.downloadBase64", fileName, contentType, base64);
    }
}

public sealed class BrowserThemeService : IThemeService
{
    private readonly IJSRuntime _js;

    public BrowserThemeService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task ApplyThemeAsync(
        CalorieTracker.Domain.Enums.ThemeMode theme,
        CalorieTracker.Domain.Enums.ColorScheme scheme)
    {
        var mode = theme == CalorieTracker.Domain.Enums.ThemeMode.Dark ? "dark" : "light";
        var accent = scheme switch
        {
            CalorieTracker.Domain.Enums.ColorScheme.RoyalBlue => "royal-blue",
            CalorieTracker.Domain.Enums.ColorScheme.Green => "green",
            CalorieTracker.Domain.Enums.ColorScheme.Purple => "purple",
            CalorieTracker.Domain.Enums.ColorScheme.Orange => "orange",
            CalorieTracker.Domain.Enums.ColorScheme.Red => "red",
            CalorieTracker.Domain.Enums.ColorScheme.Teal => "teal",
            CalorieTracker.Domain.Enums.ColorScheme.Rose => "rose",
            CalorieTracker.Domain.Enums.ColorScheme.Indigo => "indigo",
            _ => "royal-blue"
        };
        await _js.InvokeVoidAsync("themeBridge.apply", mode, accent);
    }
}

public sealed class BrowserAppUpdateService : IAppUpdateService, IDisposable
{
    private readonly IJSRuntime _js;
    private DotNetObjectReference<BrowserAppUpdateService>? _dotNetRef;
    private bool _watching;

    public event Action? UpdateAvailableChanged;
    public bool IsUpdateAvailable { get; private set; }

    public BrowserAppUpdateService(IJSRuntime js)
    {
        _js = js;
    }

    private sealed class JsUpdateCheckDto
    {
        public bool UpdateAvailable { get; set; }
        public bool IsSupported { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public async Task StartWatchingAsync()
    {
        if (_watching) return;
        _watching = true;
        _dotNetRef?.Dispose();
        _dotNetRef = DotNetObjectReference.Create(this);
        try
        {
            await _js.InvokeVoidAsync("pwaUpdateBridge.watchForUpdates", _dotNetRef);
        }
        catch
        {
            // ignore if JS not ready
        }
    }

    [JSInvokable]
    public void OnUpdateReady()
    {
        if (!IsUpdateAvailable)
        {
            IsUpdateAvailable = true;
            UpdateAvailableChanged?.Invoke();
        }
    }

    public async Task<AppUpdateCheckResult> CheckForUpdateAsync()
    {
        var dto = await _js.InvokeAsync<JsUpdateCheckDto>("pwaUpdateBridge.checkForUpdate");
        if (dto.UpdateAvailable && !IsUpdateAvailable)
        {
            IsUpdateAvailable = true;
            UpdateAvailableChanged?.Invoke();
        }
        return new AppUpdateCheckResult(dto.UpdateAvailable, dto.IsSupported, dto.Message);
    }

    public async Task ApplyUpdateAsync()
    {
        await _js.InvokeVoidAsync("pwaUpdateBridge.applyUpdate");
    }

    public void Dispose() => _dotNetRef?.Dispose();
}
