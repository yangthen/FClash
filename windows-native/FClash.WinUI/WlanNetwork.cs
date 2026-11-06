using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace FClash.WinUI;

internal static class WlanNetwork
{
    public static string? GetSsid()
    {
        var error = WlanOpenHandle(2, 0, out _, out var client);
        if (error != 0) throw new Win32Exception((int)error);
        try
        {
            error = WlanEnumInterfaces(client, 0, out var interfaces);
            if (error != 0) throw new Win32Exception((int)error);
            try
            {
                var count = Math.Min(Marshal.ReadInt32(interfaces), 64);
                for (var index = 0; index < count; index++)
                {
                    var info = interfaces + 8 + index * 532; // GUID + WCHAR[256] + state
                    if (Marshal.ReadInt32(info + 528) != 1) continue;
                    var id = Marshal.PtrToStructure<Guid>(info);
                    error = WlanQueryInterface(client, ref id, 7, 0, out var size, out var attributes, out _);
                    if (error != 0) throw new Win32Exception((int)error, "无法读取 Wi-Fi 名称。请检查 Windows 的位置权限，或清空按需连接排除列表。");
                    try
                    {
                        if (size < 556) throw new InvalidDataException("Wi-Fi 连接信息不完整");
                        var length = Marshal.ReadInt32(attributes + 520);
                        if (length is < 0 or > 32) throw new InvalidDataException("SSID 长度无效");
                        var bytes = new byte[length]; Marshal.Copy(attributes + 524, bytes, 0, length);
                        return Encoding.UTF8.GetString(bytes);
                    }
                    finally { WlanFreeMemory(attributes); }
                }
                return null;
            }
            finally { WlanFreeMemory(interfaces); }
        }
        finally { WlanCloseHandle(client, 0); }
    }
    [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint version, nint reserved, out uint negotiated, out nint client);
    [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(nint client, nint reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(nint client, nint reserved, out nint list);
    [DllImport("wlanapi.dll")] private static extern uint WlanQueryInterface(nint client, ref Guid id, int opcode, nint reserved, out uint size, out nint data, out int type);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(nint memory);
}
