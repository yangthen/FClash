using System.Runtime.InteropServices;

namespace FClash.WinUI;

/// <summary>Win32 notification icon; no Windows Forms, WebView or additional UI process.</summary>
internal sealed class NativeTray : IDisposable
{
    private const uint Callback = 0x8001;
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly nint window;
    private readonly SubclassProc callback;
    private readonly Action show, exit, toggle;
    private NotifyData data;
    private nint icon;
    private bool disposed;
    public NativeTray(nint window, Action show, Action exit, Action toggle)
    {
        this.window = window; this.show = show; this.exit = exit; this.toggle = toggle;
        callback = WindowProc;
        if (!SetWindowSubclass(window, callback, 1, 0)) throw new InvalidOperationException("无法初始化托盘消息");
        icon = LoadImage(0, Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"), 1, 32, 32, 0x10);
        data = new() { Size = (uint)Marshal.SizeOf<NotifyData>(), Window = window, Id = 1, Flags = 1 | 2 | 4, Callback = Callback, Icon = icon, Tip = "FClash" };
        if (!ShellNotifyIcon(0, ref data)) { Dispose(); throw new InvalidOperationException("无法创建托盘图标"); }
    }
    private nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint reference)
    {
        if (message == taskbarCreated && !disposed) ShellNotifyIcon(0, ref data);
        if (message == Callback)
        {
            if ((uint)lParam is 0x0203 or 0x0202) show();
            if ((uint)lParam == 0x0205)
            {
                var menu = CreatePopupMenu();
                try
                {
                    AppendMenu(menu, 0, 1, "显示 FClash"); AppendMenu(menu, 0, 2, "启动 / 停止代理"); AppendMenu(menu, 0x800, 0, null); AppendMenu(menu, 0, 3, "退出");
                    GetCursorPos(out var point); SetForegroundWindow(window);
                    var selected = TrackPopupMenu(menu, 0x100 | 2, point.X, point.Y, 0, window, 0);
                    if (selected == 1) show(); if (selected == 2) toggle(); if (selected == 3) exit();
                }
                finally { DestroyMenu(menu); }
            }
            return 0;
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        ShellNotifyIcon(2, ref data); RemoveWindowSubclass(window, callback, 1); if (icon != 0) DestroyIcon(icon); icon = 0;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyData
    {
        public uint Size; public nint Window; public uint Id, Flags, Callback; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    private delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint reference);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellNotifyIcon(uint message, ref NotifyData data);
    [DllImport("comctl32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nuint reference);
    [DllImport("comctl32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode)] private static extern nint LoadImage(nint instance, string name, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool AppendMenu(nint menu, uint flags, nuint id, string? text);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint window, nint rect);
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
}
