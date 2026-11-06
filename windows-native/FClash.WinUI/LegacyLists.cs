using System.Collections.ObjectModel;
using FClash.Domain;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace FClash.WinUI;

public sealed partial class MainWindow
{
    private Func<Task>? pageFabAction;
    private int pageFabGlyph;
    private string pageFabTitle = "";
    private void Fab(int glyph, string title, Func<Task> action)
    {
        pageFabAction = action; pageFabGlyph = glyph; pageFabTitle = title;
        PowerButton.Visibility = Visibility.Visible; PowerButton.Width = 56; PowerButton.Content = Icon(glyph); ToolTipService.SetToolTip(PowerButton, T(title));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(PowerButton, T(title)); PowerButton.IsEnabled = true;
    }
    private void BuildLegacyProxies()
    {
        proxySort = Session.State.Config.Object("proxiesStyleProps")["sortType"].Text() switch { "delay" => 1, "name" => 2, _ => 0 };
        PageActions.Children.Add(IconAction(0xe567, "搜索", () => { ToggleProxySearch(); return Task.CompletedTask; }));
        PageActions.Children.Add(IconAction(0xe061, "定位选中项", () =>
        {
            var selected = Session.Proxies[group]?["now"].Text();
            var item = proxyCards?.Items.Cast<Button>().FirstOrDefault(x => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(x) == selected);
            if (item != null) proxyCards!.ScrollIntoView(item); return Task.CompletedTask;
        }));
        var menuButton = IconAction(0xe404, "更多", () => Task.CompletedTask);
        var menu = new MenuFlyout();
        var options = new MenuFlyoutItem { Text = T("设置") }; options.Click += (_, _) => _ = RunAsync(ProxyOptionsAsync); menu.Items.Add(options);
        var providers = new MenuFlyoutItem { Text = T("代理集合") }; providers.Click += (_, _) => OpenSubpage(ResourcesSettings); menu.Items.Add(providers);
        menuButton.Flyout = menu; PageActions.Children.Add(menuButton);
        group = Session.Groups.Contains(group) ? group : Session.Groups.FirstOrDefault() ?? "";
        var tabs = new Pivot { Height = 48, Margin = new Thickness(-12, -10, 0, 0) };
        foreach (var name in Session.Groups) tabs.Items.Add(new PivotItem { Header = name });
        tabs.SelectedIndex = Session.Groups.IndexOf(group);
        tabs.SelectionChanged += (_, _) => { group = (tabs.SelectedItem as PivotItem)?.Header?.ToString() ?? ""; RenderProxyCards(proxySort); };
        Body.Children.Add(tabs);
        proxyCards = new GridView { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = false, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(-4, 0, -4, 0) };
        Body.Children.Add(proxyCards); RenderProxyCards(proxySort);
        Fab(0xe5e0, "延迟测试", async () =>
        {
            if (testing) return; testing = true;
            try { await Session.TestAsync(GroupNodes(), lifetime.Token); UpdateProxyMetrics(); }
            finally { testing = false; }
        });
        PowerButton.IsEnabled = Session.Ready && !Session.Options.SafeMode;
        if (Session.Groups.Count == 0) Empty("暂无代理，请先添加并选中配置");
    }
    private void ToggleProxySearch()
    {
        var existing = Body.Children.OfType<AutoSuggestBox>().FirstOrDefault();
        if (existing != null) { Body.Children.Remove(existing); proxySearch = ""; RenderProxyCards(proxySort); return; }
        var search = new AutoSuggestBox { PlaceholderText = T("搜索代理"), Text = proxySearch, HorizontalAlignment = HorizontalAlignment.Stretch };
        search.TextChanged += (_, _) => { proxySearch = search.Text; RenderProxyCards(proxySort); };
        Body.Children.Insert(0, search); search.Focus(FocusState.Programmatic);
    }
    private async Task ProxyOptionsAsync()
    {
        var sort = new ComboBox { Header = T("排序"), ItemsSource = new[] { T("默认"), T("延迟"), T("名称") }, SelectedIndex = proxySort, HorizontalAlignment = HorizontalAlignment.Stretch };
        var compact = new CheckBox { Content = T("紧凑卡片"), IsChecked = Session.State.Config.Object("proxiesStyleProps")["cardType"].Text() == "shrink" };
        var layout = new ComboBox { Header = T("布局"), ItemsSource = new[] { "tight", "standard", "loose" }, SelectedItem = Session.State.Config.Object("proxiesStyleProps")["layout"].Text("standard"), HorizontalAlignment = HorizontalAlignment.Stretch };
        if (!await FormAsync("设置", Stack(sort, layout, compact))) return;
        proxySort = sort.SelectedIndex; Session.State.Config.Object("proxiesStyleProps")["sortType"] = proxySort == 1 ? "delay" : proxySort == 2 ? "name" : "none";
        Session.State.Config.Object("proxiesStyleProps")["cardType"] = compact.IsChecked == true ? "shrink" : "expand";
        Session.State.Config.Object("proxiesStyleProps")["layout"] = layout.SelectedItem?.ToString() ?? "standard";
        await Session.SaveAsync(); RenderProxyCards(proxySort);
    }
    private void BuildLegacyProfiles()
    {
        PageActions.Children.Add(IconAction(0xe62f, "更新全部", async () =>
        { foreach (var profile in Session.State.Profiles.Where(x => x.Url.Length > 0).ToArray()) await Session.UpdateProfileAsync(profile); ShowPage("profiles"); }));
        PageActions.Children.Add(IconAction(0xe5d2, "排序", ReorderProfilesAsync));
        Fab(0xe047, "添加配置", async () =>
        {
            var options = new ListView { ItemsSource = new[] { T("从 URL 导入"), T("从文件导入") }, SelectedIndex = 0, MinWidth = 280 };
            if (!await FormAsync("添加配置", options, "下一步")) return;
            if (options.SelectedIndex == 1) await ImportProfileAsync(); else await AddProfileAsync();
        });
        if (Session.State.Profiles.Count == 0) { Empty("暂无配置，请添加配置后使用"); return; }
        var columns = LegacyUiLayout.ProfileColumns(Root.ActualWidth - 80);
        var grid = new Grid { ColumnSpacing = 14, RowSpacing = 14 };
        for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new());
        for (var index = 0; index < Session.State.Profiles.Count; index++)
        {
            var profile = Session.State.Profiles[index];
            if (index % columns == 0) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var row = new Grid { ColumnSpacing = 12, MinHeight = 72 };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(28) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = new GridLength(40) });
            var radio = new RadioButton { IsChecked = Session.State.CurrentId == profile.Id, GroupName = "profiles", MinWidth = 24, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
            radio.Click += async (_, _) => await RunAsync(() => SelectProfileAsync(profile)); row.Children.Add(radio);
            var title = Text(profile.Label, 16); title.MaxLines = 1; title.TextTrimming = TextTrimming.CharacterEllipsis;
            var label = Stack(title, Text(profile.UpdatedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "", 12, true)); label.Spacing = 6; label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(label, 1); row.Children.Add(label);
            var more = IconAction(0xe404, "更多", () => Task.CompletedTask); Grid.SetColumn(more, 2); row.Children.Add(more);
            var menu = new MenuFlyout();
            void ActionItem(string text, Func<Task> action)
            { var item = new MenuFlyoutItem { Text = T(text) }; item.Click += async (_, _) => await RunAsync(action); menu.Items.Add(item); }
            ActionItem("编辑", () => EditProfileAsync(profile));
            if (profile.Url.Length > 0) ActionItem("更新", async () => { await Session.UpdateProfileAsync(profile); ShowPage("profiles"); });
            ActionItem("覆写", async () => { await SelectProfileAsync(profile); OpenSubpage(ScriptSettings); });
            if (profile.Url.Length > 0) ActionItem("复制链接", () => { Copy(profile.Url); return Task.CompletedTask; });
            ActionItem("导出文件", () => ExportProfileAsync(profile));
            menu.Items.Add(new MenuFlyoutSeparator()); ActionItem("删除", () => DeleteProfileAsync(profile)); more.Flyout = menu;
            var card = Card(row); card.Padding = new Thickness(12, 4, 4, 4);
            if (Session.State.CurrentId == profile.Id) { card.Background = Brush("SelectionBrush"); card.BorderBrush = Brush("PrimaryBrush"); }
            var button = new Button { Content = card, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = new SolidColorBrush(Colors.Transparent), HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch };
            button.Click += async (_, e) => { if (!more.IsPointerOver) await RunAsync(() => SelectProfileAsync(profile)); };
            Grid.SetColumn(button, index % columns); Grid.SetRow(button, index / columns); grid.Children.Add(button);
        }
        Body.Children.Add(grid);
    }
    private async Task SelectProfileAsync(Profile profile)
    {
        if (Session.State.CurrentId == profile.Id) return;
        var old = Session.State.CurrentId; Session.State.CurrentId = profile.Id;
        try { await Session.ApplyAsync(); BuildRail(); ShowPage("profiles"); }
        catch { Session.State.CurrentId = old; throw; }
    }
    private async Task DeleteProfileAsync(Profile profile)
    {
        if (!await ConfirmAsync("删除", "确定删除“" + profile.Label + "”？")) return;
        if (Session.State.CurrentId == profile.Id) { if (Session.Running) await Session.SetRunningAsync(false); Session.State.CurrentId = null; }
        Session.State.Profiles.Remove(profile); await Session.SaveAsync(); ShowPage("profiles");
    }
    private async Task ExportProfileAsync(Profile profile)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = profile.Label };
        picker.FileTypeChoices.Add("YAML", new[] { ".yaml" }); WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync(); if (file != null) File.Copy(Session.Store.ProfilePath(profile.Id), file.Path, true);
    }
    private async Task ReorderProfilesAsync()
    {
        var items = new ObservableCollection<Profile>(Session.State.Profiles);
        var list = new ListView { ItemsSource = items, CanDragItems = true, CanReorderItems = true, AllowDrop = true, MinWidth = 300, MaxHeight = 440 };
        if (!await FormAsync("配置排序", list)) return;
        Session.State.Profiles = items.ToList(); await Session.SaveAsync(); ShowPage("profiles");
    }
    private void BuildLegacyOrdered(string title, IEnumerable<string> source, Func<List<string>, Task> save, Func<string, Task<string?>> edit)
    {
        var values = new ObservableCollection<OrderedEntry>(source.Select(OrderedEntry.From));
        var initial = values.Select(x => x.Raw).ToList();
        var list = new ListView { ItemsSource = values, CanDragItems = true, CanReorderItems = true, AllowDrop = true, IsItemClickEnabled = true, SelectionMode = ListViewSelectionMode.Single, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        list.ItemTemplate = (DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><Grid MinHeight="64" Padding="16,8" ColumnSpacing="16"><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions><StackPanel Spacing="4" VerticalAlignment="Center"><TextBlock Text="{Binding Title}" FontSize="14" TextWrapping="Wrap"/><TextBlock Text="{Binding Detail}" FontSize="14" Opacity="0.6" TextWrapping="Wrap"/></StackPanel><FontIcon Grid.Column="1" Glyph="&#xE206;" FontFamily="ms-appx:///Assets/Fonts/materialicons-regular.otf#Material Icons" FontSize="18" Opacity="0.6"/></Grid></DataTemplate>
            """);
        async Task Commit()
        {
            try { await save(values.Select(x => x.Raw).ToList()); initial = values.Select(x => x.Raw).ToList(); }
            catch { values.Clear(); foreach (var raw in initial) values.Add(OrderedEntry.From(raw)); throw; }
        }
        async Task Add()
        { if (await edit("") is not { } raw) return; values.Add(OrderedEntry.From(raw)); await Commit(); }
        list.ItemClick += async (_, e) => await RunAsync(async () =>
        {
            var entry = (OrderedEntry)e.ClickedItem; var index = values.IndexOf(entry);
            if (await edit(entry.Raw) is not { } raw) return; values[index] = OrderedEntry.From(raw); await Commit();
        });
        list.DragItemsCompleted += async (_, _) => await RunAsync(Commit);
        var context = new MenuFlyout();
        foreach (var (label, operation) in new (string, Func<Task>)[]
        {
            ("删除", async () => { if (list.SelectedIndex < 0) return; values.RemoveAt(list.SelectedIndex); await Commit(); }),
            ("上移", async () => { var i = list.SelectedIndex; if (i > 0) { values.Move(i, i - 1); list.SelectedIndex = i - 1; await Commit(); } }),
            ("下移", async () => { var i = list.SelectedIndex; if (i >= 0 && i < values.Count - 1) { values.Move(i, i + 1); list.SelectedIndex = i + 1; await Commit(); } }),
        }) { var item = new MenuFlyoutItem { Text = T(label) }; item.Click += async (_, _) => await RunAsync(operation); context.Items.Add(item); }
        list.ContextFlyout = context;
        var panel = new StackPanel { Spacing = 0 };
        if (PageTitle.Text != T(title)) { var heading = Text(title, 14); heading.Margin = new Thickness(16, 24, 16, 8); panel.Children.Add(heading); }
        panel.Children.Add(list); Body.Children.Add(panel);
        PageActions.Children.Add(IconAction(0xe047, "添加", Add));
        var editButton = IconAction(0xe21a, "编辑", () => Task.CompletedTask);
        editButton.Click += (_, _) =>
        {
            var selecting = list.SelectionMode != ListViewSelectionMode.Multiple;
            list.SelectionMode = selecting ? ListViewSelectionMode.Multiple : ListViewSelectionMode.Single;
            list.IsItemClickEnabled = !selecting; list.SelectedItems.Clear();
            editButton.Content = Icon(selecting ? 0xe156 : 0xe21a);
        };
        PageActions.Children.Add(editButton);
        var deleteButton = IconAction(0xe1b9, "删除", async () =>
        {
            foreach (var item in list.SelectedItems.Cast<OrderedEntry>().ToArray()) values.Remove(item);
            await Commit();
        });
        deleteButton.Visibility = Visibility.Collapsed;
        list.SelectionChanged += (_, _) => deleteButton.Visibility = list.SelectionMode == ListViewSelectionMode.Multiple && list.SelectedItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PageActions.Children.Add(deleteButton);
    }
}

[Microsoft.UI.Xaml.Data.Bindable]
public sealed class OrderedEntry(string raw, string title, string detail)
{
    public string Raw { get; } = raw;
    public string Title { get; } = title;
    public string Detail { get; } = detail;
    public static OrderedEntry From(string raw)
    {
        var arrow = raw.IndexOf(" => ", StringComparison.Ordinal);
        if (arrow >= 0) return new(raw, raw[..arrow], raw[(arrow + 4)..]);
        var comma = raw.IndexOf(',');
        return comma >= 0 && raw[..comma].All(c => char.IsUpper(c) || c == '-') ? new(raw, raw[..comma], raw[(comma + 1)..]) : new(raw, raw, "");
    }
    public override string ToString() => Raw;
}
