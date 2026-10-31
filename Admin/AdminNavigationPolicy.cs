namespace ChihDesktop;

public static class AdminNavigationPolicy
{
    public const string Host = "chih-admin.invalid";
    public const string StartUrl = "https://chih-admin.invalid/index.html";
    public const string RuntimeHelp = "https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution";
    public static bool IsLocal(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.Host == Host && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo);
    public static bool IsApprovedExternal(string? value) => value == RuntimeHelp;
}
