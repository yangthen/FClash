using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using FClash.Domain;

namespace FClash.WinUI;

internal sealed class Hotkeys : IDisposable
{
    private readonly nint window;
    private readonly Callback callback;
    private readonly Action<string> execute;
    private readonly Dictionary<int, string> actions = [];
    public Hotkeys(nint window, Action<string> execute)
    {
        this.window = window; this.execute = execute; callback = WindowProc;
        if (!SetWindowSubclass(window, callback, 2, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public void Set(JsonArray? bindings)
    {
        foreach (var id in actions.Keys) UnregisterHotKey(window, id); actions.Clear();
        if (bindings == null) return;
        try
        {
            foreach (var binding in bindings.OfType<JsonObject>())
            {
                var key = binding["key"]?.GetValue<int>() ?? 0;
                // Flutter physical USB HID usages (0x00070004 = A) map to Win32 VKeys.
                var usage = key & 0xffff;
                uint vk = usage switch { >= 4 and <= 29 => (uint)('A' + usage - 4), >= 30 and <= 38 => (uint)('1' + usage - 30), 39 => '0', >= 58 and <= 69 => (uint)(0x70 + usage - 58), 44 => 0x20, _ => 0 };
                if (vk == 0) continue;
                uint modifiers = 0x4000;
                foreach (var modifier in (binding["modifiers"] as JsonArray ?? []))
                    modifiers |= modifier.Text() switch { "alt" => 1u, "control" => 2u, "shift" => 4u, "meta" => 8u, _ => 0u };
                if (modifiers == 0x4000) continue;
                var id = 0x5000 + actions.Count;
                if (!RegisterHotKey(window, id, modifiers, vk)) throw new Win32Exception(Marshal.GetLastWin32Error(), "快捷键被占用，请更换组合");
                actions[id] = binding["action"].Text();
            }
        }
        catch { foreach (var id in actions.Keys) UnregisterHotKey(window, id); actions.Clear(); throw; }
    }
    private nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint reference)
    {
        if (message == 0x0312 && actions.TryGetValue((int)wParam, out var action)) { execute(action); return 0; }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }
    public void Dispose() { foreach (var id in actions.Keys) UnregisterHotKey(window, id); actions.Clear(); RemoveWindowSubclass(window, callback, 2); }
    private delegate nint Callback(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint reference);
    [DllImport("user32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("comctl32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(nint hwnd, Callback callback, nuint id, nuint reference);
    [DllImport("comctl32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(nint hwnd, Callback callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
}
