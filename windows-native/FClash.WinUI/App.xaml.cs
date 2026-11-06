using FClash.Domain;
using Microsoft.UI.Xaml;

namespace FClash.WinUI;

public partial class App : Application
{
    private MainWindow? window;
    private Mutex? instance;
    private EventWaitHandle? activate;
    private RegisteredWaitHandle? activationWait;
    private string? diagnostics;
    public App()
    {
        diagnostics = LaunchOptions.Read(Environment.GetCommandLineArgs().Skip(1).ToArray()).DataDirectory;
        Directory.CreateDirectory(diagnostics);
        UnhandledException += (_, e) =>
        {
            if (diagnostics != null) File.AppendAllText(Path.Combine(diagnostics, "error.log"), e.Exception + Environment.NewLine);
        };
        DebugSettings.XamlResourceReferenceFailed += (_, e) => File.AppendAllText(Path.Combine(diagnostics, "error.log"), e.Message + Environment.NewLine);
        try { InitializeComponent(); }
        catch (Exception e) { File.AppendAllText(Path.Combine(diagnostics, "error.log"), e + Environment.NewLine); throw; }
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var waitAt = Array.IndexOf(arguments, "--wait-for-exit");
        if (waitAt >= 0 && waitAt + 1 < arguments.Length && int.TryParse(arguments[waitAt + 1], out var owner))
        {
            try { using var process = System.Diagnostics.Process.GetProcessById(owner); process.WaitForExit(15000); }
            catch (ArgumentException) { }
        }
        var options = LaunchOptions.Read(arguments);
        diagnostics = options.DataDirectory;
        Directory.CreateDirectory(diagnostics);
        if (!options.SafeMode)
        {
            foreach (var argument in arguments)
                if (Uri.TryCreate(argument, UriKind.Absolute, out var uri) && uri.Scheme is "fclash" or "flclash" or "clash" or "clashmeta")
                {
                    var queue = Path.Combine(options.DataDirectory, "incoming"); Directory.CreateDirectory(queue);
                    File.WriteAllText(Path.Combine(queue, Guid.NewGuid().ToString("N") + ".json"), System.Text.Json.JsonSerializer.Serialize(argument));
                }
        }
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(options.DataDirectory)));
        instance = new Mutex(true, @"Local\FClashNative_" + key, out var created);
        activate = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\FClashNative_activate_" + key);
        if (!created) { activate.Set(); Exit(); return; }
        try { window = new MainWindow(new ClientSession(options)); }
        catch (Exception e) { File.AppendAllText(Path.Combine(diagnostics, "error.log"), e + Environment.NewLine); Exit(); return; }
        activationWait = ThreadPool.RegisterWaitForSingleObject(activate, (_, _) => window.DispatcherQueue.TryEnqueue(() =>
        { window.AppWindow.Show(); window.Activate(); _ = window.HandleActivationsAsync(); }), null, Timeout.Infinite, false);
        window.Activate();
    }
}

public sealed record LaunchOptions(bool SafeMode, string DataDirectory)
{
    public static LaunchOptions Read(string[] args)
    {
        var safe = args.Contains("--safe-mode") || Environment.GetEnvironmentVariable("FCLASH_SAFE_MODE") == "true";
        var at = Array.IndexOf(args, "--data-dir");
        var path = at >= 0 && at + 1 < args.Length ? args[at + 1] : Environment.GetEnvironmentVariable("FCLASH_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(path) && !Path.IsPathFullyQualified(path)) throw new ArgumentException("数据目录必须是绝对路径");
        return new(safe, path ?? (safe ? Path.Combine(Path.GetTempPath(), "FClash-native-safe-" + Environment.ProcessId) : StateStore.DefaultDirectory));
    }
}
