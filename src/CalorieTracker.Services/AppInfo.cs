namespace CalorieTracker.Services;

public static class AppInfo
{
    public static string Version =>
        typeof(AppInfo).Assembly.GetName().Version?.ToString(4) ?? "0.0.0.0";

    public static string DisplayVersion => $"v{Version}";
}
