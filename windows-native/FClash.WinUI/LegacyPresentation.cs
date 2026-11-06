using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json.Nodes;
using FClash.Domain;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace FClash.WinUI;

public sealed partial class MainWindow
{
    private readonly Stack<(Action Render, double Offset)> pageHistory = new();
    private Action? currentRenderer;
    private readonly Dictionary<string, Border> railIndicators = [];
    private readonly Dictionary<string, TextBlock> railLabels = [];
    private bool dashboardEditing;
    private TextBlock? uploadUsage, downloadUsage, detectedIp;
    private Image? detectedFlag;
    private LegacySpeedChart? speedChart;
    private LegacyDonut? usageChart;
    private DateTimeOffset? runStarted;
    private int proxySort;
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);
    private void RestoreLegacyWindowSize()
    {
        var settings = Session.State.Config.Object("windowProps");
        var scale = Math.Max(GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0, 1);
        var width = double.TryParse(settings["width"].Text(), out var w) && w > 0 ? w : 680;
        var height = double.TryParse(settings["height"].Text(), out var h) && h > 0 ? h : 580;
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(Math.Max(380, width) * scale), (int)(Math.Max(400, height) * scale)));
        Root.SizeChanged += (_, e) =>
        {
            if (!initialized) return;
            var currentScale = Math.Max(GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0, 1);
            settings["width"] = AppWindow.Size.Width / currentScale; settings["height"] = AppWindow.Size.Height / currentScale;
            if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) < 1) return;
            if (page == "proxies") RenderProxyCards(proxySort);
            else if (page == "profiles" && pageHistory.Count == 0) ShowPage("profiles");
        };
    }
    private void ApplyLegacyPalette()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "material-palettes.json");
        if (!File.Exists(path)) return;
        var palettes = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var seed = Session.State.Config.Object("themeProps")["primaryColor"]?.GetValue<long>() ?? 0xffd8c0c3;
        var palette = (palettes[((uint)seed).ToString("x8")] ?? palettes["ffd8c0c3"])!.AsObject();
        static Windows.UI.Color Color(string hex) => ColorHelper.FromArgb(Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16), Convert.ToByte(hex.Substring(7, 2), 16));
        foreach (var theme in new[] { "Light", "Dark" })
            foreach (var (key, value) in palette[theme.ToLowerInvariant()]!.AsObject())
                if (key.EndsWith("Brush", StringComparison.Ordinal) && ((ResourceDictionary)Root.Resources.ThemeDictionaries[theme])[key] is SolidColorBrush brush) brush.Color = Color(value.Text());
        var current = palette[Root.ActualTheme == ElementTheme.Dark ? "dark" : "light"]!;
        foreach (var name in new[] { "AccentButtonBackground", "AccentButtonBackgroundPointerOver", "AccentButtonBackgroundPressed", "RadioButtonOuterEllipseFillChecked", "RadioButtonOuterEllipseFillCheckedPointerOver", "PivotHeaderItemSelectedPipeFill", "PivotHeaderItemFocusPipeFill", "ListViewItemSelectionIndicatorBrush", "ListViewItemSelectionIndicatorPointerOverBrush", "TextControlBorderBrushFocused" })
            Application.Current.Resources[name] = new SolidColorBrush(Color(current["PrimaryBrush"].Text()));
        Application.Current.Resources["AccentButtonForeground"] = new SolidColorBrush(Color(current["onPrimary"].Text()));
        Application.Current.Resources["LegacyPrimaryBrush"] = new SolidColorBrush(Color(current["PrimaryBrush"].Text()));
        Application.Current.Resources["LegacyMutedBrush"] = new SolidColorBrush(Color(current["MutedBrush"].Text()));
        Application.Current.Resources["TextFillColorPrimaryBrush"] = new SolidColorBrush(Color(current["onSurface"].Text()));
    }
    private readonly Dictionary<string, string> navTitles = new()
    { ["dashboard"] = "仪表盘", ["proxies"] = "代理", ["profiles"] = "配置", ["requests"] = "请求", ["connections"] = "连接", ["logs"] = "日志", ["tools"] = "工具" };
    private static readonly FontFamily MaterialFont = new("ms-appx:///Assets/Fonts/materialicons-regular.otf#Material Icons");
    private FontIcon Icon(int glyph, double size = 24) => new() { FontFamily = MaterialFont, Glyph = char.ConvertFromUtf32(glyph), FontSize = size, Foreground = Brush("MutedBrush") };
    private Button IconAction(int glyph, string title, Func<Task> action)
    {
        var button = Button(title, action); button.Content = Icon(glyph);
        button.Style = (Style)Root.Resources["IconButtonStyle"];
        ToolTipService.SetToolTip(button, T(title));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, T(title));
        return button;
    }
    private void BuildRail()
    {
        var selected = page;
        Navigation.Items.Clear(); railIndicators.Clear(); railLabels.Clear();
        var glyphs = new Dictionary<string, int> { ["dashboard"] = 0xe5da, ["proxies"] = 0xe0a2, ["profiles"] = 0xe2a3, ["requests"] = 0xf059d, ["connections"] = 0xe0cb, ["logs"] = 0xe046, ["tools"] = 0xe189 };
        foreach (var key in LegacyUiLayout.DesktopNavigation)
        {
            if (key == "proxies" && Session.Groups.Count == 0) continue;
            if (key == "logs" && !Session.State.Settings.Flag("openLogs")) continue;
            var panel = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
            var indicator = new Border { Width = 64, Height = 32, CornerRadius = new CornerRadius(16), Child = Icon(glyphs[key]) };
            var label = Text(navTitles[key], 12); label.HorizontalAlignment = HorizontalAlignment.Center;
            label.Visibility = Session.State.Settings.Flag("showLabel") ? Visibility.Visible : Visibility.Collapsed;
            panel.Children.Add(indicator); panel.Children.Add(label);
            var item = new ListViewItem { Tag = key, Content = panel, MinHeight = 48 };
            ToolTipService.SetToolTip(item, T(navTitles[key]));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, T(navTitles[key]));
            railIndicators[key] = indicator; railLabels[key] = label; Navigation.Items.Add(item);
            if (key == selected) Navigation.SelectedItem = item;
        }
        UpdateRailSelection();
    }
    private void UpdateRailSelection()
    {
        foreach (var (key, indicator) in railIndicators)
            indicator.Background = key == page || page == "settings" && key == "tools" ? Brush("SelectionBrush") : new SolidColorBrush(Colors.Transparent);
    }
    private async void ToggleRailLabels(object sender, RoutedEventArgs e)
    {
        Session.State.Settings["showLabel"] = !Session.State.Settings.Flag("showLabel");
        foreach (var label in railLabels.Values) label.Visibility = Session.State.Settings.Flag("showLabel") ? Visibility.Visible : Visibility.Collapsed;
        await RunAsync(Session.SaveAsync);
    }
    private void OpenSubpage(Action render)
    {
        pageHistory.Push((currentRenderer ?? (() => ShowPage("tools")), Scroller.VerticalOffset));
        currentRenderer = render; render();
    }
    private void GoBackClick(object sender, RoutedEventArgs e) => GoBack();
    private void GoBack()
    {
        if (!pageHistory.TryPop(out var previous)) return;
        currentRenderer = previous.Render; previous.Render();
        RestoreScroll(previous.Offset);
    }
    private void RestoreScroll(double offset)
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            Scroller.UpdateLayout();
            Scroller.ChangeView(null, offset, null, true);
        });
    }
    private void BeginSubpage(string title)
    {
        Body.Children.Clear(); Body.Padding = new Thickness(0, 0, 0, 32); Body.Spacing = 0;
        PageTitle.Text = T(title); PageActions.Children.Clear(); BackButton.Visibility = Visibility.Visible;
        PowerButton.Visibility = Visibility.Collapsed;
        DispatcherQueue.TryEnqueue(() => Scroller.ChangeView(null, 0, null, true));
    }
    private void Section(string title)
    {
        var label = Text(title, 14); label.Foreground = Brush("PrimaryBrush");
        label.Margin = new Thickness(16, 24, 16, 8); Body.Children.Add(label);
    }
    private void RuleLine() => Body.Children.Add(new Border { Height = .5, Background = Brush("CardStroke"), Margin = new Thickness(16, 0, 16, 0) });
    private Button SettingRow(int glyph, string title, string? subtitle, Action? open = null, UIElement? trailing = null)
    {
        var row = new Grid { ColumnSpacing = 16, MinHeight = subtitle == null ? 56 : 72 };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(glyph == 0 ? 0 : 24) });
        row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        if (glyph != 0) row.Children.Add(Icon(glyph));
        var labels = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(Text(title, 16));
        if (subtitle != null) labels.Children.Add(Text(subtitle, 14, true));
        Grid.SetColumn(labels, 1); row.Children.Add(labels);
        if (trailing != null) { Grid.SetColumn((FrameworkElement)trailing, 2); row.Children.Add(trailing); }
        var button = new Button
        {
            Content = row,
            Padding = new Thickness(16, 0, 16, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0)
        };
        if (open != null) button.Click += (_, _) => open();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, T(title));
        return button;
    }
    private ToggleButton CompactSwitch(bool value, Func<bool, Task> changed)
    {
        var thumb = new Ellipse { Width = 24, Height = 24, Fill = Brush("CanvasBrush"), HorizontalAlignment = value ? HorizontalAlignment.Right : HorizontalAlignment.Left };
        var track = new Border { Width = 48, Height = 28, CornerRadius = new CornerRadius(14), Padding = new Thickness(2), Background = value ? Brush("PrimaryBrush") : Brush("CardStroke"), Child = thumb };
        var toggle = new ToggleButton { Content = track, IsChecked = value, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = new SolidColorBrush(Colors.Transparent), IsEnabled = !Session.Options.SafeMode };
        var busy = false;
        toggle.Click += async (_, _) =>
        {
            if (busy) return; busy = true; toggle.IsEnabled = false;
            try { await changed(toggle.IsChecked == true); value = toggle.IsChecked == true; }
            catch (Exception error) { toggle.IsChecked = value; Error(error); }
            finally
            {
                thumb.HorizontalAlignment = value ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                track.Background = value ? Brush("PrimaryBrush") : Brush("CardStroke"); busy = false; toggle.IsEnabled = !Session.Options.SafeMode;
            }
        };
        return toggle;
    }
    private Border DashboardCard(string key, int glyph, string title, UIElement content, Action? options = null)
    {
        var header = new Grid { Margin = new Thickness(16, 16, 16, 0), ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new() { Width = new GridLength(20) }); header.ColumnDefinitions.Add(new());
        header.Children.Add(Icon(glyph, 20));
        var label = Text(title, 14); Grid.SetColumn(label, 1); header.Children.Add(label);
        var body = new Grid(); body.RowDefinitions.Add(new() { Height = GridLength.Auto }); body.RowDefinitions.Add(new());
        body.Children.Add(header); Grid.SetRow((FrameworkElement)content, 1); body.Children.Add(content);
        var card = Card(body); card.Padding = new Thickness(0); card.Tag = key;
        if (options != null) card.Tapped += (_, e) =>
        {
            for (var node = e.OriginalSource as DependencyObject; node != null && node != card; node = VisualTreeHelper.GetParent(node)) if (node is ButtonBase) return;
            options();
        };
        return card;
    }
    private void BuildLegacyDashboard()
    {
        PageActions.Children.Add(IconAction(dashboardEditing ? 0xe550 : 0xe21a, dashboardEditing ? "保存" : "编辑", async () =>
        { dashboardEditing = !dashboardEditing; ShowPage("dashboard"); await Session.SaveAsync(); }));
        if (dashboardEditing) PageActions.Children.Insert(0, IconAction(0xe047, "添加", AddDashboardWidgetAsync));
        var panel = new LegacyDashboardPanel();
        var available = new Dictionary<string, Border>();
        speed = Text("↑ 0 B/s   ↓ 0 B/s", 12, true); speed.HorizontalAlignment = HorizontalAlignment.Right;
        speedChart = new LegacySpeedChart(Brush("PrimaryBrush"));
        var speedBody = new Grid(); speedBody.RowDefinitions.Add(new() { Height = new GridLength(0) }); speedBody.RowDefinitions.Add(new());
        Grid.SetRow(speedChart, 1); speedBody.Children.Add(speedChart);
        var speedCard = DashboardCard("networkSpeed", 0xe5e0, "网络速度", speedBody);
        if (speedCard.Child is Grid speedGrid && speedGrid.Children[0] is Grid header) { Grid.SetColumn(speed, 1); header.Children.Add(speed); }
        available["networkSpeed"] = speedCard;
        Grid Quick(string title, bool enabled, Func<bool, Task> change)
        {
            var content = new Grid { Margin = new Thickness(16, 0, 8, 8) }; content.ColumnDefinitions.Add(new()); content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            content.Children.Add(Text("选项", 12, true)); var toggle = CompactSwitch(enabled, change);
            Grid.SetColumn(toggle, 1); content.Children.Add(toggle); return content;
        }
        available["systemProxyButton"] = DashboardCard("systemProxyButton", 0xe5a1, "系统代理", Quick("系统代理", Session.State.Network.Flag("systemProxy", true), async value =>
        {
            Session.State.Network["systemProxy"] = value;
            if (Session.Running) { if (value) Session.Proxy.Enable(Session.State.Patch.Number("mixed-port", 7890), Session.State.Network["bypassDomain"] as JsonArray); else Session.Proxy.Restore(); }
            await Session.SaveAsync();
        }), () => _ = ShowQuickOptionsAsync(false));
        available["tunButton"] = DashboardCard("tunButton", 0xe5f7, "TUN", Quick("TUN", Session.State.Patch.Object("tun").Flag("enable"), async value =>
        {
            if (value && !IsAdministrator()) throw new InvalidOperationException("TUN 需要管理员权限");
            await ChangeConfigAsync(() => Session.State.Patch.Object("tun")["enable"] = value);
        }), () => _ = ShowQuickOptionsAsync(true));
        var radios = new StackPanel { Margin = new Thickness(12, 6, 12, 6), Spacing = 0 };
        foreach (var (mode, label) in new[] { ("rule", "规则"), ("global", "全局"), ("direct", "直连") })
        {
            var radio = new RadioButton { Content = T(label), GroupName = "outbound", IsChecked = Session.State.Patch["mode"].Text("rule") == mode, MinHeight = 35, Padding = new Thickness(4, 0, 0, 0) };
            radio.Click += async (_, _) => await RunAsync(() => ChangeConfigAsync(() => Session.State.Patch["mode"] = mode)); radios.Children.Add(radio);
        }
        available["outboundMode"] = DashboardCard("outboundMode", 0xe12d, "出站模式", radios);
        available["outboundModeV2"] = DashboardCard("outboundModeV2", 0xe12d, "出站模式", Text(T(Session.State.Patch["mode"].Text("rule")), 16));
        uploadUsage = Text("0 B", 12); downloadUsage = Text("0 B", 12); usageChart = new LegacyDonut(Brush("PrimaryBrush"), Brush("SelectionBrush"));
        var usage = new Grid { Margin = new Thickness(16, 8, 16, 16), ColumnSpacing = 12 }; usage.ColumnDefinitions.Add(new()); usage.ColumnDefinitions.Add(new());
        usage.Children.Add(usageChart); var numbers = Stack(Text("上传", 12, true), uploadUsage, Text("下载", 12, true), downloadUsage); numbers.Spacing = 4; numbers.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(numbers, 1); usage.Children.Add(numbers);
        available["trafficUsage"] = DashboardCard("trafficUsage", 0xe1b3, "流量统计", usage);
        detectedIp = Text("Timeout", 15); detectedIp.Margin = new Thickness(16, 0, 16, 12); detectedIp.Foreground = new SolidColorBrush(Colors.IndianRed);
        var detection = DashboardCard("networkDetection", 0xe424, "网络检测", detectedIp);
        detectedFlag = new Image { Width = 20, Height = 16, Visibility = Visibility.Collapsed };
        if (detection.Child is Grid detectionGrid && detectionGrid.Children[0] is Grid detectHeader)
        {
            detectHeader.Children.Add(detectedFlag);
            var info = IconAction(0xe33c, "提示", () => MessageAsync("网络检测", "显示当前出站连接的出口 IP 与国家。切换代理或启动代理后重新检测。"));
            info.Width = info.Height = 24; info.Padding = new Thickness(3); info.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(info, 1); detectHeader.Children.Add(info);
        }
        available["networkDetection"] = detection;
        var local = Text(LocalAddress(), 15); local.MaxLines = 1; local.TextTrimming = TextTrimming.CharacterEllipsis; local.Margin = new Thickness(16, 0, 16, 12);
        available["intranetIp"] = DashboardCard("intranetIp", 0xe1cb, "内网 IP", local);
        coreMemory = Text("—", 15); coreMemory.Margin = new Thickness(16, 0, 16, 12);
        available["memoryInfo"] = DashboardCard("memoryInfo", 0xe3db, "内存信息", coreMemory);
        var order = (Session.State.Settings["dashboardWidgets"] as JsonArray)?.Select(x => x.Text() == "coreMemory" ? "memoryInfo" : x.Text()).ToList() ?? LegacyUiLayout.DashboardDefaults.ToList();
        foreach (var key in order.Distinct())
        {
            if (!available.TryGetValue(key, out var card)) continue;
            if (dashboardEditing)
            {
                var wrapper = new Grid { Tag = key }; card.IsHitTestVisible = false; wrapper.Children.Add(card); wrapper.Children.Add(new Border { Background = new SolidColorBrush(Colors.Transparent) });
                var remove = IconAction(0xe16a, "移除", async () => { await SaveDashboardOrderAsync(order.Where(x => x != key)); ShowPage("dashboard"); });
                remove.Width = remove.Height = 24; remove.Padding = new Thickness(2); remove.HorizontalAlignment = HorizontalAlignment.Right; remove.VerticalAlignment = VerticalAlignment.Top; wrapper.Children.Add(remove); panel.Children.Add(wrapper);
            }
            else panel.Children.Add(card);
        }
        panel.EnableReorder = dashboardEditing;
        panel.Reordered += keys => _ = RunAsync(() => SaveDashboardOrderAsync(keys));
        Body.Children.Add(panel);
        if (!Session.Options.SafeMode && Session.Ready) _ = RunAsync(DetectNetworkCardAsync);
    }
    private Task SaveDashboardOrderAsync(IEnumerable<string> keys)
    { Session.State.Settings["dashboardWidgets"] = Array(keys); return Session.SaveAsync(); }
    private async Task AddDashboardWidgetAsync()
    {
        var order = (Session.State.Settings["dashboardWidgets"] as JsonArray)?.Select(x => x.Text()).ToList() ?? LegacyUiLayout.DashboardDefaults.ToList();
        var labels = new Dictionary<string, string> { ["networkSpeed"] = "网络速度", ["systemProxyButton"] = "系统代理", ["tunButton"] = "TUN", ["outboundMode"] = "出站模式", ["outboundModeV2"] = "出站模式（分段）", ["networkDetection"] = "网络检测", ["trafficUsage"] = "流量统计", ["intranetIp"] = "内网 IP", ["memoryInfo"] = "内存信息" };
        var choices = labels.Where(x => !order.Contains(x.Key)).ToArray();
        var picker = new ListView { ItemsSource = choices.Select(x => T(x.Value)).ToArray(), SelectionMode = ListViewSelectionMode.Single, MinWidth = 280 };
        if (await FormAsync("添加", picker, "添加") && picker.SelectedIndex >= 0) { order.Add(choices[picker.SelectedIndex].Key); await SaveDashboardOrderAsync(order); ShowPage("dashboard"); }
    }
    private async Task ShowQuickOptionsAsync(bool tun)
    {
        var value = tun ? Session.State.Patch.Object("tun").Flag("enable") : Session.State.Network.Flag("systemProxy", true);
        var toggle = Toggle(tun ? "TUN" : "系统代理", value, async enabled =>
        {
            if (tun) { if (enabled && !IsAdministrator()) throw new InvalidOperationException("TUN 需要管理员权限"); await ChangeConfigAsync(() => Session.State.Patch.Object("tun")["enable"] = enabled); }
            else { Session.State.Network["systemProxy"] = enabled; if (Session.Running) { if (enabled) Session.Proxy.Enable(Session.State.Patch.Number("mixed-port", 7890), Session.State.Network["bypassDomain"] as JsonArray); else Session.Proxy.Restore(); } await Session.SaveAsync(); }
        });
        var body = Stack(toggle);
        if (tun)
        {
            var stack = new ComboBox { Header = T("网络栈"), ItemsSource = new[] { "mixed", "system", "gvisor" }, SelectedItem = Session.State.Patch.Object("tun")["stack"].Text("mixed") };
            stack.SelectionChanged += async (_, _) => await RunAsync(() => ChangeConfigAsync(() => Session.State.Patch.Object("tun")["stack"] = stack.SelectedItem?.ToString())); body.Children.Add(stack);
        }
        var bypass = false;
        var dialog = new ContentDialog { Title = T(tun ? "TUN" : "系统代理"), Content = body, CloseButtonText = T("关闭"), XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme };
        if (!tun) body.Children.Add(Button("绕过域名", () => { bypass = true; dialog.Hide(); return Task.CompletedTask; }));
        await dialogGate.WaitAsync(lifetime.Token);
        try { await dialog.ShowAsync(); }
        finally { dialogGate.Release(); }
        if (bypass) OpenOrdered("绕过域名", (Session.State.Network["bypassDomain"] as JsonArray ?? []).Select(x => x.Text()), async items =>
        { Session.State.Network["bypassDomain"] = Array(items); await Session.SaveAsync(); if (Session.Running && Session.Proxy.Enabled) Session.Proxy.Enable(Session.State.Patch.Number("mixed-port", 7890), Session.State.Network["bypassDomain"] as JsonArray); }, EditTextAsync);
        else ShowPage("dashboard");
    }
    private static string LocalAddress()
    {
        return NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
            .Where(x => x.GetIPProperties().GatewayAddresses.Any())
            .SelectMany(x => x.GetIPProperties().UnicastAddresses).Select(x => x.Address)
            .FirstOrDefault(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(x) && !x.ToString().StartsWith("198.18.") && !x.ToString().StartsWith("169.254."))?.ToString() ?? T("无网络");
    }
    private async Task DetectNetworkCardAsync()
    {
        if (Session.Options.SafeMode || detectedIp == null) return;
        var target = detectedIp; target.Text = "…";
        using var handler = new System.Net.Http.HttpClientHandler { Proxy = new WebProxy("http://127.0.0.1:" + Session.State.Patch.Number("mixed-port", 7890)), UseProxy = Session.Running && !Session.Suspended };
        using var client = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            var info = JsonNode.Parse(await client.GetStringAsync("https://ipwho.is", lifetime.Token))!;
            if (target != detectedIp) return; target.Text = info["ip"].Text(); target.Foreground = Brush("MutedBrush");
            var country = info["country_code"].Text().ToLowerInvariant();
            if (detectedFlag != null && country.Length == 2 && File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Flags", country + ".svg")))
            { detectedFlag.Source = new SvgImageSource(new Uri("ms-appx:///Assets/Flags/" + country + ".svg")); detectedFlag.Visibility = Visibility.Visible; }
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or OperationCanceledException)
        { if (target == detectedIp) { target.Text = "Timeout"; target.Foreground = new SolidColorBrush(Colors.IndianRed); } }
    }
    private void UpdateDashboardMetrics(JsonNode rate, JsonNode usage, JsonNode? memory)
    {
        if (speed != null) speed.Text = "↑ " + Bytes(rate["up"]) + "/s   ↓ " + Bytes(rate["down"]) + "/s";
        if (uploadUsage != null) uploadUsage.Text = Bytes(usage["up"]);
        if (downloadUsage != null) downloadUsage.Text = Bytes(usage["down"]);
        if (coreMemory != null) coreMemory.Text = Bytes(JsonValue.Create(System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 + (long.TryParse(memory.Text(), out var bytes) ? bytes : 0)));
        speedChart?.Add((rate["up"]?.GetValue<long>() ?? 0) + (rate["down"]?.GetValue<long>() ?? 0));
        usageChart?.Update(usage["up"]?.GetValue<long>() ?? 0, usage["down"]?.GetValue<long>() ?? 0);
    }
}

