using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using FClash.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FClash.WinUI;

public sealed partial class MainWindow
{
    private void SettingsPage() => BuildLegacyTools();
    private void OnDemandSettings()
    {
        SettingsBody("按需连接");
        Body.Children.Add(Card(Text("连接到排除列表中的 Wi-Fi 时暂停代理；离开后恢复。读取 SSID 需要 Windows 允许的位置权限。安全模式不会查询 Wi-Fi。", 14, true)));
        OrderedEditor("排除的 Wi-Fi 名称", (Session.State.Config["excludeSSIDs"] as JsonArray ?? []).Select(x => x.Text()),
            async values => { Session.State.Config["excludeSSIDs"] = Array(values); await Session.SaveAsync(); }, EditTextAsync);
    }
    private void WebDavSettings()
    {
        SettingsBody("WebDAV");
        var props = Session.State.Config.Object("davProps");
        var address = new TextBox { Header = "服务器地址", Text = props["uri"].Text(), PlaceholderText = "https://…", MinWidth = 400 };
        var user = new TextBox { Header = "用户名", Text = props["user"].Text() };
        var password = new PasswordBox { Header = "密码", Password = props["password"].Text() };
        var fileName = new TextBox { Header = "原生备份文件名", Text = Session.State.Config["nativeDavFileName"].Text("FClash-native.zip") };
        async Task Save()
        {
            props["uri"] = address.Text.Trim(); props["user"] = user.Text; props["password"] = password.Password;
            Session.State.Config["nativeDavFileName"] = fileName.Text.Trim(); await Session.SaveAsync();
        }
        Body.Children.Add(Card(Stack(address, user, password, fileName, Text("原生备份与 Flutter 备份格式不同，请使用单独的文件名。密码按旧版配置格式保存在本机数据目录，请妥善保管备份。", 14, true), Button("保存", Save))));
        Body.Children.Add(Row(Button("上传备份", async () =>
        {
            if (Session.Options.SafeMode) throw new InvalidOperationException("安全模式禁用 WebDAV 网络访问");
            if (!await ConfirmAsync("上传备份", "将配置、订阅文件和脚本上传到填写的 WebDAV 服务器，并覆盖同名文件。")) return;
            await Save();
            var temporary = Path.Combine(Path.GetDirectoryName(Session.Store.DirectoryPath)!, "FClash-upload-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                await Session.Store.BackupAsync(temporary, Session.State);
                using var http = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(2) };
                await new WebDavBackup(http).UploadAsync(address.Text, fileName.Text, user.Text, password.Password, temporary, lifetime.Token);
                Success("备份已上传");
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }, true), Button("下载并恢复", async () =>
        {
            if (Session.Options.SafeMode) throw new InvalidOperationException("安全模式禁用 WebDAV 网络访问");
            if (!await ConfirmAsync("恢复远程备份", "将下载备份并停止代理，恢复前会自动保存当前备份。")) return;
            await Save();
            var temporary = Path.Combine(Path.GetDirectoryName(Session.Store.DirectoryPath)!, "FClash-download-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                using var http = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(2) };
                await new WebDavBackup(http).DownloadAsync(address.Text, fileName.Text, user.Text, password.Password, temporary, lifetime.Token);
                await Session.RestoreAsync(temporary); SetTheme(); UpdateStatus(); ShowPage("settings"); Success("远程备份已恢复，代理处于停止状态");
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        })));
    }
    private void HotkeySettings() => BuildLegacyHotkeys();
    private void SettingsBody(string title) => BeginSubpage(title);
    private void AppSettings() => BuildLegacyApplication();
    private void NetworkSettings() => BuildLegacyNetwork();
    private void DnsSettings() => BuildLegacyDns();
    private static JsonArray Array(IEnumerable<string> items) => new(items.Select(x => JsonValue.Create(x)).ToArray());
    private void OrderedEditor(string title, IEnumerable<string> values, Func<List<string>, Task> save, Func<string, Task<string?>> edit) => BuildLegacyOrdered(title, values, save, edit);
    private async Task<string?> EditTextAsync(string current)
    {
        var box = new TextBox { Text = current, MinWidth = 430, PlaceholderText = "请输入内容" };
        if (!await FormAsync("编辑条目", box)) return null;
        return string.IsNullOrWhiteSpace(box.Text) ? throw new InvalidDataException("内容不能为空") : box.Text.Trim();
    }
    private async Task<string?> EditPolicyAsync(string current)
    {
        var parts = current.Split(" => ", 2);
        var domain = new TextBox { Header = "域名", Text = parts[0], PlaceholderText = "+.dev.sijingkeji.cn", MinWidth = 430 };
        var server = new TextBox { Header = "DNS 服务器", Text = parts.Length == 2 ? parts[1] : "", PlaceholderText = "system://" };
        if (!await FormAsync("域名策略", Stack(domain, server))) return null;
        if (string.IsNullOrWhiteSpace(domain.Text) || string.IsNullOrWhiteSpace(server.Text)) throw new InvalidDataException("域名和服务器不能为空");
        return domain.Text.Trim() + " => " + server.Text.Trim();
    }
    private void RulesSettings() => BuildLegacyRules();
    private async Task<string?> EditRuleAsync(string current)
    {
        var parts = current.Split(',');
        if (parts[0] is "AND" or "OR" or "NOT" or "SUB-RULE") return await EditTextAsync(current);
        var types = new[] { "DOMAIN", "DOMAIN-SUFFIX", "DOMAIN-KEYWORD", "GEOSITE", "GEOIP", "IP-CIDR", "IP-CIDR6", "RULE-SET", "PROCESS-NAME", "PROCESS-PATH", "MATCH" };
        var type = new ComboBox { Header = "规则类型", IsEditable = true, ItemsSource = types, SelectedItem = parts[0].Length == 0 ? "DOMAIN-SUFFIX" : parts[0], HorizontalAlignment = HorizontalAlignment.Stretch };
        var content = new TextBox { Header = "内容", Text = parts.Length > 1 ? parts[1] : "", MinWidth = 430 };
        var targetIndex = parts.Length - 1;
        while (targetIndex > 0 && parts[targetIndex] is "src" or "no-resolve") targetIndex--;
        var currentTarget = parts.Length > 1 ? parts[targetIndex] : "DIRECT";
        var target = new ComboBox { Header = "分流策略", IsEditable = true, ItemsSource = new[] { "DIRECT", "REJECT", "MATCH" }.Concat(Session.Groups).Distinct().ToArray(), Text = currentTarget, HorizontalAlignment = HorizontalAlignment.Stretch };
        var noResolve = new CheckBox { Content = "不解析主机名（no-resolve）", IsChecked = parts.Contains("no-resolve") };
        var src = new CheckBox { Content = "匹配来源 IP（src）", IsChecked = parts.Contains("src") };
        if (!await FormAsync("编辑规则", Stack(type, content, target, noResolve, src))) return null;
        var action = type.Text.Trim(); var policy = target.Text.Trim();
        if (action.Length == 0 || policy.Length == 0 || action != "MATCH" && content.Text.Trim().Length == 0) throw new InvalidDataException("请填写完整规则");
        var fields = new List<string> { action }; if (action != "MATCH") fields.Add(content.Text.Trim()); fields.Add(policy);
        if (src.IsChecked == true) fields.Add("src"); if (noResolve.IsChecked == true) fields.Add("no-resolve");
        return string.Join(',', fields);
    }
    private void ScriptSettings()
    {
        SettingsBody("脚本与配置");
        if (Session.State.Current is not { } profile) { Empty("请先选中配置"); return; }
        Body.Children.Add(Card(Stack(Text(profile.Label, 20), Button("编辑 YAML", () => EditProfileAsync(profile)))));
        var type = new ComboBox { Header = "覆写方式", ItemsSource = new[] { "standard", "script", "custom" }, SelectedItem = profile.OverwriteType };
        var script = new TextBox { AcceptsReturn = true, MinHeight = 260, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas"), Text = profile.ScriptId is { } id && File.Exists(Session.Store.ScriptPath(id)) ? File.ReadAllText(Session.Store.ScriptPath(id)) : "function main(config) {\n  return config;\n}" };
        Body.Children.Add(Card(Stack(type, Text("JavaScript：main(config) 返回配置对象", 14, true), script, Button("保存脚本与覆写方式", async () =>
        {
            var old = profile.OverwriteType; var oldId = profile.ScriptId;
            var nextId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await StateStore.AtomicWriteAsync(Session.Store.ScriptPath(nextId), script.Text);
            profile.ScriptId = nextId; profile.OverwriteType = type.SelectedItem?.ToString() ?? "standard";
            try { await Session.ApplyAsync(); } catch { profile.OverwriteType = old; profile.ScriptId = oldId; throw; }
            Success("已保存");
        }, true))));
        OrderedEditor("自定义规则", profile.CustomRules, async values =>
        {
            var previous = profile.CustomRules; profile.CustomRules = values;
            try { await Session.ApplyAsync(); } catch { profile.CustomRules = previous; throw; }
        }, EditRuleAsync);
        OrderedEditor("禁用的附加规则", profile.DisabledRules, async values =>
        {
            var previous = profile.DisabledRules; profile.DisabledRules = values;
            try { await Session.ApplyAsync(); } catch { profile.DisabledRules = previous; throw; }
        }, EditRuleAsync);
        Body.Children.Add(Button("编辑自定义代理分组", async () =>
        {
            var groups = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                MinWidth = 540,
                Height = 380,
                FontFamily = new FontFamily("Consolas"),
                Text = ConfigCompiler.Yaml(new JsonObject { ["proxy-groups"] = profile.Groups.Copy() })
            };
            if (!await FormAsync("自定义代理分组", Stack(Text("使用 Mihomo proxy-groups 语法；空数组表示使用订阅分组。", 14, true), groups))) return;
            var next = ConfigCompiler.Parse(groups.Text)["proxy-groups"] as JsonArray ?? throw new InvalidDataException("缺少 proxy-groups 数组");
            var previous = profile.Groups; profile.Groups = next;
            try { await Session.ApplyAsync(); } catch { profile.Groups = previous; throw; }
            Success("代理分组已更新");
        }));
    }
    private void BackupSettings() => BuildLegacyBackup();
    private void AppearanceSettings() => BuildLegacyAppearance();
    private void AboutSettings() => BuildLegacyAbout();
}
