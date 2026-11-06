using System.Text.Json.Nodes;
using FClash.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FClash.WinUI;

public sealed partial class MainWindow
{
    private void BuildLegacyTools()
    {
        Body.Children.Clear(); Body.Padding = new Thickness(0, 0, 0, 24); Body.Spacing = 0;
        PageTitle.Text = T("工具"); BackButton.Visibility = Visibility.Collapsed; PowerButton.Visibility = Visibility.Collapsed;
        void Link(int glyph, string title, string? description, Action render) => Body.Children.Add(SettingRow(glyph, title, description, () => OpenSubpage(render)));
        Section("更多");
        Link(0xe609, "资源", "外部代理与规则资源管理", ResourcesSettings);
        if (!Session.State.Settings.Flag("openLogs")) Link(0xe046, "日志", "日志捕获记录", () => { BeginSubpage("日志"); HistoryPage(logs, "", "日志"); if (Session.Ready) _ = RunAsync(async () => await Session.Core.CallAsync("startLog")); });
        Section("设置");
        Body.Children.Add(SettingRow(0xe366, "语言", Session.State.Settings["locale"].Text("默认"), () => _ = RunAsync(SelectLanguageAsync)));
        Link(0xe613, "主题", "主题与外观设置", AppearanceSettings);
        Link(0xf04ce, "备份与恢复", "本地备份与 WebDAV", BackupSettings);
        Link(0xe351, "快捷键管理", "管理全局快捷键", HotkeySettings);
        Link(0xe21a, "基本配置", "修改基础内核配置", BasicSettings);
        Link(0xe189, "进阶配置", "网络、DNS、规则与脚本", AdvancedSettings);
        Link(0xe57f, "应用", "启动、关闭及应用行为", AppSettings);
        Section("其他");
        Link(0xe33c, "关于", null, AboutSettings);
    }
    private async Task SelectLanguageAsync()
    {
        var labels = new[] { "默认", "简体中文", "English", "日本語", "Русский" };
        var values = new string?[] { null, "zh_CN", "en", "ja", "ru" };
        var options = new ListView { ItemsSource = labels, SelectedIndex = System.Array.IndexOf(values, Session.State.Settings["locale"]?.ToString()), MinWidth = 280 };
        if (!await FormAsync("语言", options) || options.SelectedIndex < 0) return;
        Session.State.Settings["locale"] = values[options.SelectedIndex];
        UiStrings.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "Translations"), values[options.SelectedIndex]);
        LocalizeNavigation(); await Session.SaveAsync(); BuildLegacyTools();
    }
    private void ResourcesSettings() { BeginSubpage("资源"); ResourcesPage(); }
    private void AdvancedSettings()
    {
        BeginSubpage("进阶配置");
        foreach (var (icon, title, description, render) in new (int, string, string, Action)[]
        {
            (0xe57f, "网络", "系统代理与 TUN 选项", NetworkSettings),
            (0xe6e7, "按需连接", "通过 Wi-Fi 名称控制代理运行", OnDemandSettings),
            (0xe1e5, "DNS", "配置域名解析服务器", DnsSettings),
            (0xe0a2, "附加规则", "控制全局附加规则", RulesSettings),
            (0xe21a, "脚本", "覆写配置脚本", ScriptSettings),
        }) { Body.Children.Add(SettingRow(icon, title, description, () => OpenSubpage(render))); RuleLine(); }
    }
    private void SwitchRow(string title, string? subtitle, bool value, Func<bool, Task> changed)
    { Body.Children.Add(SettingRow(0, title, subtitle, trailing: CompactSwitch(value, changed))); }
    private void InputRow(string title, string value, Func<string, Task> changed)
    {
        Body.Children.Add(SettingRow(0, title, value, () => _ = RunAsync(async () =>
        {
            var input = new TextBox { Text = value, MinWidth = 280 };
            if (!await FormAsync(title, input)) return; await changed(input.Text.Trim()); RefreshSettings();
        })));
    }
    private void ChoicesRow(string title, string value, string[] choices, Func<string, Task> changed, string[]? labels = null)
    {
        var index = System.Array.IndexOf(choices, value);
        Body.Children.Add(SettingRow(0, title, labels != null && index >= 0 ? labels[index] : value, () => _ = RunAsync(async () =>
        {
            var picker = new ListView { ItemsSource = labels ?? choices, SelectedIndex = index, MinWidth = 280 };
            if (!await FormAsync(title, picker) || picker.SelectedIndex < 0) return;
            await changed(choices[picker.SelectedIndex]); RefreshSettings();
        })));
    }
    private void RefreshSettings()
    {
        var offset = Scroller.VerticalOffset;
        currentRenderer?.Invoke();
        RestoreScroll(offset);
    }
    private void BuildLegacyAppearance()
    {
        BeginSubpage("主题");
        ChoicesRow("主题模式", Session.State.Config.Object("themeProps")["themeMode"].Text("dark"), ["system", "light", "dark"], async value =>
        { Session.State.Config.Object("themeProps")["themeMode"] = value; SetTheme(); await Session.SaveAsync(); }, ["默认", "浅色", "深色"]);
        Section("主题色");
        var colors = JsonNode.Parse(File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "material-palettes.json")))!.AsObject();
        var picker = new GridView { SelectionMode = ListViewSelectionMode.None, Margin = new Thickness(16), IsItemClickEnabled = false };
        foreach (var entry in colors)
        {
            var seed = Convert.ToUInt32(entry.Key, 16);
            var color = Microsoft.UI.ColorHelper.FromArgb(255, (byte)(seed >> 16), (byte)(seed >> 8), (byte)seed);
            var button = Button(entry.Key, async () => { Session.State.Config.Object("themeProps")["primaryColor"] = (long)seed; SetTheme(); await Session.SaveAsync(); });
            button.Content = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 40, Height = 40, Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(color) }; button.Padding = new Thickness(8); button.BorderThickness = new Thickness(0); picker.Items.Add(button);
        }
        Body.Children.Add(picker);
    }
    private void BuildLegacyRules()
    {
        BeginSubpage("附加规则");
        OrderedEditor("附加规则", Session.State.AddedRules, async values =>
        {
            var old = Session.State.AddedRules; Session.State.AddedRules = values;
            try { if (Session.State.Current != null) await Session.ApplyAsync(); else await Session.SaveAsync(); }
            catch { Session.State.AddedRules = old; throw; }
        }, EditRuleAsync);
    }
    private void BuildLegacyAbout()
    {
        BeginSubpage("关于");
        var brand = new Grid { Margin = new Thickness(24, 40, 24, 24), ColumnSpacing = 32 }; brand.ColumnDefinitions.Add(new() { Width = new GridLength(72) }); brand.ColumnDefinitions.Add(new());
        brand.Children.Add(new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/icon.png")), Width = 64, Height = 80 });
        var labels = Stack(Text("FClash", 28), Text(ClientVersion.Label, 14)); labels.VerticalAlignment = VerticalAlignment.Center; labels.Spacing = 4; Grid.SetColumn(labels, 1); brand.Children.Add(labels); Body.Children.Add(brand);
        var description = Text("基于 ClashMeta 的多平台代理客户端，简单易用，开源无广告。", 14); description.Margin = new Thickness(24, 8, 24, 32); Body.Children.Add(description);
        Body.Children.Add(SettingRow(0, "检查更新", null, () => _ = RunAsync(async () => await MessageAsync("检查更新", await Session.CheckUpdateAsync()))));
    }
    private void BuildLegacyApplication()
    {
        BeginSubpage("应用");
        SwitchRow("开机启动", null, Session.State.Settings.Flag("autoLaunch"), async value =>
        {
            if (Session.Options.SafeMode) throw new InvalidOperationException("安全模式禁止注册开机启动");
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (value) key.SetValue("FClash.WinUI", "\"" + Environment.ProcessPath + "\""); else key.DeleteValue("FClash.WinUI", false);
            Session.State.Settings["autoLaunch"] = value; await Session.SaveAsync();
        });
        SwitchRow("自动运行", "应用启动时自动运行代理", Session.State.Settings.Flag("autoRun"), async value => { Session.State.Settings["autoRun"] = value; await Session.SaveAsync(); });
        SwitchRow("退出时最小化", "关闭窗口后保留托盘；托盘退出会停止代理", Session.State.Settings.Flag("minimizeOnExit", true), async value => { Session.State.Settings["minimizeOnExit"] = value; await Session.SaveAsync(); });
        SwitchRow("日志", "在导航栏中显示日志", Session.State.Settings.Flag("openLogs"), async value => { Session.State.Settings["openLogs"] = value; await Session.SaveAsync(); BuildRail(); });
        Body.Children.Add(SettingRow(0, "以管理员身份重启", "使用 TUN 时需要管理员权限", () => _ = RunAsync(async () =>
        {
            if (Session.Options.SafeMode) throw new InvalidOperationException("安全模式禁用提权");
            var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
            start.ArgumentList.Add("--data-dir"); start.ArgumentList.Add(Session.Store.DirectoryPath); start.ArgumentList.Add("--wait-for-exit"); start.ArgumentList.Add(Environment.ProcessId.ToString());
            System.Diagnostics.Process.Start(start); await ExitAsync();
        })));
        Body.Children.Add(SettingRow(0, "订阅协议", "注册 fclash:// 订阅链接", () => _ = RunAsync(async () =>
        {
            if (Session.Options.SafeMode) throw new InvalidOperationException("安全模式禁止注册协议");
            var aliases = new CheckBox { Content = "同时接管 clash / clashmeta / flclash 订阅协议" };
            if (!await FormAsync("订阅协议", aliases)) return;
            foreach (var scheme in aliases.IsChecked == true ? new[] { "fclash", "flclash", "clash", "clashmeta" } : new[] { "fclash" })
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + scheme); key.SetValue("", "URL:FClash Protocol"); key.SetValue("URL Protocol", "");
                using var command = key.CreateSubKey(@"shell\open\command"); command.SetValue("", "\"" + Environment.ProcessPath + "\" \"%1\"");
            }
        })));
    }
    private void BasicSettings()
    {
        BeginSubpage("基本配置");
        ChoicesRow("日志等级", Session.State.Patch["log-level"].Text("error"), ["debug", "info", "warning", "error", "silent"], value => ChangeConfigAsync(() => Session.State.Patch["log-level"] = value));
        InputRow("User-Agent", Session.State.Patch["global-ua"].Text("FClash"), value => ChangeConfigAsync(() => Session.State.Patch["global-ua"] = value));
        SwitchRow("IPv6", null, Session.State.Patch.Flag("ipv6"), value => ChangeConfigAsync(() => Session.State.Patch["ipv6"] = value));
        SwitchRow("允许局域网访问", null, Session.State.Patch.Flag("allow-lan"), value => ChangeConfigAsync(() => Session.State.Patch["allow-lan"] = value));
        SwitchRow("统一延迟", null, Session.State.Patch.Flag("unified-delay", true), value => ChangeConfigAsync(() => Session.State.Patch["unified-delay"] = value));
        SwitchRow("TCP 并发", null, Session.State.Patch.Flag("tcp-concurrent", true), value => ChangeConfigAsync(() => Session.State.Patch["tcp-concurrent"] = value));
        foreach (var (key, title) in new[] { ("mixed-port", "混合端口"), ("port", "HTTP 端口"), ("socks-port", "SOCKS 端口") })
            InputRow(title, Session.State.Patch.Number(key, key == "mixed-port" ? 7890 : 0).ToString(), async value =>
            {
                if (!int.TryParse(value, out var port) || port < 0 || port > 65535 || key == "mixed-port" && port == 0) throw new InvalidDataException("端口无效");
                await ChangeConfigAsync(() => Session.State.Patch[key] = port);
                if (key == "mixed-port" && Session.Running && Session.Proxy.Enabled) Session.Proxy.Enable(port, Session.State.Network["bypassDomain"] as JsonArray);
            });
    }
    private void BuildLegacyNetwork()
    {
        BeginSubpage("网络");
        SwitchRow("系统代理", null, Session.State.Network.Flag("systemProxy", true), async value =>
        {
            Session.State.Network["systemProxy"] = value;
            if (Session.Running) { if (value) Session.Proxy.Enable(Session.State.Patch.Number("mixed-port", 7890), Session.State.Network["bypassDomain"] as JsonArray); else Session.Proxy.Restore(); }
            await Session.SaveAsync();
        });
        Body.Children.Add(SettingRow(0, "绕过域名", "配置不经过系统代理的地址", () => OpenOrdered("绕过域名", (Session.State.Network["bypassDomain"] as JsonArray ?? []).Select(x => x.Text()), async values =>
        { Session.State.Network["bypassDomain"] = Array(values); await Session.SaveAsync(); if (Session.Running && Session.Proxy.Enabled) Session.Proxy.Enable(Session.State.Patch.Number("mixed-port", 7890), Session.State.Network["bypassDomain"] as JsonArray); }, EditTextAsync)));
        RuleLine();
        SwitchRow("TUN", null, Session.State.Patch.Object("tun").Flag("enable"), async value =>
        { if (value && !IsAdministrator()) throw new InvalidOperationException("TUN 需要管理员权限"); await ChangeConfigAsync(() => Session.State.Patch.Object("tun")["enable"] = value); });
        ChoicesRow("网络栈", Session.State.Patch.Object("tun")["stack"].Text("mixed"), ["mixed", "system", "gvisor"], value => ChangeConfigAsync(() => Session.State.Patch.Object("tun")["stack"] = value));
        SwitchRow("自动路由", null, Session.State.Patch.Object("tun").Flag("auto-route"), value => ChangeConfigAsync(() => Session.State.Patch.Object("tun")["auto-route"] = value));
        SwitchRow("附加系统 DNS", null, Session.State.Network.Flag("appendSystemDns"), async value =>
        { Session.State.Network["appendSystemDns"] = value; if (Session.State.Current != null) await Session.ApplyAsync(); else await Session.SaveAsync(); });
    }
    private void OpenOrdered(string title, IEnumerable<string> values, Func<List<string>, Task> save, Func<string, Task<string?>> edit)
    {
        var snapshot = values.ToArray();
        OpenSubpage(() => { BeginSubpage(title); OrderedEditor(title, snapshot, save, edit); });
    }
    private void BuildLegacyDns()
    {
        BeginSubpage("DNS");
        var dns = ConfigCompiler.DefaultDns();
        if (Session.State.Patch["dns"] is JsonObject custom) foreach (var (key, value) in custom) dns[key] = value.Copy();
        Task Set(string key, JsonNode? value) => ChangeConfigAsync(() => Session.State.Patch.Object("dns")[key] = value.Copy());
        SwitchRow("覆盖 DNS", "覆盖订阅中的 DNS 设置", Session.State.Config.Flag("overrideDns"), value => ChangeConfigAsync(() => Session.State.Config["overrideDns"] = value));
        Section("选项");
        SwitchRow("状态", "启用 DNS 服务", dns.Flag("enable", true), value => Set("enable", JsonValue.Create(value)));
        InputRow("监听地址", dns["listen"].Text("127.0.0.1:1053"), value => Set("listen", JsonValue.Create(value)));
        foreach (var (key, title, fallback) in new[] { ("use-hosts", "使用 Hosts", true), ("use-system-hosts", "使用系统 Hosts", true), ("ipv6", "IPv6", false), ("respect-rules", "遵循规则", false), ("prefer-h3", "PreferH3", false) })
            SwitchRow(title, null, dns.Flag(key, fallback), value => Set(key, JsonValue.Create(value)));
        ChoicesRow("模式", dns["enhanced-mode"].Text("fake-ip"), ["fake-ip", "redir-host"], value => Set("enhanced-mode", JsonValue.Create(value)));
        InputRow("Fake-IP 范围", dns["fake-ip-range"].Text("198.18.0.1/16"), value => Set("fake-ip-range", JsonValue.Create(value)));
        void Servers(string key, string title)
        {
            var values = (dns[key] as JsonArray ?? []).Select(x => x.Text()).ToArray();
            Body.Children.Add(SettingRow(0, title, string.Join(", ", values), () => OpenOrdered(title, values, items => Set(key, Array(items)), EditTextAsync)));
        }
        Servers("fake-ip-filter", "Fake-IP 过滤"); Servers("default-nameserver", "默认域名服务器");
        var policies = dns["nameserver-policy"] switch
        {
            JsonObject map => map.Select(x => x.Key + " => " + (x.Value is JsonArray ? x.Value.ToJsonString() : x.Value.Text())).ToArray(),
            JsonArray array => array.Select(x => x?["domain"].Text() + " => " + x?["server"].Text()).ToArray(),
            _ => [],
        };
        Body.Children.Add(SettingRow(0, "域名策略", $"{policies.Length} 项", () => OpenOrdered("域名策略", policies, items =>
        {
            var map = new JsonObject(); foreach (var item in items)
            { var pair = item.Split(" => ", 2); if (pair.Length != 2 || map.ContainsKey(pair[0])) throw new InvalidDataException("域名策略无效或重复"); map[pair[0]] = pair[1].StartsWith('[') ? JsonNode.Parse(pair[1]) : JsonValue.Create(pair[1]); }
            return Set("nameserver-policy", map);
        }, EditPolicyAsync)));
        Servers("nameserver", "域名服务器"); Servers("fallback", "Fallback"); Servers("proxy-server-nameserver", "代理域名服务器"); Servers("direct-nameserver", "直连域名服务器");
        Section("Fallback 过滤");
        var filter = dns.Object("fallback-filter");
        SwitchRow("GEOIP", null, filter.Flag("geoip", true), value => ChangeConfigAsync(() => Session.State.Patch.Object("dns").Object("fallback-filter")["geoip"] = value));
        InputRow("GEOIP 代码", filter["geoip-code"].Text("CN"), value => ChangeConfigAsync(() => Session.State.Patch.Object("dns").Object("fallback-filter")["geoip-code"] = value));
        foreach (var key in new[] { "geosite", "ipcidr", "domain" })
        {
            var values = (filter[key] as JsonArray ?? []).Select(x => x.Text()).ToArray();
            Body.Children.Add(SettingRow(0, key.ToUpperInvariant(), string.Join(", ", values), () => OpenOrdered(key.ToUpperInvariant(), values, items => ChangeConfigAsync(() => Session.State.Patch.Object("dns").Object("fallback-filter")[key] = Array(items)), EditTextAsync)));
        }
    }
}
