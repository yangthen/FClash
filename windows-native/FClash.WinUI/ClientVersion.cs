using System.Reflection;

namespace FClash.WinUI;

internal static class ClientVersion
{
    public static string Label => typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(App).Assembly.GetName().Version?.ToString(3) ?? "unknown";
    public static string UserAgent => "FClash/" + Label;
}
