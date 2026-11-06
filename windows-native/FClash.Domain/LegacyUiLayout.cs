namespace FClash.Domain;

/// <summary>Layout contract ported from Flutter's DashboardView, Grid and constants.</summary>
public static class LegacyUiLayout
{
    public const double RailWidth = 80, AppBarHeight = 56, Inset = 16, Gap = 14, CardRadius = 14;
    public static readonly string[] DashboardDefaults = ["networkSpeed", "systemProxyButton", "tunButton", "outboundMode", "networkDetection", "trafficUsage", "intranetIp"];
    public static readonly string[] DesktopNavigation = ["dashboard", "proxies", "profiles", "requests", "connections", "logs", "tools"];
    public static int ProfileColumns(double contentWidth) => Math.Max((int)Math.Floor(contentWidth / 280), 1);
    public static int ProxyColumns(double contentWidth, string layout = "standard")
    {
        var columns = Math.Max((int)Math.Ceiling(contentWidth / 250), 2);
        return layout switch { "tight" => columns + 1, "loose" => columns - 1, _ => columns };
    }
    public sealed record Tile(string Key, double X, double Y, double Width, double Height);
    public static IReadOnlyList<Tile> Dashboard(double contentWidth, IEnumerable<string> order)
    {
        var columns = Math.Max(4 * (int)Math.Ceiling(contentWidth / 280), 8);
        var width = Math.Max(contentWidth - Inset * 2, 0);
        var stride = (width + Gap) / columns;
        var offsets = new double[columns];
        var result = new List<Tile>();
        foreach (var key in order.Distinct())
        {
            var span = Math.Min(key is "networkSpeed" or "outboundModeV2" ? 8 : 4, columns);
            var height = key is "networkSpeed" or "outboundMode" or "trafficUsage" ? 174 : 80;
            var best = double.PositiveInfinity; var start = 0;
            for (var i = 0; i <= columns - span; i++)
            {
                var y = offsets.Skip(i).Take(span).Max();
                if (y < best) { best = y; start = i; }
            }
            result.Add(new(key, start * stride, best, span * stride - Gap, height));
            for (var i = start; i < start + span; i++) offsets[i] = best + height + Gap;
        }
        return result;
    }
}
