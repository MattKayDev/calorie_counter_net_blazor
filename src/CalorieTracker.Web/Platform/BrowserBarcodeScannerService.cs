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

    public async Task ApplyThemeAsync(CalorieTracker.Domain.Enums.ThemeMode theme)
    {
        var mode = theme == CalorieTracker.Domain.Enums.ThemeMode.Dark ? "dark" : "light";
        await _js.InvokeVoidAsync("themeBridge.apply", mode);
    }
}
