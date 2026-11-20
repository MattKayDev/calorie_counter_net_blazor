using CalorieTracker.Data.Sqlite;
using CalorieTracker.Services;
using CalorieTracker.Web;
using CalorieTracker.Web.Platform;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Platform adapters (swappable for .NET MAUI Blazor Hybrid later)
builder.Services.AddScoped<ISqliteEngine, BrowserSqliteEngine>();
builder.Services.AddScoped<IBarcodeScannerService, BrowserBarcodeScannerService>();
builder.Services.AddScoped<IFileDownloadService, BrowserFileDownloadService>();
builder.Services.AddScoped<IThemeService, BrowserThemeService>();

// Local SQLite repositories & application services
builder.Services.AddSqliteRepositories();
builder.Services.AddCalorieTrackerServices();

await builder.Build().RunAsync();
