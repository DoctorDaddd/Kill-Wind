using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KillWind.Wpf
{
    internal abstract class ToolWindow : Window
    {
        protected static readonly Brush WindowBrush = BrushFrom("#111418");
        protected static readonly Brush PanelBrush = BrushFrom("#20262D");
        protected static readonly Brush HeaderBrush = BrushFrom("#2A323A");
        protected static readonly Brush InputBrush = BrushFrom("#14191E");
        protected static readonly Brush LineBrush = BrushFrom("#3A434D");
        protected static readonly Brush TextBrush = BrushFrom("#E4E8EC");
        protected static readonly Brush MutedBrush = BrushFrom("#9DA7B1");
        protected static readonly Brush AccentBrush = BrushFrom("#65C9C5");

        protected ToolWindow(string title, double width, double height)
        {
            Title = title; Width = width; Height = height; MinWidth = 620; MinHeight = 360; Owner = Application.Current == null ? null : Application.Current.MainWindow; Background = WindowBrush; Foreground = TextBrush;
            Resources[SystemColors.WindowBrushKey] = InputBrush; Resources[SystemColors.WindowTextBrushKey] = TextBrush; Resources[SystemColors.ControlBrushKey] = InputBrush; Resources[SystemColors.ControlTextBrushKey] = TextBrush; Resources[SystemColors.HighlightBrushKey] = BrushFrom("#285F63"); Resources[SystemColors.HighlightTextBrushKey] = TextBrush;
        }

        protected Button Button(string text, Action action, bool accent = false)
        {
            var button = new Button { Content = text, Height = 30, MinWidth = 80, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(9, 0, 9, 0), Background = accent ? BrushFrom("#285F63") : BrushFrom("#303841"), Foreground = TextBrush, BorderBrush = accent ? BrushFrom("#4E9695") : LineBrush };
            button.Click += (sender, args) => action(); return button;
        }

        protected TextBox Input(string hint = "") { return new TextBox { Height = 30, ToolTip = hint, Background = InputBrush, Foreground = TextBrush, BorderBrush = LineBrush, Padding = new Thickness(7, 4, 7, 4) }; }
        protected ComboBox Combo(IEnumerable<string> values) { var combo = new ComboBox { ItemsSource = values, SelectedIndex = 0, Height = 30, Background = InputBrush, Foreground = TextBrush, BorderBrush = LineBrush }; var style = new Style(typeof(ComboBoxItem)); style.Setters.Add(new Setter(Control.BackgroundProperty, InputBrush)); style.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush)); var highlight = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true }; highlight.Setters.Add(new Setter(Control.BackgroundProperty, BrushFrom("#285F63"))); highlight.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White)); style.Triggers.Add(highlight); combo.ItemContainerStyle = style; return combo; }
        protected static Border Panel(string title, UIElement content) { var dock = new DockPanel(); var header = new TextBlock { Text = title, Foreground = TextBrush, Background = HeaderBrush, Padding = new Thickness(9, 7, 9, 6), FontWeight = FontWeights.SemiBold }; DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header); dock.Children.Add(content); return new Border { Background = PanelBrush, BorderBrush = LineBrush, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 5), Child = dock }; }
        protected static DataGrid CreateGrid() { return new DataGrid { AutoGenerateColumns = true, CanUserAddRows = false, IsReadOnly = true, Background = PanelBrush, Foreground = TextBrush, RowBackground = PanelBrush, AlternatingRowBackground = BrushFrom("#1D2329"), GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, BorderThickness = new Thickness(0), HeadersVisibility = DataGridHeadersVisibility.Column, RowHeaderWidth = 0 }; }
        protected static SolidColorBrush BrushFrom(string value) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); }
        protected static DataGridTextColumn Column(string header, string path, double width) { return new DataGridTextColumn { Header = header, Binding = new System.Windows.Data.Binding(path), Width = width }; }
    }

    internal sealed class AobScanWindow : ToolWindow
    {
        private readonly NativeBridgeClient bridge;
        private readonly ProcessInfo process;
        private readonly ModuleInfo[] modules;
        private readonly AobScanner scanner;
        private readonly Func<AobMatch, Task> addMatch;
        private TextBox patternBox;
        private ComboBox moduleCombo;
        private DataGrid resultsGrid;
        private TextBlock status;
        private CancellationTokenSource cancellation;

        public AobScanWindow(NativeBridgeClient bridge, ProcessInfo process, ModuleInfo[] modules, Func<AobMatch, Task> addMatch) : base("AOB / Signature Scan", 900, 600)
        {
            this.bridge = bridge; this.process = process; this.modules = modules ?? new ModuleInfo[0]; this.scanner = new AobScanner(bridge); this.addMatch = addMatch;
            var root = new Grid { Margin = new Thickness(8) }; root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var controls = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
            patternBox = Input("例如：48 8B 05 ?? ?? ?? ?? 48 85 C0"); patternBox.Width = 330; controls.Children.Add(patternBox);
            moduleCombo = Combo(new[] { "整个进程" }.Concat(this.modules.Select(item => item.name))); moduleCombo.Width = 210; moduleCombo.Margin = new Thickness(6, 0, 0, 0); controls.Children.Add(moduleCombo);
            controls.Children.Add(Button("扫描", async () => await ScanAsync(), true)); controls.Children.Add(Button("取消", CancelScan));
            status = new TextBlock { Text = "支持 ?? 通配符", Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) }; controls.Children.Add(status);
            root.Children.Add(controls);
            resultsGrid = CreateGrid(); resultsGrid.Columns.Add(Column("地址", "Address", 170)); resultsGrid.Columns.Add(Column("模块", "Module", 180)); resultsGrid.Columns.Add(Column("特征", "Pattern", 420)); resultsGrid.MouseDoubleClick += async (sender, args) => { var match = resultsGrid.SelectedItem as AobMatch; if (match != null && addMatch != null) await addMatch(match); };
            Grid.SetRow(resultsGrid, 1); root.Children.Add(Panel("匹配结果（双击加入地址列表）", resultsGrid)); Content = root;
        }

        private async Task ScanAsync()
        {
            try
            {
                AobPattern pattern = AobPattern.Parse(patternBox.Text);
                ModuleInfo module = moduleCombo.SelectedIndex <= 0 ? null : modules[moduleCombo.SelectedIndex - 1];
                MemoryRegion[] regions = await bridge.ListRegionsAsync(process.pid, true, false);
                cancellation = new CancellationTokenSource(); status.Text = "扫描中...";
                resultsGrid.ItemsSource = await scanner.ScanAsync(process, regions, pattern, module, new Progress<int>(value => status.Text = "扫描中 " + value + "%"), cancellation.Token);
                status.Text = "完成：" + ((ICollection<AobMatch>)resultsGrid.ItemsSource).Count + " 个匹配";
            }
            catch (OperationCanceledException) { status.Text = "已取消"; }
            catch (Exception error) { status.Text = "失败：" + error.Message; }
            finally { cancellation = null; }
        }

        private void CancelScan() { if (cancellation != null) cancellation.Cancel(); }
    }

    internal sealed class PointerScanWindow : ToolWindow
    {
        private readonly PointerScanner scanner;
        private readonly ProcessInfo process;
        private readonly ModuleInfo[] modules;
        private readonly NativeBridgeClient bridge;
        private readonly Func<PointerPath, Task> addPath;
        private TextBox targetBox;
        private TextBox depthBox;
        private TextBox offsetBox;
        private TextBox alignmentBox;
        private DataGrid resultsGrid;
        private TextBlock status;
        private CancellationTokenSource cancellation;

        public PointerScanWindow(NativeBridgeClient bridge, ProcessInfo process, ModuleInfo[] modules, Func<PointerPath, Task> addPath, string initialTarget = "") : base("Pointer Scan", 920, 620)
        {
            this.bridge = bridge; this.process = process; this.modules = modules ?? new ModuleInfo[0]; this.scanner = new PointerScanner(bridge); this.addPath = addPath;
            var root = new Grid { Margin = new Thickness(8) }; root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var controls = new WrapPanel { Margin = new Thickness(0, 0, 0, 7) };
            targetBox = Input("目标地址，例如 0x12345678"); targetBox.Text = initialTarget ?? ""; targetBox.Width = 190; controls.Children.Add(targetBox);
            depthBox = Input("深度"); depthBox.Text = "3"; depthBox.Width = 55; depthBox.Margin = new Thickness(6, 0, 0, 0); controls.Children.Add(depthBox);
            offsetBox = Input("最大偏移"); offsetBox.Text = "0x1000"; offsetBox.Width = 90; offsetBox.Margin = new Thickness(6, 0, 0, 0); controls.Children.Add(offsetBox);
            alignmentBox = Input("对齐"); alignmentBox.Text = "8"; alignmentBox.Width = 55; alignmentBox.Margin = new Thickness(6, 0, 0, 0); controls.Children.Add(alignmentBox);
            controls.Children.Add(Button("开始扫描", async () => await ScanAsync(), true)); controls.Children.Add(Button("取消", CancelScan));
            status = new TextBlock { Text = "从目标地址反向寻找模块指针链", Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) }; controls.Children.Add(status);
            root.Children.Add(controls);
            resultsGrid = CreateGrid(); resultsGrid.Columns.Add(Column("指针路径", "Display", 520)); resultsGrid.Columns.Add(Column("目标地址", "TargetAddress", 170)); resultsGrid.MouseDoubleClick += async (sender, args) => { var path = resultsGrid.SelectedItem as PointerPath; if (path != null && addPath != null) await addPath(path); };
            Grid.SetRow(resultsGrid, 1); root.Children.Add(Panel("结果（双击加入地址列表）", resultsGrid)); Content = root;
        }

        private async Task ScanAsync()
        {
            try
            {
                ulong target = PointerParser.ParseHex(targetBox.Text); int depth = Int32.Parse(depthBox.Text); ulong maxOffset = PointerParser.ParseHex(offsetBox.Text); int alignment = Int32.Parse(alignmentBox.Text);
                MemoryRegion[] regions = await bridge.ListRegionsAsync(process.pid, true, true);
                cancellation = new CancellationTokenSource(); status.Text = "扫描中...";
                resultsGrid.ItemsSource = await scanner.ScanAsync(process, modules, regions, target, depth, maxOffset, alignment, new Progress<int>(value => status.Text = "扫描中 " + value + "%"), cancellation.Token);
                status.Text = "完成：" + ((ICollection<PointerPath>)resultsGrid.ItemsSource).Count + " 条路径";
            }
            catch (OperationCanceledException) { status.Text = "已取消"; }
            catch (Exception error) { status.Text = "失败：" + error.Message; }
            finally { cancellation = null; }
        }

        private void CancelScan() { if (cancellation != null) cancellation.Cancel(); }
    }

    internal sealed class SaveEditorWindow : ToolWindow
    {
        private readonly SaveEditorService service = new SaveEditorService();
        private readonly string backupDirectory;
        private SaveDocument document;
        private TextBox pathBox;
        private TextBox editor;
        private DataGrid diffGrid;
        private TextBlock status;

        public SaveEditorWindow(string backupDirectory) : base("存档编辑器", 1100, 720)
        {
            this.backupDirectory = backupDirectory;
            var root = new Grid { Margin = new Thickness(8) }; root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(170) });
            var controls = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
            pathBox = Input(); pathBox.Width = 560; pathBox.IsReadOnly = true; controls.Children.Add(pathBox); controls.Children.Add(Button("打开", OpenFile)); controls.Children.Add(Button("保存并备份", SaveFile, true)); controls.Children.Add(Button("对比存档", DiffFile));
            status = new TextBlock { Text = "支持 JSON / XML / INI / CFG / TXT / Binary", Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) }; controls.Children.Add(status); root.Children.Add(controls);
            editor = new TextBox { AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Background = InputBrush, Foreground = TextBrush, BorderBrush = LineBrush, FontFamily = new FontFamily("Consolas"), FontSize = 12, Padding = new Thickness(8) }; root.Children.Add(Panel("文件内容（Binary 使用十六进制字节）", editor)); Grid.SetRow(root.Children[root.Children.Count - 1], 1);
            diffGrid = CreateGrid(); diffGrid.Columns.Add(Column("偏移", "Offset", 120)); diffGrid.Columns.Add(Column("旧值", "OldValue", 120)); diffGrid.Columns.Add(Column("新值", "NewValue", 120)); var diffPanel = Panel("Save Diff", diffGrid); System.Windows.Controls.Grid.SetRow(diffPanel, 2); root.Children.Add(diffPanel); Content = root;
        }

        private void OpenFile()
        {
            var dialog = new OpenFileDialog { Filter = "存档文件|*.json;*.xml;*.ini;*.cfg;*.txt;*.dat;*.sav|所有文件|*.*" };
            if (dialog.ShowDialog(this) != true) return;
            try { document = service.Open(dialog.FileName); pathBox.Text = document.Path; editor.Text = document.Text; status.Text = document.IsBinary ? "Binary 存档" : "文本存档"; }
            catch (Exception error) { status.Text = "打开失败：" + error.Message; }
        }

        private void SaveFile()
        {
            try { string backup = service.Save(document, editor.Text, backupDirectory); status.Text = "已保存，备份：" + backup; }
            catch (Exception error) { status.Text = "保存失败：" + error.Message; }
        }

        private void DiffFile()
        {
            if (document == null) { status.Text = "请先打开一个存档。"; return; }
            var dialog = new OpenFileDialog { Filter = "存档文件|*.*", Title = "选择另一个存档" };
            if (dialog.ShowDialog(this) != true) return;
            try { diffGrid.ItemsSource = service.Diff(document.Path, dialog.FileName); status.Text = "差异：" + ((ICollection<SaveDiff>)diffGrid.ItemsSource).Count + " 项"; }
            catch (Exception error) { status.Text = "对比失败：" + error.Message; }
        }
    }

    internal sealed class MemoryViewerWindow : ToolWindow
    {
        private readonly NativeBridgeClient bridge;
        private readonly ProcessInfo process;
        private TextBox addressBox;
        private TextBox sizeBox;
        private TextBox bytesBox;
        private TextBlock status;

        public MemoryViewerWindow(NativeBridgeClient bridge, ProcessInfo process) : base("Memory Viewer / Dissect", 900, 600)
        {
            this.bridge = bridge; this.process = process;
            var root = new Grid { Margin = new Thickness(8) }; root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var controls = new WrapPanel { Margin = new Thickness(0, 0, 0, 7) }; addressBox = Input("地址，例如 0x12345678"); addressBox.Width = 190; controls.Children.Add(addressBox); sizeBox = Input("字节数"); sizeBox.Text = "128"; sizeBox.Width = 70; sizeBox.Margin = new Thickness(6, 0, 0, 0); controls.Children.Add(sizeBox); controls.Children.Add(Button("读取", async () => await ReadAsync(), true)); controls.Children.Add(Button("写入", async () => await WriteAsync())); status = new TextBlock { Text = "十六进制视图 · ASCII", Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) }; controls.Children.Add(status); root.Children.Add(controls);
            bytesBox = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Background = InputBrush, Foreground = TextBrush, BorderBrush = LineBrush, FontFamily = new FontFamily("Consolas"), FontSize = 12, Padding = new Thickness(8) }; Grid.SetRow(bytesBox, 1); root.Children.Add(Panel("Bytes（可编辑后写入）", bytesBox)); Content = root;
        }

        private async Task ReadAsync()
        {
            try { ulong address = PointerParser.ParseHex(addressBox.Text); int size = Int32.Parse(sizeBox.Text); byte[] bytes = await bridge.ReadAsync(process.pid, address, size); bytesBox.Text = SaveEditorService.FormatBytes(bytes); status.Text = "已读取 " + bytes.Length + " 字节"; }
            catch (Exception error) { status.Text = "读取失败：" + error.Message; }
        }

        private async Task WriteAsync()
        {
            try { ulong address = PointerParser.ParseHex(addressBox.Text); byte[] bytes = SaveEditorService.ParseBytes(bytesBox.Text); await bridge.WriteAsync(process.pid, address, bytes); status.Text = "已写入 " + bytes.Length + " 字节"; }
            catch (Exception error) { status.Text = "写入失败：" + error.Message; }
        }
    }

    internal sealed class TrainerWindow : ToolWindow
    {
        private readonly IList<AddressEntry> entries;
        private readonly Func<AddressEntry, Task> write;
        private readonly Action<AddressEntry> toggleFreeze;
        private DataGrid grid;

        public TrainerWindow(IList<AddressEntry> entries, Func<AddressEntry, Task> write, Action<AddressEntry> toggleFreeze) : base("Trainer Mode", 760, 520)
        {
            this.entries = entries; this.write = write; this.toggleFreeze = toggleFreeze;
            var root = new Grid { Margin = new Thickness(8) }; root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 7) }; buttons.Children.Add(Button("写入选中功能", async () => { var entry = grid.SelectedItem as AddressEntry; if (entry != null) await write(entry); }, true)); buttons.Children.Add(Button("冻结/取消冻结", () => { var entry = grid.SelectedItem as AddressEntry; if (entry != null) toggleFreeze(entry); })); root.Children.Add(buttons);
            grid = CreateGrid(); grid.Columns.Add(Column("描述", "Description", 180)); grid.Columns.Add(Column("当前值", "CurrentValue", 120)); grid.Columns.Add(Column("新值", "NewValue", 120)); grid.Columns.Add(Column("状态", "Frozen", 80)); grid.ItemsSource = entries; System.Windows.Controls.Grid.SetRow(grid, 1); root.Children.Add(Panel("Trainer 功能", grid)); Content = root;
        }
    }
}
