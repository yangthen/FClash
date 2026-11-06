using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace FClash.WinUI;

/// <summary>Restore only settings owned by this session; never overwrite a later third-party change.</summary>
public sealed class SystemProxy(string directory, bool safeMode)
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private string Journal => Path.Combine(directory, "proxy-recovery.json");
    private string? owned;
    public bool Enabled => owned != null;
    public sealed record Snapshot(int? Enabled, string? Server, string? Bypass, string OwnedServer, string? PreviousOwnedServer = null);
    private void SaveJournal(Snapshot snapshot)
    {
        var temporary = Journal + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot)); File.Move(temporary, Journal, true);
    }
    [DllImport("wininet.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InternetSetOption(nint internet, int option, nint buffer, int length);
    private static void Notify()
    {
        InternetSetOption(0, 39, 0, 0); InternetSetOption(0, 37, 0, 0);
    }
    public void Enable(int port, JsonArray? bypass)
    {
        if (safeMode) throw new InvalidOperationException("安全模式禁止修改系统代理");
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true) ?? throw new IOException("无法打开系统代理设置");
        var server = "127.0.0.1:" + port;
        if (owned == null)
        {
            var snapshot = new Snapshot(key.GetValue("ProxyEnable") as int?, key.GetValue("ProxyServer") as string,
                key.GetValue("ProxyOverride") as string, server);
            SaveJournal(snapshot);
        }
        else if (owned != server)
        {
            var snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(Journal)) ?? throw new InvalidDataException("代理恢复记录无效");
            SaveJournal(snapshot with { OwnedServer = server, PreviousOwnedServer = owned });
        }
        owned = server;
        key.SetValue("ProxyServer", server); key.SetValue("ProxyOverride", string.Join(';', bypass?.Select(x => x?.ToString()) ?? ["<local>", "localhost", "127.*", "10.*", "192.168.*"]));
        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord); Notify();
    }
    public void Recover() { if (!safeMode) Restore(); }
    public void Restore()
    {
        if (safeMode || !File.Exists(Journal)) return;
        var snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(Journal)) ?? throw new InvalidDataException("代理恢复记录无效");
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true) ?? throw new IOException("无法恢复系统代理设置");
        var current = key.GetValue("ProxyServer") as string;
        if ((current == snapshot.OwnedServer || current == snapshot.PreviousOwnedServer && current != null) && key.GetValue("ProxyEnable") as int? == 1)
        {
            static void Set(RegistryKey key, string name, object? value)
            {
                if (value == null) key.DeleteValue(name, false); else key.SetValue(name, value);
            }
            Set(key, "ProxyEnable", snapshot.Enabled); Set(key, "ProxyServer", snapshot.Server); Set(key, "ProxyOverride", snapshot.Bypass); Notify();
        }
        File.Delete(Journal); owned = null;
    }
}
