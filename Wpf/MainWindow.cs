using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace KillWind.Wpf
{
    public sealed class MainWindow : Window
    {
        private readonly NativeBridgeClient bridge;
        private readonly MemoryScanner scanner;
        private readonly PointerResolver pointerResolver;
        private readonly AobScanner aobScanner;
        private readonly ProfileStore profileStore;
        private readonly ObservableAddressList addresses = new ObservableAddressList();
        private readonly DispatcherTimer freezeTimer;
        private readonly DispatcherTimer processWatchTimer;
        private readonly DispatcherTimer watchTimer;
        private readonly GlobalHotkeyService hotkeys;
        private ComboBox processCombo;
        private ComboBox typeCombo;
        private ComboBox initialCombo;
        private ComboBox conditionCombo;
        private ComboBox moduleCombo;
        private TextBox scanValue;
        private TextBox moduleOffsetBox;
        private TextBox pointerOffsetsBox;
        private TextBlock pointerResultText;
        private TextBox logBox;
        private DataGrid resultsGrid;
        private DataGrid regionsGrid;
        private DataGrid addressesGrid;
        private ProgressBar progress;
        private TextBlock statusText;
        private TextBlock scanCountText;
        private TextBlock processText;
        private TextBox profileName;
        private ComboBox profileCombo;
        private ProcessInfo[] processes = new ProcessInfo[0];
        private ProcessInfo attached;
        private MemoryRegion[] regions = new MemoryRegion[0];
        private ModuleInfo[] modules = new ModuleInfo[0];
        private List<ScanResult> scanResults = new List<ScanResult>();
        private readonly List<List<ScanResult>> scanHistory = new List<List<ScanResult>>();
        private CancellationTokenSource scanCancellation;
        private bool addressRefreshRunning;

        private static readonly Brush WindowBrush = BrushFrom("#111418");
        private static readonly Brush PanelBrush = BrushFrom("#20262D");
        private static readonly Brush HeaderBrush = BrushFrom("#2A323A");
        private static readonly Brush InputBrush = BrushFrom("#14191E");
        private static readonly Brush LineBrush = BrushFrom("#3A434D");
        private static readonly Brush TextBrush = BrushFrom("#E4E8EC");
        private static readonly Brush MutedBrush = BrushFrom("#9DA7B1");
        private static readonly Brush AccentBrush = BrushFrom("#65C9C5");

        public MainWindow(string helperPath)
        {
            bridge = new NativeBridgeClient(helperPath);
            scanner = new MemoryScanner(bridge);
            pointerResolver = new PointerResolver(bridge);
            aobScanner = new AobScanner(bridge);
            profileStore = new ProfileStore();
            freezeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            freezeTimer.Tick += FreezeTimerOnTick;
            processWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            processWatchTimer.Tick += ProcessWatchTimerOnTick;
            watchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            watchTimer.Tick += WatchTimerOnTick;
            hotkeys = new GlobalHotkeyService(this, HotkeyTriggered);
            Title = "KillWind";
            Width = 1380; Height = 900; MinWidth = 1060; MinHeight = 700;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize; Background = WindowBrush;
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
            BuildUi();
            Loaded += async (sender, args) => { hotkeys.RegisterDefaults(); Activate(); await RefreshProcessesAsync(); RefreshProfileList(); processWatchTimer.Start(); };
            Closed += (sender, args) => { freezeTimer.Stop(); processWatchTimer.Stop(); watchTimer.Stop(); hotkeys.Dispose(); bridge.Dispose(); };
        }

        private void BuildUi()
        {
            var root = new Grid();
            root.Resources.Add(typeof(ScrollBar), DarkScrollBarStyle());
            root.Resources.Add(typeof(Thumb), DarkThumbStyle());
            root.Resources.Add(typeof(RepeatButton), DarkRepeatButtonStyle());
            root.Resources[SystemColors.ScrollBarBrushKey] = BrushFrom("#171D23");
            root.Resources[SystemColors.ScrollBarColorKey] = Color.FromRgb(23, 29, 35);
            root.Resources[SystemColors.ControlBrushKey] = BrushFrom("#242C34");
            root.Resources[SystemColors.ControlDarkBrushKey] = BrushFrom("#12171C");
            root.Resources[SystemColors.ControlDarkDarkBrushKey] = BrushFrom("#0D1115");
            root.Resources[SystemColors.ControlLightBrushKey] = BrushFrom("#35404A");
            root.Resources[SystemColors.ControlLightLightBrushKey] = BrushFrom("#3F4B56");
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(102) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            Content = root;

            root.Children.Add(BuildTitleBar());
            root.Children.Add(BuildMenuBar());
            root.Children.Add(BuildToolbar());
            root.Children.Add(BuildWorkspace());
            root.Children.Add(BuildLogPanel());
            root.Children.Add(BuildStatusBar());
        }

        private UIElement BuildTitleBar()
        {
            var title = new Grid { Background = BrushFrom("#1B2025") };
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var mark = new Border { Width = 25, Height = 25, Background = BrushFrom("#183137"), BorderBrush = BrushFrom("#527D7F"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3) };
            mark.Child = new TextBlock { Text = "K", Foreground = AccentBrush, FontSize = 16, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            brand.Children.Add(mark);
            brand.Children.Add(new TextBlock { Text = "KillWind  ·  通用离线游戏修改器", Foreground = TextBrush, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0) });
            title.Children.Add(brand);
            var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            controls.Children.Add(WindowButton("−", () => WindowState = WindowState.Minimized));
            controls.Children.Add(WindowButton("□", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized));
            controls.Children.Add(WindowButton("×", Close, true));
            Grid.SetColumn(controls, 1); title.Children.Add(controls);
            title.MouseLeftButtonDown += (sender, args) => { if (args.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; else DragMove(); };
            Grid.SetRow(title, 0); return title;
        }

        private Button WindowButton(string text, Action action, bool close = false)
        {
            var button = new Button { Content = text, Width = 42, Height = 30, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = close ? BrushFrom("#E17B82") : MutedBrush, FontSize = 16, Padding = new Thickness(0) };
            button.Click += (sender, args) => action();
            button.MouseEnter += (sender, args) => button.Background = close ? BrushFrom("#A33E48") : BrushFrom("#303941");
            button.MouseLeave += (sender, args) => button.Background = Brushes.Transparent;
            return button;
        }

        private UIElement BuildMenuBar()
        {
            var menu = new Menu { Background = BrushFrom("#171B20"), Foreground = TextBrush, Padding = new Thickness(6, 0, 0, 0) };
            var file = Menu("文件"); file.Items.Add(MenuCommand("新建扫描", NewScan)); file.Items.Add(AsyncMenuCommand("刷新进程", RefreshProcessesAsync)); file.Items.Add(new Separator()); file.Items.Add(MenuCommand("退出", Close));
            var edit = Menu("编辑"); edit.Items.Add(MenuCommand("添加选中地址", AddSelectedAddresses)); edit.Items.Add(AsyncMenuCommand("刷新地址数值", RefreshAddressesMenuAsync)); edit.Items.Add(MenuCommand("清空当前扫描", NewScan));
            var view = Menu("视图"); view.Items.Add(AsyncMenuCommand("刷新内存区域", RefreshRegionsAsync)); view.Items.Add(AsyncMenuCommand("刷新地址列表", RefreshAddressesMenuAsync)); view.Items.Add(MenuCommand("Memory Viewer / Dissect", OpenMemoryViewer));
            var tools = Menu("工具"); tools.Items.Add(MenuCommand("启动测试程序", LaunchTestGame)); tools.Items.Add(AsyncMenuCommand("屏幕取值模式", ScreenEditAsync)); tools.Items.Add(MenuCommand("AOB / Signature Scan", OpenAobScanner)); tools.Items.Add(MenuCommand("Pointer Scan", OpenPointerScanner)); tools.Items.Add(MenuCommand("存档编辑器", OpenSaveEditor)); tools.Items.Add(MenuCommand("Trainer Mode", OpenTrainer));
            var help = Menu("帮助"); help.Items.Add(MenuCommand("关于 KillWind", () => Message("KillWind WPF 原生桌面版\n用于本地离线游戏进程研究。\n当前版本：0.2.0")));
            menu.Items.Add(file); menu.Items.Add(edit); menu.Items.Add(view); menu.Items.Add(tools); menu.Items.Add(help);
            Grid.SetRow(menu, 1); return menu;
        }

        private static MenuItem Menu(string header) { return new MenuItem { Header = header, Padding = new Thickness(10, 2, 10, 2) }; }
        private static MenuItem MenuCommand(string header, Action action) { var item = new MenuItem { Header = header }; item.Click += (sender, args) => action(); return item; }
        private static MenuItem AsyncMenuCommand(string header, Func<Task> action) { var item = new MenuItem { Header = header }; item.Click += async (sender, args) => await action(); return item; }

        private UIElement BuildToolbar()
        {
            var toolbar = new DockPanel { Background = BrushFrom("#20252A"), LastChildFill = false, Margin = new Thickness(0, 1, 0, 0) };
            toolbar.Children.Add(ToolButton("刷新", async () => await RefreshProcessesAsync()));
            toolbar.Children.Add(ToolButton("测试程序", LaunchTestGame));
            toolbar.Children.Add(new Separator { Width = 12, Opacity = .3 });
            toolbar.Children.Add(new TextBlock { Text = "目标进程", Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) });
            processCombo = DarkCombo(new string[0], 0); processCombo.Width = 390; processCombo.DisplayMemberPath = "DisplayName"; processCombo.Margin = new Thickness(0, 0, 6, 0);
            toolbar.Children.Add(processCombo);
            toolbar.Children.Add(ToolButton("连接", async () => await AttachAsync(), true));
            toolbar.Children.Add(ToolButton("断开", Detach, false));
            toolbar.Children.Add(ToolButton("屏幕取值", async () => await ScreenEditAsync()));
            toolbar.Children.Add(new Separator { Width = 12, Opacity = .3 });
            processText = new TextBlock { Text = "未连接目标进程", Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            toolbar.Children.Add(processText);
            Grid.SetRow(toolbar, 2); return toolbar;
        }

        private Button ToolButton(string text, Action action, bool accent = false)
        {
            var button = new Button { Content = text, MinWidth = 68, Height = 30, Margin = new Thickness(3, 0, 0, 0), Padding = new Thickness(8, 0, 8, 0), Background = accent ? BrushFrom("#285F63") : BrushFrom("#303841"), Foreground = TextBrush, BorderBrush = accent ? BrushFrom("#4E9695") : LineBrush };
            button.Click += (sender, args) => action(); return button;
        }

        private UIElement BuildWorkspace()
        {
            var grid = new Grid { Margin = new Thickness(6, 6, 6, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(385) });
            grid.Children.Add(BuildLeftColumn()); grid.Children.Add(BuildCenterColumn()); grid.Children.Add(BuildRightColumn());
            Grid.SetColumn(grid.Children[0], 0); Grid.SetColumn(grid.Children[1], 1); Grid.SetColumn(grid.Children[2], 2); Grid.SetRow(grid, 3); return grid;
        }

        private UIElement BuildLeftColumn()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var stack = new StackPanel();
            stack.Children.Add(Panel("进程信息", BuildProcessInfo()));
            stack.Children.Add(Panel("扫描器", BuildScanner()));
            stack.Children.Add(Panel("指针解析", BuildPointerActions()));
            stack.Children.Add(Panel("配置", BuildProfileActions()));
            stack.Children.Add(Panel("地址操作", BuildAddressActions()));
            scroll.Content = stack; return scroll;
        }

        private UIElement BuildProfileActions()
        {
            var stack = new StackPanel { Margin = new Thickness(8) };
            profileName = Input("例如：我的 RPG Maker 游戏"); stack.Children.Add(Labelled("配置名称", profileName));
            profileCombo = DarkCombo(new string[0], 0); stack.Children.Add(Labelled("已保存配置", profileCombo));
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(ToolButton("保存配置", SaveProfile, true));
            row.Children.Add(ToolButton("加载配置", LoadProfile));
            stack.Children.Add(row);
            return stack;
        }

        private UIElement BuildProcessInfo()
        {
            var grid = new Grid { Margin = new Thickness(8) }; grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 4; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddInfo(grid, "进程", "—", 0, 0); AddInfo(grid, "PID", "—", 1, 0); AddInfo(grid, "架构", "—", 0, 1); AddInfo(grid, "内存", "—", 1, 1); AddInfo(grid, "路径", "—", 0, 2, 2); return grid;
        }

        private void AddInfo(Grid grid, string label, string value, int column, int row, int span = 1)
        {
            var block = new StackPanel { Margin = new Thickness(2, 3, 2, 3) }; block.Children.Add(new TextBlock { Text = label, Foreground = MutedBrush, FontSize = 10 }); block.Children.Add(new TextBlock { Text = value, Foreground = TextBrush, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(block, column); Grid.SetRow(block, row); if (span > 1) Grid.SetColumnSpan(block, span); grid.Children.Add(block);
        }

        private UIElement BuildScanner()
        {
            var stack = new StackPanel { Margin = new Thickness(8) };
            scanValue = Input("精确扫描时输入当前数值"); stack.Children.Add(Labelled("数值", scanValue));
            typeCombo = DarkCombo(new[] { "Byte", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64", "Float", "Double", "String", "ByteArray" }, 3); stack.Children.Add(Labelled("数据类型", typeCombo));
            initialCombo = DarkCombo(new[] { "精确数值", "未知初始值" }, 0); stack.Children.Add(Labelled("首次扫描", initialCombo));
            conditionCombo = DarkCombo(new[] { "精确数值", "已改变", "未改变", "增加", "减少" }, 0); stack.Children.Add(Labelled("再次扫描条件", conditionCombo));
            initialCombo.SelectionChanged += (sender, args) => UpdateScanInputState();
            conditionCombo.SelectionChanged += (sender, args) => UpdateScanInputState();
            typeCombo.SelectionChanged += (sender, args) => UpdateScanInputState();
            var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(ToolButton("首次扫描", async () => await FirstScanAsync(), true)); row.Children.Add(ToolButton("再次扫描", async () => await NextScanAsync())); stack.Children.Add(row);
            var row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) }; row2.Children.Add(ToolButton("新建扫描", NewScan)); row2.Children.Add(ToolButton("撤销筛选", UndoScan)); row2.Children.Add(ToolButton("取消", CancelScan)); stack.Children.Add(row2);
            progress = new ProgressBar { Height = 3, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 9, 0, 3), Foreground = AccentBrush }; stack.Children.Add(progress);
            scanCountText = new TextBlock { Text = "0 个结果", Foreground = MutedBrush }; stack.Children.Add(scanCountText); return stack;
        }

        private UIElement BuildAddressActions()
        {
            var stack = new StackPanel { Margin = new Thickness(8) }; stack.Children.Add(ToolButton("添加选中地址", AddSelectedAddresses, true)); stack.Children.Add(ToolButton("写入选中地址", async () => await WriteSelectedAddressAsync())); stack.Children.Add(ToolButton("恢复原始值", async () => await RestoreSelectedAsync())); stack.Children.Add(ToolButton("刷新地址数值", async () => await RefreshAddressesAsync())); stack.Children.Add(ToolButton("取消全部冻结", DisableAllFreeze)); return stack;
        }

        private UIElement BuildPointerActions()
        {
            var stack = new StackPanel { Margin = new Thickness(8) };
            moduleCombo = DarkCombo(new string[0], 0);
            moduleCombo.DisplayMemberPath = "DisplayName";
            stack.Children.Add(Labelled("模块", moduleCombo));
            moduleOffsetBox = Input("例如：0x1A4F920");
            stack.Children.Add(Labelled("模块偏移", moduleOffsetBox));
            pointerOffsetsBox = Input("例如：0x30, 0x18, 0x88");
            stack.Children.Add(Labelled("后续偏移（从左到右）", pointerOffsetsBox));
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(ToolButton("刷新模块", async () => await RefreshModulesAsync()));
            row.Children.Add(ToolButton("解析指针", async () => await ResolvePointerAsync(), true));
            stack.Children.Add(row);
            pointerResultText = new TextBlock { Text = "未解析", Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 0) };
            stack.Children.Add(pointerResultText);
            return stack;
        }

        private UIElement BuildCenterColumn()
        {
            var grid = new Grid { Margin = new Thickness(5, 0, 5, 0) }; grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(190) });
            resultsGrid = CreateGrid(); resultsGrid.SelectionMode = DataGridSelectionMode.Extended; resultsGrid.Columns.Add(Column("地址", "Address", 150)); resultsGrid.Columns.Add(Column("数值", "Value", 110)); resultsGrid.Columns.Add(Column("类型", "Type", 80)); grid.Children.Add(Panel("扫描结果", resultsGrid));
            regionsGrid = CreateGrid(); regionsGrid.Columns.Add(Column("基址", "baseAddress", 150)); regionsGrid.Columns.Add(Column("大小", "size", 100)); regionsGrid.Columns.Add(Column("保护", "protection", 80)); regionsGrid.Columns.Add(Column("类型", "type", 80)); var regionsPanel = Panel("内存区域", regionsGrid); Grid.SetRow(regionsPanel, 1); grid.Children.Add(regionsPanel);
            Grid.SetRow(grid, 0); return grid;
        }

        private UIElement BuildRightColumn()
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 0) }; grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(155) });
            addressesGrid = CreateGrid(); addressesGrid.IsReadOnly = false; addressesGrid.SelectionMode = DataGridSelectionMode.Single; addressesGrid.Columns.Add(Column("描述", "Description", 100)); addressesGrid.Columns.Add(Column("地址", "Address", 130)); addressesGrid.Columns.Add(Column("当前值", "CurrentValue", 80)); addressesGrid.Columns.Add(Column("新值", "NewValue", 80)); addressesGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "冻", Binding = new Binding("Frozen") }); addressesGrid.CellEditEnding += (sender, args) => Dispatcher.BeginInvoke(new Action(UpdateFreezeTimer)); grid.Children.Add(Panel("地址列表", addressesGrid));
            var help = new StackPanel { Margin = new Thickness(10) }; help.Children.Add(new TextBlock { Text = "操作提示", Foreground = AccentBrush, FontWeight = FontWeights.Bold }); help.Children.Add(new TextBlock { Text = "精确扫描：输入当前值后首次扫描。\n未知初始值：首次扫描无需输入，回到游戏改变数值后筛选。\n选中结果后添加到地址列表，双击新值单元格可编辑。", Foreground = MutedBrush, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap }); var helpPanel = Panel("帮助", help); Grid.SetRow(helpPanel, 1); grid.Children.Add(helpPanel); Grid.SetColumn(grid, 2); return grid;
        }

        private UIElement BuildLogPanel()
        {
            logBox = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = InputBrush, Foreground = MutedBrush, BorderBrush = LineBrush, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 11, Margin = new Thickness(6, 0, 6, 4) }; Grid.SetRow(logBox, 4); return logBox;
        }

        private UIElement BuildStatusBar()
        {
            var bar = new DockPanel { Background = BrushFrom("#171B20"), LastChildFill = false }; statusText = new TextBlock { Text = "未连接", Foreground = MutedBrush, Margin = new Thickness(10, 3, 0, 0) }; bar.Children.Add(statusText); var right = new TextBlock { Text = "WPF 原生桌面版 · 本地离线工具", Foreground = MutedBrush, Margin = new Thickness(0, 3, 10, 0) }; DockPanel.SetDock(right, Dock.Right); bar.Children.Add(right); Grid.SetRow(bar, 5); return bar;
        }

        private Border Panel(string title, UIElement content)
        {
            var dock = new DockPanel(); var header = new TextBlock { Text = title, Foreground = TextBrush, Background = HeaderBrush, Padding = new Thickness(9, 7, 9, 6), FontWeight = FontWeights.SemiBold }; DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header); dock.Children.Add(content); return new Border { Background = PanelBrush, BorderBrush = LineBrush, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 5), Child = dock };
        }

        private static DataGrid CreateGrid()
        {
            var grid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column, Background = PanelBrush, Foreground = TextBrush, RowBackground = PanelBrush, AlternatingRowBackground = BrushFrom("#1D2329"), GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, HorizontalGridLinesBrush = BrushFrom("#2C343C"), BorderThickness = new Thickness(0), SelectionUnit = DataGridSelectionUnit.FullRow, RowHeaderWidth = 0 };
            var cellStyle = new Style(typeof(DataGridCell));
            cellStyle.Setters.Add(new Setter(Control.BackgroundProperty, PanelBrush));
            cellStyle.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
            cellStyle.Setters.Add(new Setter(Control.BorderBrushProperty, BrushFrom("#2C343C")));
            var selectedCell = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
            selectedCell.Setters.Add(new Setter(Control.BackgroundProperty, BrushFrom("#285F63")));
            selectedCell.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            cellStyle.Triggers.Add(selectedCell);
            var rowStyle = new Style(typeof(DataGridRow));
            rowStyle.Setters.Add(new Setter(Control.BackgroundProperty, PanelBrush));
            rowStyle.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
            var headerStyle = new Style(typeof(DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, HeaderBrush));
            headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
            headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, LineBrush));
            headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7, 4, 7, 4)));
            grid.Resources.Add(typeof(DataGridCell), cellStyle);
            grid.Resources.Add(typeof(DataGridRow), rowStyle);
            grid.Resources.Add(typeof(DataGridColumnHeader), headerStyle);
            return grid;
        }
        private static DataGridTextColumn Column(string header, string path, double width) { return new DataGridTextColumn { Header = header, Binding = new Binding(path), Width = width }; }
        private static TextBox Input(string hint) { return new TextBox { Height = 29, Text = "", ToolTip = hint, Background = InputBrush, Foreground = TextBrush, BorderBrush = LineBrush, Padding = new Thickness(7, 4, 7, 4) }; }
        private static StackPanel Labelled(string label, UIElement input) { var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 7) }; stack.Children.Add(new TextBlock { Text = label, Foreground = MutedBrush, FontSize = 11 }); stack.Children.Add(input); return stack; }

        private static ComboBox DarkCombo(IEnumerable<string> items, int selectedIndex)
        {
            var combo = new ComboBox { ItemsSource = items, SelectedIndex = selectedIndex, Height = 29, Foreground = TextBrush, Background = InputBrush, BorderBrush = LineBrush, Padding = new Thickness(5, 0, 5, 0) };
            combo.Resources[SystemColors.WindowBrushKey] = InputBrush;
            combo.Resources[SystemColors.WindowTextBrushKey] = TextBrush;
            combo.Resources[SystemColors.ControlBrushKey] = InputBrush;
            combo.Resources[SystemColors.ControlTextBrushKey] = TextBrush;
            combo.Resources[SystemColors.MenuBrushKey] = PanelBrush;
            combo.Resources[SystemColors.MenuTextBrushKey] = TextBrush;
            combo.Resources[SystemColors.HighlightBrushKey] = BrushFrom("#285F63");
            combo.Resources[SystemColors.HighlightTextBrushKey] = TextBrush;
            var itemStyle = new Style(typeof(ComboBoxItem));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, InputBrush));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
            itemStyle.Setters.Add(new Setter(Control.BorderBrushProperty, LineBrush));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7, 5, 7, 5)));
            var highlighted = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
            highlighted.Setters.Add(new Setter(Control.BackgroundProperty, BrushFrom("#285F63")));
            highlighted.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            itemStyle.Triggers.Add(highlighted);
            combo.ItemContainerStyle = itemStyle;
            combo.Template = DarkComboTemplate();
            return combo;
        }

        private static ControlTemplate DarkComboTemplate()
        {
            var template = new ControlTemplate(typeof(ComboBox));
            var root = new FrameworkElementFactory(typeof(Grid));
            var outer = new FrameworkElementFactory(typeof(Border));
            outer.SetBinding(Border.BackgroundProperty, TemplateBinding("Background"));
            outer.SetBinding(Border.BorderBrushProperty, TemplateBinding("BorderBrush"));
            outer.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            outer.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            root.AppendChild(outer);

            var selected = new FrameworkElementFactory(typeof(ContentPresenter));
            selected.SetBinding(ContentPresenter.ContentProperty, TemplateBinding("SelectionBoxItem"));
            selected.SetBinding(ContentPresenter.ContentTemplateProperty, TemplateBinding("SelectionBoxItemTemplate"));
            selected.SetBinding(TextElement.ForegroundProperty, TemplateBinding("Foreground"));
            selected.SetValue(ContentPresenter.MarginProperty, new Thickness(8, 0, 32, 0));
            selected.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            selected.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            selected.SetValue(UIElement.IsHitTestVisibleProperty, false);
            root.AppendChild(selected);

            var toggle = new FrameworkElementFactory(typeof(ToggleButton));
            toggle.Name = "PART_ToggleButton";
            toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding("IsDropDownOpen") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent), Mode = BindingMode.TwoWay });
            toggle.SetValue(Grid.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            toggle.SetValue(FrameworkElement.WidthProperty, 28.0);
            toggle.SetValue(FrameworkElement.HeightProperty, Double.NaN);
            toggle.SetValue(ToggleButton.BackgroundProperty, InputBrush);
            toggle.SetValue(ToggleButton.BorderThicknessProperty, new Thickness(0));
            toggle.SetValue(ToggleButton.FocusableProperty, false);
            toggle.SetValue(Control.TemplateProperty, DarkToggleTemplate());
            var arrow = new FrameworkElementFactory(typeof(TextBlock));
            arrow.SetValue(TextBlock.TextProperty, "▼");
            arrow.SetValue(TextBlock.FontSizeProperty, 9.0);
            arrow.SetValue(TextBlock.ForegroundProperty, MutedBrush);
            arrow.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            arrow.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            toggle.AppendChild(arrow);
            root.AppendChild(toggle);

            var popup = new FrameworkElementFactory(typeof(Popup));
            popup.Name = "PART_Popup";
            popup.SetBinding(Popup.IsOpenProperty, new Binding("IsDropDownOpen") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent), Mode = BindingMode.TwoWay });
            popup.SetBinding(Popup.PlacementTargetProperty, new Binding { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popup.SetValue(Popup.StaysOpenProperty, false);
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 0));
            var popupBorder = new FrameworkElementFactory(typeof(Border));
            popupBorder.SetValue(Border.BackgroundProperty, InputBrush);
            popupBorder.SetValue(Border.BorderBrushProperty, LineBrush);
            popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            popupBorder.SetBinding(FrameworkElement.MinWidthProperty, new Binding("ActualWidth") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
            scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            scroll.SetValue(FrameworkElement.MaxHeightProperty, 280.0);
            var itemsPresenter = new FrameworkElementFactory(typeof(ItemsPresenter));
            scroll.AppendChild(itemsPresenter);
            popupBorder.AppendChild(scroll);
            popup.AppendChild(popupBorder);
            root.AppendChild(popup);
            template.VisualTree = root;
            return template;
        }

        private static ControlTemplate DarkToggleTemplate()
        {
            var template = new ControlTemplate(typeof(ToggleButton));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BackgroundProperty, TemplateBinding("Background"));
            border.SetBinding(Border.BorderBrushProperty, TemplateBinding("BorderBrush"));
            border.SetBinding(Border.BorderThicknessProperty, TemplateBinding("BorderThickness"));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetBinding(ContentPresenter.ContentProperty, TemplateBinding("Content"));
            content.SetBinding(TextElement.ForegroundProperty, TemplateBinding("Foreground"));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            template.VisualTree = border;
            return template;
        }

        private static Binding TemplateBinding(string path)
        {
            return new Binding(path) { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent), Mode = BindingMode.OneWay };
        }

        private static Style DarkScrollBarStyle()
        {
            var style = new Style(typeof(ScrollBar));
            style.Setters.Add(new Setter(Control.BackgroundProperty, BrushFrom("#171D23")));
            style.Setters.Add(new Setter(Control.ForegroundProperty, BrushFrom("#566673")));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, BrushFrom("#2D3943")));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(ScrollBar.WidthProperty, 10.0));
            style.Setters.Add(new Setter(ScrollBar.HeightProperty, 10.0));
            style.Setters.Add(new Setter(Control.TemplateProperty, DarkScrollBarTemplate()));
            var vertical = new Trigger { Property = ScrollBar.OrientationProperty, Value = Orientation.Vertical };
            vertical.Setters.Add(new Setter(ScrollBar.WidthProperty, 10.0));
            vertical.Setters.Add(new Setter(ScrollBar.HeightProperty, Double.NaN));
            var horizontal = new Trigger { Property = ScrollBar.OrientationProperty, Value = Orientation.Horizontal };
            horizontal.Setters.Add(new Setter(ScrollBar.WidthProperty, Double.NaN));
            horizontal.Setters.Add(new Setter(ScrollBar.HeightProperty, 10.0));
            style.Triggers.Add(vertical);
            style.Triggers.Add(horizontal);
            style.Resources[SystemColors.ScrollBarBrushKey] = BrushFrom("#171D23");
            style.Resources[SystemColors.ControlBrushKey] = BrushFrom("#242C34");
            style.Resources[SystemColors.ControlDarkBrushKey] = BrushFrom("#12171C");
            style.Resources[SystemColors.ControlLightBrushKey] = BrushFrom("#35404A");
            return style;
        }

        private static Style DarkThumbStyle()
        {
            var style = new Style(typeof(Thumb));
            style.Setters.Add(new Setter(Control.BackgroundProperty, BrushFrom("#566673")));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, BrushFrom("#6B7D8B")));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 28.0));
            style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 28.0));
            style.Setters.Add(new Setter(Control.TemplateProperty, DarkThumbTemplate()));
            var hover = new Trigger { Property = Thumb.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, AccentBrush));
            hover.Setters.Add(new Setter(Control.BorderBrushProperty, BrushFrom("#8DE0DB")));
            style.Triggers.Add(hover);
            return style;
        }

        private static Style DarkRepeatButtonStyle()
        {
            var style = new Style(typeof(RepeatButton));
            style.Setters.Add(new Setter(Control.BackgroundProperty, BrushFrom("#242C34")));
            style.Setters.Add(new Setter(Control.ForegroundProperty, MutedBrush));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, BrushFrom("#2D3943")));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 0.0));
            style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 0.0));
            style.Setters.Add(new Setter(Control.TemplateProperty, DarkRepeatButtonTemplate()));
            return style;
        }

        private static ControlTemplate DarkScrollBarTemplate()
        {
            const string xaml = @"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ScrollBar}'>
  <Border Background='#171D23' BorderBrush='#2D3943' BorderThickness='1'>
    <Track x:Name='PART_Track' Orientation='{TemplateBinding Orientation}' Maximum='{TemplateBinding Maximum}' Minimum='{TemplateBinding Minimum}' Value='{TemplateBinding Value}' ViewportSize='{TemplateBinding ViewportSize}' IsDirectionReversed='True'>
      <Track.DecreaseRepeatButton>
        <RepeatButton Command='{x:Static ScrollBar.LineUpCommand}' Background='Transparent' BorderThickness='0' Height='0' Width='0' MinHeight='0' MinWidth='0'/>
      </Track.DecreaseRepeatButton>
      <Track.Thumb>
        <Thumb Background='#566673' BorderBrush='#6B7D8B' BorderThickness='1' MinHeight='28' MinWidth='28'>
          <Thumb.Template>
            <ControlTemplate TargetType='{x:Type Thumb}'>
              <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='3'/>
            </ControlTemplate>
          </Thumb.Template>
        </Thumb>
      </Track.Thumb>
      <Track.IncreaseRepeatButton>
        <RepeatButton Command='{x:Static ScrollBar.LineDownCommand}' Background='Transparent' BorderThickness='0' Height='0' Width='0' MinHeight='0' MinWidth='0'/>
      </Track.IncreaseRepeatButton>
    </Track>
  </Border>
</ControlTemplate>";
            return (ControlTemplate)XamlReader.Parse(xaml);
        }

        private static ControlTemplate DarkThumbTemplate()
        {
            var template = new ControlTemplate(typeof(Thumb));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BackgroundProperty, TemplateBinding("Background"));
            border.SetBinding(Border.BorderBrushProperty, TemplateBinding("BorderBrush"));
            border.SetBinding(Border.BorderThicknessProperty, TemplateBinding("BorderThickness"));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            template.VisualTree = border;
            return template;
        }

        private static ControlTemplate DarkRepeatButtonTemplate()
        {
            var template = new ControlTemplate(typeof(RepeatButton));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BackgroundProperty, TemplateBinding("Background"));
            border.SetBinding(Border.BorderBrushProperty, TemplateBinding("BorderBrush"));
            border.SetBinding(Border.BorderThicknessProperty, TemplateBinding("BorderThickness"));
            template.VisualTree = border;
            return template;
        }

        private void UpdateScanInputState()
        {
            if (scanValue == null || typeCombo == null || initialCombo == null || conditionCombo == null) return;
            bool firstScan = scanHistory.Count == 0;
            bool needsValue = firstScan ? initialCombo.SelectedIndex == (int)ScanInitialMode.Exact : conditionCombo.SelectedIndex == (int)ScanCondition.Exact;
            scanValue.IsEnabled = needsValue;
            scanValue.Opacity = needsValue ? 1.0 : 0.45;
            string hint = SelectedType() == ScanDataType.ByteArray ? "输入十六进制字节，例如：48 8B 05" : (SelectedType() == ScanDataType.String ? "输入 UTF-8 字符串" : "输入当前数值");
            scanValue.ToolTip = needsValue ? hint : "当前扫描条件不需要输入数值";
        }

        private async Task RefreshProcessesAsync()
        {
            try { processes = await bridge.ListProcessesAsync(true); processCombo.ItemsSource = processes; if (attached != null) processCombo.SelectedItem = processes.FirstOrDefault(item => item.pid == attached.pid); Log("信息", "任务栏进程列表已刷新：" + processes.Length + " 个"); }
            catch (Exception error) { Log("错误", error.Message); }
        }

        private async Task AttachAsync()
        {
            var selected = processCombo.SelectedItem as ProcessInfo; if (selected == null) { Message("请选择目标进程。"); return; }
            try { attached = selected; regions = await bridge.ListRegionsAsync(attached.pid); modules = await bridge.ListModulesAsync(attached.pid); regionsGrid.ItemsSource = regions; moduleCombo.ItemsSource = modules; moduleCombo.SelectedItem = modules.FirstOrDefault(item => String.Equals(item.name, attached.name, StringComparison.OrdinalIgnoreCase)); processText.Text = "已连接：" + attached.name + "  PID " + attached.pid; statusText.Text = "已连接 · " + attached.name; if (String.IsNullOrWhiteSpace(profileName.Text)) profileName.Text = attached.name.Replace(".exe", ""); UpdateFreezeTimer(); watchTimer.Start(); Log("成功", "已连接进程：" + attached.name + "，模块 " + modules.Length + " 个"); if (addresses.Count > 0) await RefreshAddressesAsync(); }
            catch (Exception error) { attached = null; Log("错误", error.Message); }
        }

        private void Detach()
        {
            freezeTimer.Stop(); watchTimer.Stop(); attached = null; regions = new MemoryRegion[0]; modules = new ModuleInfo[0]; moduleCombo.ItemsSource = modules; regionsGrid.ItemsSource = regions; scanHistory.Clear(); scanResults = new List<ScanResult>(); resultsGrid.ItemsSource = scanResults; processText.Text = "未连接目标进程"; statusText.Text = "未连接"; Log("信息", "已断开进程");
        }

        private void ProcessWatchTimerOnTick(object sender, EventArgs args)
        {
            if (attached == null) return;
            try
            {
                using (var target = Process.GetProcessById(attached.pid))
                {
                    if (!target.HasExited) return;
                }
            }
            catch (Exception error)
            {
                Log("警告", "目标进程已不可用：" + error.Message);
            }
            HandleProcessClosed();
        }

        private void HandleProcessClosed()
        {
            freezeTimer.Stop();
            watchTimer.Stop();
            attached = null;
            regions = new MemoryRegion[0];
            modules = new ModuleInfo[0];
            scanHistory.Clear();
            scanResults = new List<ScanResult>();
            processCombo.SelectedItem = null;
            regionsGrid.ItemsSource = regions;
            moduleCombo.ItemsSource = modules;
            resultsGrid.ItemsSource = scanResults;
            scanCountText.Text = "0 个结果";
            processText.Text = "目标进程已关闭";
            statusText.Text = "未连接";
            Log("警告", "目标进程已关闭，已停止冻结并清空当前扫描。");
        }

        private async Task RefreshRegionsAsync() { if (attached == null) { Message("请先连接目标进程。"); return; } try { regions = await bridge.ListRegionsAsync(attached.pid); regionsGrid.ItemsSource = regions; Log("信息", "内存区域已刷新：" + regions.Length + " 个"); } catch (Exception error) { Log("错误", error.Message); } }

        private async Task RefreshModulesAsync()
        {
            if (attached == null) { Message("请先连接目标进程。"); return; }
            try
            {
                modules = await bridge.ListModulesAsync(attached.pid);
                moduleCombo.ItemsSource = modules;
                moduleCombo.SelectedItem = modules.FirstOrDefault(item => String.Equals(item.name, attached.name, StringComparison.OrdinalIgnoreCase));
                Log("信息", "模块列表已刷新：" + modules.Length + " 个");
            }
            catch (Exception error) { Log("错误", "模块列表刷新失败：" + error.Message); }
        }

        private void OpenAobScanner()
        {
            if (attached == null) { Message("请先连接目标进程。"); return; }
            var window = new AobScanWindow(bridge, attached, modules, AddAobMatchAsync) { Owner = this };
            window.Show();
        }

        private void OpenPointerScanner()
        {
            if (attached == null) { Message("请先连接目标进程。"); return; }
            var selected = resultsGrid.SelectedItem as ScanResult;
            var window = new PointerScanWindow(bridge, attached, modules, AddPointerPathAsync, selected == null ? "" : selected.Address) { Owner = this };
            window.Show();
            if (selected != null) Log("信息", "Pointer Scan 已预填选中结果地址：" + selected.Address);
        }

        private void OpenSaveEditor()
        {
            string backupDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KillWind", "Backups");
            new SaveEditorWindow(backupDirectory) { Owner = this }.Show();
        }

        private void OpenMemoryViewer()
        {
            if (attached == null) { Message("请先连接目标进程。"); return; }
            new MemoryViewerWindow(bridge, attached) { Owner = this }.Show();
        }

        private void OpenTrainer()
        {
            if (attached == null) { Message("请先连接目标进程。"); return; }
            new TrainerWindow(addresses, WriteEntryAsync, ToggleFreezeEntry) { Owner = this }.Show();
        }

        private async Task AddAobMatchAsync(AobMatch match)
        {
            try
            {
                int size = AobPattern.Parse(match.Pattern).Bytes.Length;
                byte[] bytes = await bridge.ReadAsync(attached.pid, match.AddressValue, size);
                string value = SaveEditorService.FormatBytes(bytes);
                if (!addresses.Any(item => item.Address.Equals(match.Address, StringComparison.OrdinalIgnoreCase))) addresses.Add(new AddressEntry { Description = "AOB 匹配", Address = match.Address, CurrentValue = value, NewValue = value, OriginalValue = value, Type = "ByteArray", Size = size, Module = match.Module, Signature = match.Pattern });
                addressesGrid.ItemsSource = null; addressesGrid.ItemsSource = addresses; Log("信息", "AOB 地址已加入地址列表：" + match.Address);
            }
            catch (Exception error) { Log("错误", "加入 AOB 地址失败：" + error.Message); }
        }

        private async Task AddPointerPathAsync(PointerPath path)
        {
            ModuleInfo module = modules.FirstOrDefault(item => String.Equals(item.name, path.Module, StringComparison.OrdinalIgnoreCase));
            if (module == null) { Message("指针路径模块未找到。"); return; }
            await AddResolvedPointerAsync(module, path.ModuleOffset, path.Offsets ?? new ulong[0], path.TargetAddress);
        }

        private async Task ResolvePointerAsync()
        {
            if (attached == null) { Message("请先连接目标进程。"); return; }
            var module = moduleCombo.SelectedItem as ModuleInfo;
            if (module == null) { Message("请选择模块。"); return; }
            try
            {
                ulong moduleOffset = PointerParser.ParseHex(moduleOffsetBox.Text);
                ulong[] offsets = PointerParser.ParseOffsets(pointerOffsetsBox.Text);
                ulong address = await pointerResolver.ResolveAsync(attached, module, moduleOffset, offsets);
                string pointerText = module.name + "+" + PointerParser.Format(moduleOffset) + (offsets.Length == 0 ? "" : " -> " + String.Join(" -> ", offsets.Select(PointerParser.Format).ToArray()));
                pointerResultText.Text = pointerText + "\n目标地址：" + PointerParser.Format(address);
                Log("成功", "指针已解析：" + pointerText + " → " + PointerParser.Format(address));
                await AddResolvedPointerAsync(module, moduleOffset, offsets, address);
            }
            catch (FormatException error) { Message(error.Message); }
            catch (OverflowException) { Message("指针地址或偏移超出当前进程地址范围。"); }
            catch (Exception error) { pointerResultText.Text = "解析失败：" + error.Message; Log("警告", "指针解析失败：" + error.Message); }
        }

        private async Task AddResolvedPointerAsync(ModuleInfo module, ulong moduleOffset, ulong[] offsets, ulong address)
        {
            string type = typeCombo.SelectedItem as string ?? "Int32";
            if (type == "String" || type == "ByteArray") { Message("指针目标暂时请选择数值类型。"); return; }
            int size = DataWidth(type, 0);
            string value = "?";
            try { value = DecodeDisplay(type, await bridge.ReadAsync(attached.pid, address, size)); }
            catch (Exception error) { Log("警告", "指针目标读取失败：" + error.Message); }
            if (addresses.Any(item => item.Address.Equals(PointerParser.Format(address), StringComparison.OrdinalIgnoreCase))) return;
            addresses.Add(new AddressEntry { Description = "指针目标", Address = PointerParser.Format(address), CurrentValue = value, NewValue = value, OriginalValue = value, Type = type, Size = size, Module = module.name, ModuleOffset = PointerParser.Format(moduleOffset), PointerOffsets = String.Join(", ", offsets.Select(PointerParser.Format).ToArray()) });
            addressesGrid.ItemsSource = null; addressesGrid.ItemsSource = addresses;
            Log("信息", "指针目标已加入地址列表。");
        }

        private async Task FirstScanAsync()
        {
            if (attached == null) { Message("请先连接目标进程。"); return; }
            ScanDataType type = SelectedType();
            if ((ScanInitialMode)initialCombo.SelectedIndex == ScanInitialMode.Unknown)
            {
                if (type == ScanDataType.String || type == ScanDataType.ByteArray) { Message("String 和 ByteArray 需要输入精确内容。"); return; }
                await RunScan(async token => await scanner.FirstUnknownAsync(attached, regions, type, new Progress<int>(value => progress.Value = value), token), "未知初始值扫描完成", true);
            }
            else
                await RunScan(async token => await scanner.FirstExactAsync(attached, regions, type, scanValue.Text, new Progress<int>(value => progress.Value = value), token), "首次扫描完成", true);
        }

        private async Task NextScanAsync()
        {
            if (attached == null || scanResults.Count == 0) { Message("请先完成首次扫描。"); return; }
            ScanCondition condition = (ScanCondition)conditionCombo.SelectedIndex;
            ScanDataType type = SelectedType();
            if ((type == ScanDataType.String || type == ScanDataType.ByteArray) && (condition == ScanCondition.Increased || condition == ScanCondition.Decreased)) { Message("String 和 ByteArray 只支持精确、已改变和未改变筛选。"); return; }
            await RunScan(async token => await scanner.FilterAsync(attached, scanResults, type, condition, scanValue.Text, new Progress<int>(value => progress.Value = value), token), "再次扫描完成", false);
        }

        private async Task ScreenEditAsync()
        {
            if (attached == null) { Message("请先连接 RPG Maker 游戏进程。"); return; }
            try
            {
                statusText.Text = "请在游戏画面中点击数字";
                Log("信息", "屏幕取值模式已开启，请点击游戏画面中的数字。");
                ScreenPoint point = await bridge.PickScreenPointAsync(attached.pid, 30000);
                if (point == null || point.status != "picked") { statusText.Text = "已连接 · " + attached.name; Message("取点已取消或超时。"); return; }
                var dialog = new ValueDialog(point) { Owner = this };
                if (dialog.ShowDialog() != true) { statusText.Text = "已连接 · " + attached.name; return; }
                int current = Int32.Parse(dialog.CurrentValue, CultureInfo.InvariantCulture);
                int next = Int32.Parse(dialog.NewValue, CultureInfo.InvariantCulture);
                regions = await bridge.ListRegionsAsync(attached.pid);
                List<ScanResult> matches = await scanner.FirstExactAsync(attached, regions, ScanDataType.Int32, current.ToString(CultureInfo.InvariantCulture), new Progress<int>(value => progress.Value = value), CancellationToken.None);
                scanResults = matches;
                scanHistory.Clear(); scanHistory.Add(scanResults);
                resultsGrid.ItemsSource = scanResults;
                scanCountText.Text = matches.Count.ToString("N0") + " 个结果";
                if (matches.Count == 0) { Message("没有找到匹配的数值，请确认点击的数字和当前显示值。"); Log("警告", "屏幕取值没有匹配项。"); return; }
                if (matches.Count > 1) { Message("找到 " + matches.Count.ToString("N0") + " 个候选地址，已放入扫描结果，请进一步筛选。"); Log("警告", "屏幕取值存在多个匹配项。"); return; }
                await bridge.WriteAsync(attached.pid, matches[0].AddressValue, BitConverter.GetBytes(next));
                Log("成功", "屏幕数字已修改：" + current + " → " + next);
                statusText.Text = "已连接 · 屏幕修改完成";
            }
            catch (FormatException) { Message("请输入有效的 Int32 整数。"); }
            catch (OverflowException) { Message("数值超出 Int32 范围。"); }
            catch (Exception error) { Log("错误", "屏幕取值失败：" + error.Message); }
        }

        private async Task RunScan(Func<CancellationToken, Task<List<ScanResult>>> operation, string completedMessage, bool resetHistory)
        {
            try { scanCancellation = new CancellationTokenSource(); statusText.Text = "扫描中"; progress.Value = 0; scanResults = await operation(scanCancellation.Token); if (resetHistory) scanHistory.Clear(); scanHistory.Add(scanResults); typeCombo.IsEnabled = false; initialCombo.IsEnabled = false; resultsGrid.ItemsSource = scanResults; scanCountText.Text = scanResults.Count.ToString("N0") + " 个结果"; statusText.Text = "已连接 · 扫描完成"; UpdateScanInputState(); Log("成功", completedMessage + "：" + scanResults.Count.ToString("N0") + " 个结果"); }
            catch (OperationCanceledException) { Log("信息", "扫描已取消"); }
            catch (Exception error) { Log("错误", error.Message); }
            finally { scanCancellation = null; progress.Value = 0; }
        }

        private void CancelScan() { if (scanCancellation != null) scanCancellation.Cancel(); }
        private void NewScan() { if (scanCancellation != null) scanCancellation.Cancel(); scanHistory.Clear(); scanResults = new List<ScanResult>(); typeCombo.IsEnabled = true; initialCombo.IsEnabled = true; resultsGrid.ItemsSource = scanResults; scanCountText.Text = "0 个结果"; progress.Value = 0; UpdateScanInputState(); Log("信息", "已新建扫描"); }

        private void UndoScan()
        {
            if (scanHistory.Count < 2) { Message("当前没有可撤销的筛选。"); return; }
            scanHistory.RemoveAt(scanHistory.Count - 1);
            scanResults = scanHistory[scanHistory.Count - 1];
            resultsGrid.ItemsSource = scanResults;
            scanCountText.Text = scanResults.Count.ToString("N0") + " 个结果";
            UpdateScanInputState();
            Log("信息", "已撤销上一次筛选，恢复到 " + scanResults.Count.ToString("N0") + " 个结果。");
        }

        private void SaveProfile()
        {
            if (attached == null) { Message("请先连接目标进程。"); return; }
            try
            {
                string name = String.IsNullOrWhiteSpace(profileName.Text) ? attached.name.Replace(".exe", "") : profileName.Text.Trim();
                profileStore.Save(name, attached, addresses);
                RefreshProfileList();
                profileCombo.SelectedItem = name;
                Log("成功", "Profile 已保存：" + name);
            }
            catch (Exception error) { Log("错误", "Profile 保存失败：" + error.Message); }
        }

        private void LoadProfile()
        {
            string name = profileCombo == null ? "" : profileCombo.SelectedItem as string;
            if (String.IsNullOrWhiteSpace(name)) { Message("请选择要加载的 Profile。"); return; }
            try
            {
                ProfileRecord record = profileStore.Load(name);
                addresses.Clear();
                foreach (ProfileAddress saved in record.addresses)
                    addresses.Add(new AddressEntry { Description = saved.description, Address = saved.address, CurrentValue = saved.currentValue, NewValue = saved.newValue, OriginalValue = String.IsNullOrWhiteSpace(saved.originalValue) ? saved.currentValue : saved.originalValue, Type = saved.type, Size = saved.size, Module = saved.module, ModuleOffset = saved.moduleOffset, PointerOffsets = saved.pointerOffsets, Signature = saved.signature, Frozen = saved.frozen });
                profileName.Text = record.gameName;
                addressesGrid.ItemsSource = null; addressesGrid.ItemsSource = addresses; UpdateFreezeTimer();
                Log("成功", "Profile 已加载：" + record.gameName + "，地址 " + addresses.Count + " 项");
            }
            catch (Exception error) { Log("错误", "Profile 加载失败：" + error.Message); }
        }

        private void RefreshProfileList()
        {
            if (profileCombo == null) return;
            try { profileCombo.ItemsSource = profileStore.List(); }
            catch (Exception error) { Log("警告", "Profile 列表读取失败：" + error.Message); }
        }

        private void UpdateFreezeTimer()
        {
            if (attached != null && addresses.Any(item => item.Frozen)) freezeTimer.Start();
            else freezeTimer.Stop();
        }
        private void AddSelectedAddresses()
        {
            foreach (ScanResult result in resultsGrid.SelectedItems)
                if (!addresses.Any(item => item.Address.Equals(result.Address, StringComparison.OrdinalIgnoreCase))) addresses.Add(new AddressEntry { Description = "未命名", Address = result.Address, CurrentValue = result.Value, NewValue = result.Value, OriginalValue = result.Value, Type = result.Type, Size = result.RawValue == null ? 0 : result.RawValue.Length });
            addressesGrid.ItemsSource = null; addressesGrid.ItemsSource = addresses; Log("信息", "已添加 " + resultsGrid.SelectedItems.Count + " 个地址");
        }

        private async Task WriteSelectedAddressAsync()
        {
            var entry = addressesGrid.SelectedItem as AddressEntry; if (entry == null || attached == null) { Message("请选择地址并连接进程。"); return; }
            try { await ResolveEntryAddressAsync(entry, true); await bridge.WriteAsync(attached.pid, entry.AddressValue, Encode(entry.Type, entry.NewValue)); entry.CurrentValue = entry.NewValue; addressesGrid.ItemsSource = null; addressesGrid.ItemsSource = addresses; Log("成功", "已写入地址 " + entry.Address); } catch (Exception error) { Log("错误", error.Message); }
        }

        private async Task RefreshAddressesAsync(bool quiet = false)
        {
            if (attached == null || addressRefreshRunning) return;
            addressRefreshRunning = true;
            try
            {
                foreach (AddressEntry entry in addresses.ToList())
                {
                    try
                    {
                        await ResolveEntryAddressAsync(entry, !quiet);
                        byte[] data = await bridge.ReadAsync(attached.pid, entry.AddressValue, DataWidth(entry.Type, entry.Size));
                        entry.CurrentValue = DecodeDisplay(entry.Type, data);
                    }
                    catch (Exception error)
                    {
                        entry.CurrentValue = "无效";
                        if (!quiet) Log("警告", "读取地址失败：" + error.Message);
                    }
                }
                addressesGrid.ItemsSource = null; addressesGrid.ItemsSource = addresses;
                if (!quiet) Log("信息", "地址数值已刷新");
            }
            finally { addressRefreshRunning = false; }
        }

        private async Task RefreshAddressesMenuAsync()
        {
            await RefreshAddressesAsync();
        }

        private async void WatchTimerOnTick(object sender, EventArgs args)
        {
            if (attached != null && addresses.Count > 0) await RefreshAddressesAsync(true);
        }

        private async Task WriteEntryAsync(AddressEntry entry)
        {
            if (attached == null) return;
            try { await ResolveEntryAddressAsync(entry, true); await bridge.WriteAsync(attached.pid, entry.AddressValue, Encode(entry.Type, entry.NewValue)); entry.CurrentValue = entry.NewValue; addressesGrid.ItemsSource = null; addressesGrid.ItemsSource = addresses; Log("成功", "Trainer 已写入：" + entry.Description); }
            catch (Exception error) { Log("错误", "Trainer 写入失败：" + error.Message); }
        }

        private void HotkeyTriggered(int index)
        {
            if (index >= addresses.Count) return;
            AddressEntry entry = addresses[index];
            Dispatcher.BeginInvoke(new Action(async () => await WriteEntryAsync(entry)));
            Log("信息", "全局快捷键 F" + (index + 1) + " 已触发：" + entry.Description);
        }

        private void ToggleFreezeEntry(AddressEntry entry)
        {
            entry.Frozen = !entry.Frozen; UpdateFreezeTimer(); addressesGrid.ItemsSource = null; addressesGrid.ItemsSource = addresses; Log("信息", entry.Description + (entry.Frozen ? " 已冻结" : " 已取消冻结"));
        }

        private async void FreezeTimerOnTick(object sender, EventArgs args)
        {
            if (attached == null) return;
            foreach (AddressEntry entry in addresses.Where(item => item.Frozen).ToList()) try { await ResolveEntryAddressAsync(entry, false); await bridge.WriteAsync(attached.pid, entry.AddressValue, Encode(entry.Type, entry.NewValue)); } catch (Exception error) { Log("错误", "冻结写入失败：" + error.Message); }
        }

        private async Task RestoreSelectedAsync()
        {
            var entry = addressesGrid.SelectedItem as AddressEntry;
            if (entry == null || attached == null) { Message("请选择地址并连接进程。"); return; }
            if (String.IsNullOrWhiteSpace(entry.OriginalValue)) { Message("该地址没有记录原始值。"); return; }
            try { await ResolveEntryAddressAsync(entry, true); await bridge.WriteAsync(attached.pid, entry.AddressValue, Encode(entry.Type, entry.OriginalValue)); entry.CurrentValue = entry.OriginalValue; entry.NewValue = entry.OriginalValue; addressesGrid.ItemsSource = null; addressesGrid.ItemsSource = addresses; Log("成功", "已恢复原始值：" + entry.Description); } catch (Exception error) { Log("错误", "恢复原始值失败：" + error.Message); }
        }

        private void DisableAllFreeze()
        {
            foreach (AddressEntry entry in addresses) entry.Frozen = false;
            UpdateFreezeTimer(); addressesGrid.ItemsSource = null; addressesGrid.ItemsSource = addresses; Log("信息", "已取消全部冻结。");
        }

        private async Task ResolveEntryAddressAsync(AddressEntry entry, bool allowSignature)
        {
            if (allowSignature && !String.IsNullOrWhiteSpace(entry.Signature))
            {
                ModuleInfo signatureModule = String.IsNullOrWhiteSpace(entry.Module) ? null : modules.FirstOrDefault(item => String.Equals(item.name, entry.Module, StringComparison.OrdinalIgnoreCase));
                MemoryRegion[] signatureRegions = await bridge.ListRegionsAsync(attached.pid, true, false);
                List<AobMatch> matches = await aobScanner.ScanAsync(attached, signatureRegions, AobPattern.Parse(entry.Signature), signatureModule, new Progress<int>(), CancellationToken.None);
                if (matches.Count != 1) throw new InvalidOperationException("Signature 匹配数量为 " + matches.Count + "，无法自动恢复地址。");
                entry.Address = matches[0].Address;
                return;
            }
            if (String.IsNullOrWhiteSpace(entry.Module) || String.IsNullOrWhiteSpace(entry.ModuleOffset)) return;
            var module = modules.FirstOrDefault(item => String.Equals(item.name, entry.Module, StringComparison.OrdinalIgnoreCase));
            if (module == null) throw new InvalidOperationException("Profile 指针模块未找到：" + entry.Module);
            ulong moduleOffset = PointerParser.ParseHex(entry.ModuleOffset);
            ulong[] offsets = PointerParser.ParseOffsets(entry.PointerOffsets);
            entry.Address = PointerParser.Format(await pointerResolver.ResolveAsync(attached, module, moduleOffset, offsets));
        }

        private void LaunchTestGame()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MemoryTestGame", "MemoryTestGame.exe"); if (!File.Exists(path)) path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "MemoryTestGame", "MemoryTestGame.exe");
            try { Process.Start(path); Log("信息", "测试程序已启动"); } catch (Exception error) { Log("错误", error.Message); }
        }

        private ScanDataType SelectedType() { return (ScanDataType)typeCombo.SelectedIndex; }
        private static int DataWidth(string type, int size)
        {
            if (type == "Byte") return 1;
            if (type == "Int16" || type == "UInt16") return 2;
            if (type == "Int64" || type == "UInt64" || type == "Double") return 8;
            if (type == "String" || type == "ByteArray") return size;
            return 4;
        }

        private static byte[] Encode(string type, string text)
        {
            if (type == "Byte") return new[] { Byte.Parse(text, CultureInfo.InvariantCulture) };
            if (type == "Int16") return BitConverter.GetBytes(Int16.Parse(text, CultureInfo.InvariantCulture));
            if (type == "UInt16") return BitConverter.GetBytes(UInt16.Parse(text, CultureInfo.InvariantCulture));
            if (type == "Int32") return BitConverter.GetBytes(Int32.Parse(text, CultureInfo.InvariantCulture));
            if (type == "UInt32") return BitConverter.GetBytes(UInt32.Parse(text, CultureInfo.InvariantCulture));
            if (type == "Int64") return BitConverter.GetBytes(Int64.Parse(text, CultureInfo.InvariantCulture));
            if (type == "UInt64") return BitConverter.GetBytes(UInt64.Parse(text, CultureInfo.InvariantCulture));
            if (type == "Float") return BitConverter.GetBytes(Single.Parse(text, CultureInfo.InvariantCulture));
            if (type == "Double") return BitConverter.GetBytes(Double.Parse(text, CultureInfo.InvariantCulture));
            if (type == "String") return Encoding.UTF8.GetBytes(text ?? "");
            return ParseByteArray(text);
        }

        private static string DecodeDisplay(string type, byte[] bytes)
        {
            if (type == "Byte") return bytes[0].ToString(CultureInfo.InvariantCulture);
            if (type == "Int16") return BitConverter.ToInt16(bytes, 0).ToString(CultureInfo.InvariantCulture);
            if (type == "UInt16") return BitConverter.ToUInt16(bytes, 0).ToString(CultureInfo.InvariantCulture);
            if (type == "Int32") return BitConverter.ToInt32(bytes, 0).ToString(CultureInfo.InvariantCulture);
            if (type == "UInt32") return BitConverter.ToUInt32(bytes, 0).ToString(CultureInfo.InvariantCulture);
            if (type == "Int64") return BitConverter.ToInt64(bytes, 0).ToString(CultureInfo.InvariantCulture);
            if (type == "UInt64") return BitConverter.ToUInt64(bytes, 0).ToString(CultureInfo.InvariantCulture);
            if (type == "Float") return BitConverter.ToSingle(bytes, 0).ToString("R", CultureInfo.InvariantCulture);
            if (type == "Double") return BitConverter.ToDouble(bytes, 0).ToString("R", CultureInfo.InvariantCulture);
            if (type == "String") return Encoding.UTF8.GetString(bytes);
            return FormatByteArray(bytes);
        }

        private static byte[] ParseByteArray(string input)
        {
            string normalized = (input ?? "").Replace(",", " ").Replace("-", " ").Trim();
            if (normalized.Length == 0) throw new FormatException("请输入十六进制字节，例如：48 8B 05。");
            string[] parts = normalized.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var bytes = new List<byte>();
            if (parts.Length == 1 && parts[0].Length > 2)
            {
                if ((parts[0].Length & 1) != 0) throw new FormatException("十六进制字节长度必须是偶数。");
                for (int index = 0; index < parts[0].Length; index += 2) bytes.Add(Byte.Parse(parts[0].Substring(index, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
            else
            {
                foreach (string part in parts) bytes.Add(Byte.Parse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
            return bytes.ToArray();
        }

        private static string FormatByteArray(byte[] bytes) { return String.Join(" ", bytes.Select(item => item.ToString("X2", CultureInfo.InvariantCulture)).ToArray()); }
        private void Log(string level, string message) { if (logBox == null) return; logBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  [" + level + "]  " + message + Environment.NewLine); logBox.ScrollToEnd(); }
        private void Message(string message) { MessageBox.Show(this, message, "KillWind", MessageBoxButton.OK, MessageBoxImage.Information); }
        private static SolidColorBrush BrushFrom(string value) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); }
    }

    internal sealed class ObservableAddressList : System.Collections.ObjectModel.ObservableCollection<AddressEntry> { }
}
