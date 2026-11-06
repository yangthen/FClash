using System.Diagnostics;
using System.Text.Json.Nodes;
using FClash.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FClash.WinUI;

public sealed partial class MainWindow
{
    private void BuildLegacyBackup()
    {
        BeginSubpage("备份与恢复");
        Section("远程");
        Body.Children.Add(SettingRow(0xe161, "WebDAV", Session.State.Config.Object("davProps")["uri"].Text(), () => OpenSubpage(WebDavSettings)));
        Section("本地");
        Body.Children.Add(SettingRow(0xe4dc, "备份", null, () => _ = RunAsync(ExportBackupAsync)));
        Body.Children.Add(SettingRow(0xe514, "恢复", null, () => _ = RunAsync(RestoreBackupAsync)));
        Section("数据");
        Body.Children.Add(SettingRow(0xe2a3, "数据目录", Session.Store.DirectoryPath, () => Process.Start(new ProcessStartInfo(Session.Store.DirectoryPath) { UseShellExecute = true })));
        Body.Children.Add(SettingRow(0xe322, "旧版数据导入", "复制旧版数据到空的原生数据目录", () => _ = RunAsync(ImportLegacyFolderAsync)));
    }
    private async Task ExportBackupAsync()
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = "FClash-native-" + DateTime.Now.ToString("yyyyMMdd-HHmm") };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        picker.FileTypeChoices.Add("ZIP 备份", new List<string> { ".zip" });
        var file = await picker.PickSaveFileAsync(); if (file == null) return;
        var temporary = Path.Combine(Path.GetTempPath(), "FClash-backup-" + Guid.NewGuid().ToString("N") + ".zip");
        try { await Session.Store.BackupAsync(temporary, Session.State); File.Move(temporary, file.Path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        Success("备份已导出");
    }
    private async Task RestoreBackupAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker(); picker.FileTypeFilter.Add(".zip");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync(); if (file == null) return;
        if (!await ConfirmAsync("恢复备份", "将停止当前代理并恢复配置，恢复前会自动保存当前备份。")) return;
        await Session.RestoreAsync(file.Path); SetTheme(); BackupSettings(); UpdateStatus(); Success("备份已恢复，代理处于停止状态");
    }
    private async Task ImportLegacyFolderAsync()
    {
        if (Session.State.Profiles.Count > 0) throw new InvalidOperationException("已有原生配置，请使用独立数据目录导入，避免覆盖。");
        var picker = new Windows.Storage.Pickers.FolderPicker(); picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync(); if (folder == null) return;
        await Session.ImportLegacyAsync(folder.Path); SetTheme(); BackupSettings(); UpdateStatus(); Success("旧版配置已导入，原始数据保持不变");
    }
    private void BuildLegacyHotkeys()
    {
        BeginSubpage("快捷键管理");
        foreach (var (action, title) in new Dictionary<string, string> { ["start"] = "启动 / 停止", ["view"] = "显示 / 隐藏窗口", ["mode"] = "切换出站模式", ["proxy"] = "系统代理", ["tun"] = "TUN" })
        {
            var old = (Session.State.Config["hotKeyActions"] as JsonArray ?? []).OfType<JsonObject>().FirstOrDefault(x => x["action"].Text() == action);
            var usage = (old?["key"]?.GetValue<int>() ?? 0) & 0xffff;
            var modifiers = (old?["modifiers"] as JsonArray ?? []).Select(x => x.Text()).ToArray();
            var label = usage is >= 4 and <= 29 ? string.Join(" + ", modifiers.Select(x => x == "control" ? "Ctrl" : x == "alt" ? "Alt" : "Shift").Append(((char)('A' + usage - 4)).ToString())) : T("未设置");
            Body.Children.Add(SettingRow(0, title, label, () => _ = RunAsync(async () =>
            {
                var key = new ComboBox { Header = T("按键"), ItemsSource = new[] { T("关闭") }.Concat(Enumerable.Range('A', 26).Select(x => ((char)x).ToString())).ToArray(), SelectedIndex = usage is >= 4 and <= 29 ? usage - 3 : 0, MinWidth = 260 };
                var ctrl = new CheckBox { Content = "Ctrl", IsChecked = old == null || modifiers.Contains("control") };
                var alt = new CheckBox { Content = "Alt", IsChecked = old == null || modifiers.Contains("alt") };
                var shift = new CheckBox { Content = "Shift", IsChecked = modifiers.Contains("shift") };
                if (!await FormAsync(title, Stack(key, Row(ctrl, alt, shift)))) return;
                var previous = Session.State.Config["hotKeyActions"]?.DeepClone() as JsonArray ?? [];
                var next = previous.DeepClone().AsArray();
                foreach (var item in next.OfType<JsonObject>().Where(x => x["action"].Text() == action).ToArray()) next.Remove(item);
                if (key.SelectedIndex > 0)
                {
                    var mods = new List<string>(); if (ctrl.IsChecked == true) mods.Add("control"); if (alt.IsChecked == true) mods.Add("alt"); if (shift.IsChecked == true) mods.Add("shift");
                    if (mods.Count == 0) throw new InvalidDataException("请至少选择一个修饰键");
                    next.Add(new JsonObject { ["action"] = action, ["key"] = 0x70000 + key.SelectedIndex + 3, ["modifiers"] = Array(mods) });
                }
                try { hotkeys?.Set(next); Session.State.Config["hotKeyActions"] = next; await Session.SaveAsync(); }
                catch { hotkeys?.Set(previous); Session.State.Config["hotKeyActions"] = previous; throw; }
                RefreshSettings();
            })));
        }
    }
}