internal sealed class LegacySpeedChart : Canvas
{
    private readonly Queue<double> values = new([0, 0]);
    private readonly Polygon area;
    private readonly Polyline line;
    public LegacySpeedChart(SolidColorBrush color)
    {
        Height = 120; VerticalAlignment = VerticalAlignment.Bottom;
        area = new Polygon { Fill = new SolidColorBrush(color.Color) { Opacity = .15 } };
        line = new Polyline { Stroke = color, StrokeThickness = 2 };
        Children.Add(area); Children.Add(line); SizeChanged += (_, _) => Draw();
    }
    public void Add(double value) { values.Enqueue(value); while (values.Count > 60) values.Dequeue(); Draw(); }
    private void Draw()
    {
        var points = values.ToArray(); var max = Math.Max(points.Max(), 1); var width = ActualWidth;
        line.Points.Clear(); area.Points.Clear(); area.Points.Add(new Point(0, Height));
        for (var i = 0; i < points.Length; i++) { var p = new Point(width * i / Math.Max(points.Length - 1, 1), Height - points[i] / max * (Height - 8)); line.Points.Add(p); area.Points.Add(p); }
        area.Points.Add(new Point(width, Height));
    }
}
internal sealed class LegacyDonut : Grid
{
    private readonly Microsoft.UI.Xaml.Shapes.Path segment = new();
    public LegacyDonut(SolidColorBrush primary, SolidColorBrush secondary)
    {
        Width = Height = 80; VerticalAlignment = VerticalAlignment.Center;
        Children.Add(new Ellipse { Stroke = secondary, StrokeThickness = 12, Width = 68, Height = 68 });
        segment.Stroke = primary; segment.StrokeThickness = 12; Children.Add(segment); Update(0, 0);
    }
    public void Update(double up, double down)
    {
        var ratio = up + down <= 0 ? 0 : up / (up + down); var angle = Math.Clamp(ratio, 0, .99999) * Math.PI * 2;
        var geometry = new PathGeometry(); var figure = new PathFigure { StartPoint = new Point(40, 6), IsClosed = false };
        figure.Segments.Add(new ArcSegment { Point = new Point(40 + 34 * Math.Sin(angle), 40 - 34 * Math.Cos(angle)), Size = new Size(34, 34), IsLargeArc = ratio > .5, SweepDirection = SweepDirection.Clockwise });
        geometry.Figures.Add(figure); segment.Data = geometry; segment.Visibility = ratio <= 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
