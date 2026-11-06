using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Security.Principal;
using System.Text.Json.Nodes;
using FClash.Domain;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using WinRT.Interop;

namespace FClash.WinUI;

public sealed partial class MainWindow : Window
{
    public ClientSession Session { get; }
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer timer;
    private readonly ObservableCollection<string> logs = [];
    private readonly ObservableCollection<string> requests = [];
    private readonly CancellationTokenSource lifetime = new();
    private Task? autoUpdates;
    private Task? onDemand;
    private NativeTray? tray;
    private Hotkeys? hotkeys;
    private bool initialized, quitting, polling, testing;
    private bool processingActivations;
    private readonly SemaphoreSlim dialogGate = new(1);
    private string page = "dashboard", group = "";
    private TextBlock? speed, coreMemory;
    private ListView? connectionList;
    private ObservableCollection<ConnectionRow> connections = [];
    private GridView? proxyCards;
    private string proxySearch = "";
    private readonly Dictionary<string, Border> cards = [];
    private readonly Dictionary<string, TextBlock> cardDelay = [];
    private readonly Dictionary<string, TextBlock> cardCountry = [];
    private readonly Dictionary<string, Image> cardFlag = [];

    public MainWindow(ClientSession session)
    {
        Session = session;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(850, 725));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
        AppWindow.Closing += (sender, e) =>
        {
            if (quitting) return;
            e.Cancel = true;
            if (!Session.Options.SafeMode && initialized && Session.State.Settings.Flag("minimizeOnExit", true) && tray != null) AppWindow.Hide();
            else _ = ExitAsync();
        };
        timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += async (_, _) => await PollAsync();
        Session.Message += message => DispatcherQueue.TryEnqueue(() =>
        {
            var type = message["type"].Text();
            if (type == "log" && (page == "logs" || PageTitle.Text == T("日志"))) Append(logs, message["data"]?.ToJsonString() ?? "");
            if (type == "request" && page == "requests") Append(requests, DescribeRequest(message["data"]));
            if (type == "delay") UpdateProxyMetrics();
        });
        Session.Failed += error => DispatcherQueue.TryEnqueue(() => { Error(error); UpdateStatus(); });
        Root.Loaded += async (_, _) =>
        {
            await RunAsync(async () =>
            {
                await Session.InitializeAsync();
                UiStrings.Load(Path.Combine(AppContext.BaseDirectory, "Translations"), Session.State.Settings["locale"]?.ToString());
                LocalizeNavigation();
                SetTheme();
                RestoreLegacyWindowSize();
                if (!Session.Options.SafeMode)
                {
                    try
                    {
                        tray = new NativeTray(WindowNative.GetWindowHandle(this),
                            () => { AppWindow.Show(); Activate(); }, () => _ = ExitAsync(), () => _ = RunAsync(async () => { await Session.SetRunningAsync(!Session.Running); UpdateStatus(); }));
                    }
                    catch (Exception e) { Error(e); }
                    try
                    {
                        hotkeys = new Hotkeys(WindowNative.GetWindowHandle(this), action => _ = RunAsync(() => HandleHotkeyAsync(action)));
                        hotkeys.Set(Session.State.Config["hotKeyActions"] as JsonArray);
                    }
                    catch (Exception e) { Error(e); }
                }
                initialized = true;
                Navigation.SelectedIndex = 0;
                ShowPage("dashboard");
                UpdateStatus(); timer.Start();
                autoUpdates = AutoUpdateLoopAsync();
                onDemand = OnDemandLoopAsync();
                await HandleActivationsAsync();
            });
        };
    }
    public async Task HandleActivationsAsync()
    {
        if (!initialized || Session.Options.SafeMode || processingActivations) return;
        var queue = Path.Combine(Session.Store.DirectoryPath, "incoming");
        if (!Directory.Exists(queue)) return;
        processingActivations = true;
        try
        {
            foreach (var file in Directory.EnumerateFiles(queue, "*.json").Order(StringComparer.Ordinal).ToArray())
            {
                var value = await File.ReadAllTextAsync(file); File.Delete(file);
                await RunAsync(async () =>
                {
                    if (value.Length > 16384) throw new InvalidDataException("订阅链接过长");
                    var uri = new Uri(System.Text.Json.JsonSerializer.Deserialize<string>(value)!);
                    if (uri.Scheme is not ("fclash" or "flclash" or "clash" or "clashmeta") || uri.Host != "install-config") throw new InvalidDataException("不支持此订阅协议操作");
                    var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split('=', 2))
                        .Where(x => x.Length == 2).ToDictionary(x => Uri.UnescapeDataString(x[0]), x => Uri.UnescapeDataString(x[1].Replace('+', ' ')));
                    if (!query.TryGetValue("url", out var url)) throw new InvalidDataException("订阅链接缺少 url 参数");
                    await AddProfileFromLinkAsync(url, query.GetValueOrDefault("name", ""));
                });
            }
        }
        catch (Exception e) { Error(e); }
        finally { processingActivations = false; }
    }

    private void SetTheme()
    {
        Root.RequestedTheme = Session.State.Config.Object("themeProps")["themeMode"].Text() switch
        { "dark" => ElementTheme.Dark, "light" => ElementTheme.Light, _ => ElementTheme.Default };
        ApplyLegacyPalette();
    }
    private async Task RunAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) { Error(e); }
    }
    private void Error(Exception error)
    {
        Notice.Title = T("操作失败"); Notice.Message = T(error.Message); Notice.Severity = InfoBarSeverity.Error; Notice.IsOpen = true;
    }
    private void Success(string text)
    {
        Notice.Title = T("已完成"); Notice.Message = T(text); Notice.Severity = InfoBarSeverity.Success; Notice.IsOpen = true;
    }
    private void UpdateStatus()
    {
        RunStatus.Text = Session.Options.SafeMode ? T("安全模式") : Session.Ready ? "✓" : T("未连接");
        ToolTipService.SetToolTip(RunStatus, Session.Options.SafeMode ? T("安全模式 · 网络功能已隔离") : Session.Ready ? T("内核已连接") : T("内核未连接"));
        if (pageFabAction != null)
        {
            PowerButton.Content = Icon(pageFabGlyph); PowerButton.Width = 56;
            ToolTipService.SetToolTip(PowerButton, T(pageFabTitle));
            PowerButton.IsEnabled = page != "proxies" || Session.Ready && !Session.Options.SafeMode;
            return;
        }
        if (!Session.Running) runStarted = null; else runStarted ??= DateTimeOffset.UtcNow;
        var content = Row(Icon(Session.Running ? 0xe47c : 0xe4cb));
        if (Session.Running) content.Children.Add(Text(Session.Suspended ? T("已暂停") : (DateTimeOffset.UtcNow - runStarted!.Value).ToString(@"hh\:mm\:ss"), 14));
        PowerButton.Content = content; PowerButton.Width = Session.Running ? 152 : 56;
        ToolTipService.SetToolTip(PowerButton, Session.Running ? T("停止") : T("启动"));
        PowerButton.IsEnabled = initialized && !Session.Options.SafeMode && Session.Ready;
    }
    private async void TogglePower(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (pageFabAction != null) { await pageFabAction(); return; }
        PowerButton.IsEnabled = false;
        try { await Session.SetRunningAsync(!Session.Running); ShowPage(page); }
        finally { UpdateStatus(); }
    });
    private async Task ExitAsync()
    {
        if (quitting) return; quitting = true; timer.Stop(); lifetime.Cancel();
        try { if (autoUpdates != null) await autoUpdates; if (onDemand != null) await onDemand; await Session.DisposeAsync(); }
        catch (Exception e) { File.AppendAllText(Path.Combine(Session.Store.DirectoryPath, "error.log"), e.ToString()); }
        finally { hotkeys?.Dispose(); tray?.Dispose(); Close(); Application.Current.Exit(); }
    }
    private async Task OnDemandLoopAsync()
    {
        if (Session.Options.SafeMode) return;
        using var clock = new PeriodicTimer(TimeSpan.FromSeconds(10));
        string? lastError = null;
        try
        {
            do
            {
                try { await Session.UpdateOnDemandAsync(); UpdateStatus(); lastError = null; }
                catch (Exception e) { if (lastError != e.Message) Error(e); lastError = e.Message; }
            } while (await clock.WaitForNextTickAsync(lifetime.Token));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }
    private async Task HandleHotkeyAsync(string action)
    {
        switch (action)
        {
            case "view": if (AppWindow.IsVisible) AppWindow.Hide(); else { AppWindow.Show(); Activate(); } break;
            case "start": await Session.SetRunningAsync(!Session.Running); break;
            case "mode":
                var mode = Session.State.Patch["mode"].Text("rule");
                await ChangeConfigAsync(() => Session.State.Patch["mode"] = mode == "rule" ? "global" : mode == "global" ? "direct" : "rule"); break;
            case "proxy":
                var enabled = !Session.State.Network.Flag("systemProxy", true); Session.State.Network["systemProxy"] = enabled;
                if (Session.Running) { if (enabled) Session.Proxy.Enable(Session.State.Patch.Number("mixed-port", 7890), Session.State.Network["bypassDomain"] as JsonArray); else Session.Proxy.Restore(); }
                await Session.SaveAsync(); break;
            case "tun":
                var next = !Session.State.Patch.Object("tun").Flag("enable");
                if (next && !IsAdministrator()) throw new InvalidOperationException("TUN 需要管理员权限");
                await ChangeConfigAsync(() => Session.State.Patch.Object("tun")["enable"] = next); break;
        }
        UpdateStatus(); if (page == "dashboard") ShowPage(page);
    }
    private async Task AutoUpdateLoopAsync()
    {
        using var clock = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await clock.WaitForNextTickAsync(lifetime.Token)) await Session.UpdateDueProfilesAsync(lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }
    private async void Navigate(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized || Navigation.SelectedItem is not ListViewItem item) return;
        if (page == item.Tag.ToString() && pageHistory.Count == 0) return;
        ShowPage(item.Tag.ToString()!);
        await RunAsync(async () =>
        {
            if (!Session.Ready) return;
            await Session.Core.CallAsync(page == "logs" ? "startLog" : "stopLog");
            await Session.Core.CallAsync("requestEvents", page == "requests");
        });
    }
    private async void OpenSettings(object sender, RoutedEventArgs e)
    {
        Navigation.SelectedItem = null; ShowPage("settings");
        await RunAsync(async () => { if (Session.Ready) { await Session.Core.CallAsync("stopLog"); await Session.Core.CallAsync("requestEvents", false); } });
    }
    private void ShowPage(string target)
    {
        pageFabAction = null;
        pageHistory.Clear(); currentRenderer = () => ShowPage(target);
        BackButton.Visibility = Visibility.Collapsed; PageActions.Children.Clear();
        Body.Padding = new Thickness(16, 16, 16, 88); Body.Spacing = 14;
        PowerButton.Visibility = target is "dashboard" or "proxies" or "profiles" ? Visibility.Visible : Visibility.Collapsed;
        page = target; Body.Children.Clear(); cards.Clear(); cardDelay.Clear(); cardCountry.Clear(); cardFlag.Clear();
        Scroller.ChangeView(null, 0, null);
        speed = coreMemory = uploadUsage = downloadUsage = detectedIp = null; speedChart = null; usageChart = null; detectedFlag = null; proxyCards = null; connectionList = null;
        PageTitle.Text = target switch
        {
            "dashboard" => "仪表盘",
            "proxies" => "代理",
            "profiles" => "配置",
            "connections" => "连接",
            "requests" => "请求",
            "resources" => "资源",
            "logs" => "日志",
            "tools" => "工具",
            _ => "设置",
        };
        PageTitle.Text = T(PageTitle.Text);
        UpdateRailSelection();
        switch (target)
        {
            case "dashboard": Dashboard(); break;
            case "proxies": ProxiesPage(); break;
            case "profiles": ProfilesPage(); break;
            case "connections": ConnectionsPage(); break;
            case "requests": HistoryPage(requests, "仅显示当前页面打开期间的请求", "请求记录"); break;
            case "logs": HistoryPage(logs, "仅在此页订阅日志，最多保留 500 条", "日志"); break;
            case "resources": ResourcesPage(); break;
            case "tools": ToolsPage(); break;
            default: SettingsPage(); break;
        }
    }

    private SolidColorBrush Brush(string resource) => (SolidColorBrush)Root.Resources[resource];
    private static string T(string text) => UiStrings.Current.T(text);
    private void LocalizeNavigation()
    {
        BuildRail();
    }
    private static TextBlock Text(string text, double size = 14, bool muted = false) => new()
    {
        Text = T(text),
        FontSize = size,
        TextWrapping = TextWrapping.Wrap,
        FontFamily = new FontFamily("ms-appx:///Assets/Fonts/roboto-regular.ttf#Roboto, Microsoft YaHei UI"),
        Opacity = muted ? .65 : 1,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private static StackPanel Stack(params UIElement[] children)
    {
        var panel = new StackPanel { Spacing = 12 };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }
    private Border Card(UIElement child) => new()
    {
        Background = Brush("CardBrush"),
        BorderBrush = Brush("CardStroke"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(16),
        Child = child,
    };
    private Button Button(string title, Func<Task> action, bool accent = false)
    {
        var button = new Button { Content = T(title), HorizontalAlignment = HorizontalAlignment.Left };
        if (accent) button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await RunAsync(action); } finally { button.IsEnabled = true; }
        };
        return button;
    }
    private static StackPanel Row(params UIElement[] elements)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var element in elements) row.Children.Add(element);
        return row;
    }
    private void Empty(string text) => Body.Children.Add(Card(Text(text, 16, true)));

    private void Dashboard() => BuildLegacyDashboard();
    private static bool IsAdministrator() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    private ToggleSwitch Toggle(string title, bool value, Func<bool, Task> change)
    {
        var toggle = new ToggleSwitch { Header = T(title), IsOn = value, OnContent = T("开启"), OffContent = T("关闭"), IsEnabled = !Session.Options.SafeMode };
        var changing = false;
        toggle.Toggled += async (_, _) =>
        {
            if (changing) return;
            changing = true;
            var next = toggle.IsOn;
            toggle.IsEnabled = false;
            try { await change(next); value = next; }
            catch (Exception e) { toggle.IsOn = value; Error(e); }
            finally { changing = false; toggle.IsEnabled = !Session.Options.SafeMode; }
        };
        return toggle;
    }
    private async Task ChangeConfigAsync(Action change)
    {
        var previous = Session.State.Config.DeepClone().AsObject();
        try { change(); if (Session.State.Current != null) await Session.ApplyAsync(); else await Session.SaveAsync(); }
        catch { Session.State.Config = previous; throw; }
    }
    private async Task DetectNetworkAsync()
    {
        if (Session.Options.SafeMode) throw new InvalidOperationException("安全模式禁用网络检测");
        using var handler = new System.Net.Http.HttpClientHandler { Proxy = new WebProxy("http://127.0.0.1:" + Session.State.Patch.Number("mixed-port", 7890)), UseProxy = Session.Running };
        using var client = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var result = JsonNode.Parse(await client.GetStringAsync("https://ipwho.is"))!;
        await MessageAsync("网络检测", result["ip"].Text() + "\n" + result["country"].Text() + " · " + result["city"].Text());
    }

    private async Task PollAsync()
    {
        if (polling || !Session.Ready || !AppWindow.IsVisible || pageHistory.Count > 0) return;
        polling = true;
        try
        {
            if (page == "dashboard")
            {
                var rate = (await Session.Core.CallAsync("getTraffic", false)).Decode();
                var usage = (await Session.Core.CallAsync("getTotalTraffic", false)).Decode();
                var memory = await Session.Core.CallAsync("getMemory");
                UpdateDashboardMetrics(rate, usage, memory);
            }
            else if (page == "connections") await RefreshConnectionsAsync();
            UpdateStatus();
        }
        catch (Exception e) { Error(e); }
        finally { polling = false; }
    }
    private static string Bytes(JsonNode? node)
    {
        _ = double.TryParse(node.Text("0"), out var number);
        var units = new[] { "B", "KB", "MB", "GB" }; var i = 0;
        while (number >= 1024 && i < 3) { number /= 1024; i++; }
        return $"{number:0.#} {units[i]}";
    }

    private void ProxiesPage() => BuildLegacyProxies();
    private IEnumerable<string> GroupNodes() => (Session.Proxies[group]?["all"] as JsonArray ?? []).Select(x => x.Text());
    private void RenderProxyCards(int sort = 0)
    {
        if (proxyCards == null) return;
        proxyCards.Items.Clear(); cards.Clear(); cardDelay.Clear(); cardCountry.Clear(); cardFlag.Clear();
        var names = GroupNodes().Where(x => x.Contains(proxySearch, StringComparison.OrdinalIgnoreCase));
        if (sort == 1) names = names.OrderBy(x => Session.Delays.TryGetValue(x, out var delay) && delay > 0 ? delay : int.MaxValue);
        if (sort == 2) names = names.Order(StringComparer.CurrentCultureIgnoreCase);
        foreach (var name in names)
        {
            var title = Text(name, 14); title.MaxLines = 2; title.Height = 40; title.TextTrimming = TextTrimming.CharacterEllipsis;
            var delay = Text("—", 13);
            var country = Text("", 12);
            var flag = new Image { Width = 22, Height = 16, Visibility = Visibility.Collapsed, Stretch = Stretch.Uniform };
            var location = Row(country, flag);
            var metrics = new Grid(); metrics.ColumnDefinitions.Add(new()); metrics.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            Grid.SetColumn(location, 1); metrics.Children.Add(delay); metrics.Children.Add(location);
            var compact = Session.State.Config.Object("proxiesStyleProps")["cardType"].Text() == "shrink";
            var kind = Text(Session.Proxies[name]?["type"].Text() ?? "", 12, true);
            if (compact)
            {
                metrics.ColumnDefinitions.Insert(0, new ColumnDefinition());
                Grid.SetColumn(delay, 1); Grid.SetColumn(location, 2); metrics.Children.Add(kind);
            }
            var content = compact ? Stack(title, metrics) : Stack(title, kind, metrics); content.Spacing = 6;
            var width = Math.Max(Root.ActualWidth - 80 - 32, 200);
            var columns = LegacyUiLayout.ProxyColumns(Root.ActualWidth - 80, Session.State.Config.Object("proxiesStyleProps")["layout"].Text("standard"));
            var card = Card(content); card.Width = (width - 14 * (columns - 1)) / columns;
            card.Padding = new Thickness(12); card.MinHeight = compact ? 88 : 106; card.Margin = new Thickness(0, 0, 6, 10);
            var selected = Session.Proxies[group]?["now"].Text() == name;
            if (selected) { card.BorderBrush = Brush("PrimaryBrush"); card.Background = Brush("SelectionBrush"); }
            var button = new Button { Content = card, Padding = new Thickness(0), Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0) };
            var node = name;
            button.Click += async (_, _) => await RunAsync(async () =>
            {
                var groupType = Session.Proxies[group]?["type"].Text();
                var computed = groupType is "URLTest" or "Fallback" or "url-test" or "fallback";
                if (!computed && groupType is not ("Selector" or "select")) throw new InvalidOperationException("此分组不支持手动选择代理");
                var next = computed && Session.State.Current?.Selected.GetValueOrDefault(group) == node ? "" : node;
                await Session.SelectAsync(group, next); RenderProxyCards(sort);
            });
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
            proxyCards.Items.Add(button); cards[name] = card; cardDelay[name] = delay; cardCountry[name] = country; cardFlag[name] = flag;
        }
        UpdateProxyMetrics();
    }
    private void UpdateProxyMetrics()
    {
        foreach (var (name, text) in cardDelay)
        {
            if (Session.Delays.TryGetValue(name, out var ms))
            {
                text.Text = ms < 0 ? "超时" : ms + " ms";
                text.Foreground = new SolidColorBrush(ms < 0 ? Colors.IndianRed : ms < 500 ? Colors.MediumSeaGreen : Colors.DarkOrange);
            }
            if (Session.State.CurrentId is { } id && Session.State.Countries.TryGetValue(Session.State.CountryKey(id, name), out var country))
            {
                var flag = cardFlag[name];
                var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Flags", country.ToLowerInvariant() + ".svg");
                if (File.Exists(path))
                {
                    flag.Source = new SvgImageSource(new Uri("ms-appx:///Assets/Flags/" + country.ToLowerInvariant() + ".svg"));
                    flag.Visibility = Visibility.Visible; cardCountry[name].Text = "";
                    ToolTipService.SetToolTip(flag, country);
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(flag, country);
                }
                else cardCountry[name].Text = country;
            }
        }
    }

    private void ProfilesPage() => BuildLegacyProfiles();
    private Task AddProfileAsync() => AddProfileFromLinkAsync("", "");
    private async Task AddProfileFromLinkAsync(string source, string label)
    {
        var name = new TextBox { Header = "名称", Text = label }; var url = new TextBox { Header = "订阅地址", PlaceholderText = "https://…", MinWidth = 430, Text = source };
        var auto = new CheckBox { Content = "每天自动更新", IsChecked = true };
        if (!await FormAsync("添加订阅", Stack(name, url, auto))) return;
        var profile = new Profile { Label = string.IsNullOrWhiteSpace(name.Text) ? "订阅" : name.Text, Url = url.Text.Trim(), AutoUpdate = auto.IsChecked == true };
        await Session.UpdateProfileAsync(profile); Session.State.Profiles.Add(profile); await Session.SaveAsync(); ShowPage("profiles");
    }
    private async Task ImportProfileAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker(); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add(".yaml"); picker.FileTypeFilter.Add(".yml");
        var file = await picker.PickSingleFileAsync(); if (file == null) return;
        var yaml = await File.ReadAllTextAsync(file.Path); _ = ConfigCompiler.Parse(yaml);
        var profile = new Profile { Label = Path.GetFileNameWithoutExtension(file.Name), UpdatedAt = DateTimeOffset.Now };
        await StateStore.AtomicWriteAsync(Session.Store.ProfilePath(profile.Id), yaml); Session.State.Profiles.Add(profile); await Session.SaveAsync(); ShowPage("profiles");
    }
    private async Task EditProfileAsync(Profile profile)
    {
        var name = new TextBox { Header = "名称", Text = profile.Label };
        var url = new TextBox { Header = "订阅地址", Text = profile.Url };
        var auto = new CheckBox { Content = "自动更新", IsChecked = profile.AutoUpdate };
        var interval = new NumberBox { Header = "更新间隔（小时）", Value = profile.UpdateInterval / 3600000.0, Minimum = 1, Maximum = 8760 };
        var yaml = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinWidth = 560, Height = 350, FontFamily = new FontFamily("Consolas"), Text = await File.ReadAllTextAsync(Session.Store.ProfilePath(profile.Id)) };
        if (!await FormAsync("编辑配置", Stack(name, url, auto, interval, yaml))) return;
        _ = ConfigCompiler.Parse(yaml.Text);
        var path = Session.Store.ProfilePath(profile.Id); var previous = await File.ReadAllTextAsync(path);
        await StateStore.AtomicWriteAsync(path, yaml.Text);
        try { if (Session.State.CurrentId == profile.Id) await Session.ApplyAsync(); }
        catch { await StateStore.AtomicWriteAsync(path, previous); throw; }
        profile.Label = name.Text; profile.Url = url.Text.Trim(); profile.AutoUpdate = auto.IsChecked == true;
        profile.UpdateInterval = double.IsNaN(interval.Value) ? 86400000 : (long)(interval.Value * 3600000);
        await Session.SaveAsync(); ShowPage("profiles");
    }

    private void ConnectionsPage()
    {
        PageActions.Children.Add(IconAction(0xe514, "刷新", RefreshConnectionsAsync));
        PageActions.Children.Add(IconAction(0xe1b9, "关闭全部连接", async () => { await Session.Core.CallAsync("closeConnections"); await RefreshConnectionsAsync(); }));
        var search = new AutoSuggestBox { PlaceholderText = T("搜索域名、进程、规则"), HorizontalAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed };
        PageActions.Children.Insert(0, IconAction(0xe567, "搜索", () => { search.Visibility = search.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed; if (search.Visibility == Visibility.Visible) search.Focus(FocusState.Programmatic); return Task.CompletedTask; }));
        search.TextChanged += (_, _) => { if (connectionList != null) connectionList.ItemsSource = connections.Where(x => x.ToString().Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToArray(); };
        Body.Children.Add(search);
        connectionList = new ListView { ItemsSource = connections, SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true, MaxHeight = 600 };
        connectionList.ItemClick += async (_, args) => await RunAsync(async () =>
        {
            if (args.ClickedItem is not ConnectionRow row) return;
            var result = await FormAsync("连接详情", Text(row.Detail, 14), "关闭此连接");
            if (result) { await Session.Core.CallAsync("closeConnection", row.Id); await RefreshConnectionsAsync(); }
        });
        Body.Children.Add(connectionList); _ = RunAsync(RefreshConnectionsAsync);
    }
    private async Task RefreshConnectionsAsync()
    {
        if (!Session.Ready) return;
        var data = (await Session.Core.CallAsync("getConnections")).Decode();
        connections = new((data["connections"] as JsonArray ?? []).OfType<JsonObject>().Select(x =>
            new ConnectionRow(x["id"].Text(), DescribeRequest(x), x.ToJsonString(new() { WriteIndented = true }))));
        if (connectionList != null) connectionList.ItemsSource = connections;
    }
    private sealed record ConnectionRow(string Id, string Summary, string Detail) { public override string ToString() => Summary; }
    private static string DescribeRequest(JsonNode? node)
    {
        var meta = node?["metadata"];
        return $"{meta?["host"].Text(meta?["destinationIP"].Text() ?? "")}:{meta?["destinationPort"].Text()}   {meta?["process"].Text()}\n{node?["rule"].Text()} · {node?["chains"]?.ToJsonString()}   ↑ {Bytes(node?["upload"])} ↓ {Bytes(node?["download"])}";
    }
    private static void Append(ObservableCollection<string> list, string item)
    { list.Insert(0, item); while (list.Count > 500) list.RemoveAt(list.Count - 1); }
    private void HistoryPage(ObservableCollection<string> source, string hint, string title)
    {
        var list = new ListView { ItemsSource = source, SelectionMode = ListViewSelectionMode.Single, MaxHeight = 600, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var search = new AutoSuggestBox { PlaceholderText = T("搜索"), Visibility = Visibility.Collapsed };
        search.TextChanged += (_, _) => list.ItemsSource = search.Text.Length == 0 ? source : source.Where(x => x.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
        PageActions.Children.Add(IconAction(0xe567, "搜索", () => { search.Visibility = search.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed; if (search.Visibility == Visibility.Visible) search.Focus(FocusState.Programmatic); return Task.CompletedTask; }));
        PageActions.Children.Add(IconAction(0xe1b9, "清空", () => { source.Clear(); return Task.CompletedTask; }));
        PageActions.Children.Add(IconAction(0xe190, "复制", () => { Copy(string.Join('\n', source)); return Task.CompletedTask; }));
        Body.Children.Add(search); Body.Children.Add(list);
    }
    private static void Copy(string value) { var data = new DataPackage(); data.SetText(value); Clipboard.SetContent(data); }

    private void ResourcesPage()
    {
        PageActions.Children.Add(IconAction(0xe514, "刷新", () => { ResourcesSettings(); return Task.CompletedTask; }));
        _ = RunAsync(async () =>
        {
            if (!Session.Ready) { Empty("Core 未连接"); return; }
            var data = (await Session.Core.CallAsync("getExternalProviders")).Decode();
            IEnumerable<JsonNode?> providers = data is JsonArray array ? array : data is JsonObject map ? map.Select(x => x.Value) : [];
            foreach (var node in providers.OfType<JsonObject>())
            {
                var name = node["name"].Text();
                Body.Children.Add(Card(Stack(Text(name, 20), Text(node["type"].Text() + " · " + node["count"].Text() + " 条", 13, true),
                    Text(node["update-at"].Text(), 12, true), Button("更新", async () =>
                    { var error = (await Session.Core.CallAsync("updateExternalProvider", name)).Text(); if (error.Length > 0) throw new IOException(error); Success(name + " 已更新"); }))));
            }
        });
    }

    private void ToolsPage() => SettingsPage();

    private async Task<bool> FormAsync(string title, UIElement content, string primary = "保存")
    {
        var dialog = new ContentDialog { Title = T(title), Content = content, PrimaryButtonText = T(primary), CloseButtonText = T("取消"), DefaultButton = ContentDialogButton.Primary, XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme };
        await dialogGate.WaitAsync(lifetime.Token);
        try { return await dialog.ShowAsync() == ContentDialogResult.Primary; }
        finally { dialogGate.Release(); }
    }
    private Task<bool> ConfirmAsync(string title, string text) => FormAsync(title, Text(text), "确定");
    private async Task MessageAsync(string title, string message)
    {
        var text = new TextBox { Text = message, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinWidth = 460, MaxHeight = 500 };
        var dialog = new ContentDialog { Title = T(title), Content = text, CloseButtonText = T("关闭"), XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme };
        await dialogGate.WaitAsync(lifetime.Token);
        try { await dialog.ShowAsync(); }
        finally { dialogGate.Release(); }
    }
}
