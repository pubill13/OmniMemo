using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Memoit.Services;

namespace Memoit.Views;

public sealed class LayoutOptionsWindow : Window
{
    private LayoutSettings draft = new();
    private IReadOnlyList<MonitorDescriptor> monitors = [];
    private readonly ComboBox monitorBox = new() { DisplayMemberPath = "Name", Margin = new Thickness(0, 4, 0, 10) };
    private readonly ComboBox shape = new() { ItemsSource = new[] { "격자", "가로 한 줄", "세로 한 줄" } };
    private readonly ComboBox sort = new() { ItemsSource = new[] { "생성순", "색상순", "제목순" } };
    private readonly ComboBox corner = new() { ItemsSource = Enum.GetValues<ExpandedCorner>() };
    private readonly Slider opacity = new() { Minimum = 30, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true };
    private readonly TabControl tabs = new();
    private readonly CheckBox autoStart = new() { Content = "Windows 로그인 시 OmniMemo 실행" };
    private readonly TextBox x = new(), y = new(), gap = new(), columns = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) };
    private readonly StackPanel hotkeyRows = new();
    private readonly Dictionary<string, TextBox> recorders = [];
    private string? selectedId;
    private bool refreshing;
    private bool capturing;
    private string? captureId;
    private string captureOriginal = "";
    public bool AllowClose { get; set; }
    public string? SelectedMonitorId => selectedId;
    public event Action<LayoutSettings>? ApplyRequested;
    public event Action<LayoutSettings>? ArrangeRequested;
    public event Action<bool>? CaptureChanged;
    public event Action? Hidden;
    public event Action<bool>? AutoStartChanged;
    public event Action? BackupRequested;
    public event Action? RestoreRequested;

    public LayoutOptionsWindow(LayoutSettings settings, IReadOnlyList<MonitorDescriptor> monitors, string dataPath = "", bool autoStart = false)
    {
        Title = "OmniMemo · 설정"; Width = 340; Height = 570; MinWidth = 340; MinHeight = 450;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/OmniMemo;component/Assets/OmniMemo.ico"));
        foreach (var (control, name) in new (DependencyObject, string)[] { (x, "시작 X"), (y, "시작 Y"), (gap, "간격"), (columns, "열 수"), (shape, "배치 형태"), (sort, "정렬 기준"), (monitorBox, "모니터"), (corner, "펼친 메모 시작 방향") })
            System.Windows.Automation.AutomationProperties.SetName(control, name);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (settings.PanelLeft is double left && settings.PanelTop is double top)
        { WindowStartupLocation = WindowStartupLocation.Manual; Left = left; Top = top; }
        ShowInTaskbar = false; FontFamily = new FontFamily("Malgun Gothic"); Background = Brushes.WhiteSmoke;
        var root = new DockPanel { Margin = new Thickness(16) };
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom);
        footer.Children.Add(status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(Button("적용", () => Dispatch(false))); actions.Children.Add(Button("접어서 정돈", () => Dispatch(true))); footer.Children.Add(actions); root.Children.Add(footer);
        root.Children.Add(tabs);
        var layout = new StackPanel { Margin = new Thickness(10) };
        layout.Children.Add(Label("모니터")); layout.Children.Add(monitorBox);
        layout.Children.Add(Label("접힌 메모"));
        layout.Children.Add(Label("배치 형태")); layout.Children.Add(shape); layout.Children.Add(Label("정렬 기준")); layout.Children.Add(sort);
        layout.Children.Add(Label("타일 시작점 X / Y (DIP)"));
        var coords = new UniformGridShim(); coords.Children.Add(x); coords.Children.Add(y); layout.Children.Add(coords);
        layout.Children.Add(Button("화면에서 위치 선택…", PickAnchor));
        layout.Children.Add(Label("메모 사이 간격 (0–24 DIP)")); layout.Children.Add(gap);
        layout.Children.Add(Label("열 수 (0 = 자동, 최대 30)")); layout.Children.Add(columns);
        layout.Children.Add(Label("펼친 메모 시작 방향")); layout.Children.Add(corner);
        corner.ItemTemplate = CornerTemplate();
        layout.Children.Add(new TextBlock { Text = "접어서 정돈하면 타일 시작점에 모입니다. 모두 펼치면 선택한 모서리에서 같은 순서로 배치합니다.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Foreground = Brushes.DimGray });
        tabs.Items.Add(new TabItem { Header = "배치", Content = new ScrollViewer { Content = layout, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        var hotkeys = new DockPanel { Margin = new Thickness(8) };
        var reset = Button("모든 단축키 기본값", ResetAll); DockPanel.SetDock(reset, Dock.Bottom); hotkeys.Children.Add(reset);
        hotkeys.Children.Add(new ScrollViewer { Content = hotkeyRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        tabs.Items.Add(new TabItem { Header = "단축키", Content = hotkeys });
        tabs.Items.Add(new TabItem { Header = "일반·백업", Content = BuildGeneral(dataPath) }); Content = root;
        SetAutoStart(autoStart);
        this.autoStart.Checked += (_, _) => { if (!refreshing) AutoStartChanged?.Invoke(true); };
        this.autoStart.Unchecked += (_, _) => { if (!refreshing) AutoStartChanged?.Invoke(false); };
        monitorBox.SelectionChanged += (_, _) => SwitchMonitor();
        Closing += (_, e) => { if (!AllowClose) { e.Cancel = true; HidePanel(); } };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; if (capturing) CancelCapture(); else HidePanel(); } };
        RefreshSettings(settings, monitors);
    }

    public void SelectTab(int index) => tabs.SelectedIndex = Math.Clamp(index, 0, 2);
    public void SetAutoStart(bool value)
    {
        bool previous = refreshing; refreshing = true; autoStart.IsChecked = value; refreshing = previous;
    }
    private UIElement BuildGeneral(string dataPath)
    {
        var content = new StackPanel { Margin = new Thickness(10) };
        content.Children.Add(autoStart);
        content.Children.Add(Label("미니 패널 불투명도 (30–100%)"));
        content.Children.Add(opacity);
        System.Windows.Automation.AutomationProperties.SetName(opacity, "패널 불투명도");
        content.Children.Add(Label("백업과 복원"));
        content.Children.Add(new TextBlock { Text = "매일 첫 저장 후 자동 백업하며 최근 7개를 보관합니다. 휴지통 메모도 포함됩니다.", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(Button("백업 파일 저장…", () => BackupRequested?.Invoke()));
        content.Children.Add(Button("백업에서 복원…", () => RestoreRequested?.Invoke()));
        content.Children.Add(new TextBlock { Text = "복원하면 전체 메모가 백업 내용으로 바뀝니다. 현재 데이터는 교체 전에 안전 백업합니다.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 8, 0, 8) });
        content.Children.Add(Label("데이터 저장 위치"));
        var path = new TextBox { Text = dataPath, IsReadOnly = true, TextWrapping = TextWrapping.Wrap };
        System.Windows.Automation.AutomationProperties.SetName(path, "데이터 저장 위치");
        content.Children.Add(path);
        return new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    private static DataTemplate CornerTemplate()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding { Converter = new CornerLabelConverter() });
        return new DataTemplate { VisualTree = text };
    }
    private sealed class CornerLabelConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
        {
            ExpandedCorner.TopLeft => "왼쪽 위", ExpandedCorner.TopRight => "오른쪽 위",
            ExpandedCorner.BottomLeft => "왼쪽 아래", ExpandedCorner.BottomRight => "오른쪽 아래", _ => "오른쪽 위"
        };
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    private static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 8, 0, 4) };
    private static Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(7, 5, 7, 5), Margin = new Thickness(2, 4, 2, 4) };
        button.Click += (_, _) => action(); return button;
    }
    public void RefreshSettings(LayoutSettings settings, IReadOnlyList<MonitorDescriptor> available)
    {
        EndCapture();
        refreshing = true;
        draft = settings with { Monitors = new(settings.Monitors), Hotkeys = new(settings.Hotkeys) };
        opacity.Value = settings.OverlayOpacity * 100;
        monitors = available; selectedId = null;
        monitorBox.ItemsSource = monitors;
        monitorBox.SelectedItem = monitors.FirstOrDefault(m => m.Id == settings.SelectedMonitor) ?? monitors.FirstOrDefault();
        refreshing = false; LoadMonitor(); BuildRecorders(); status.Foreground = Brushes.DimGray; status.Text = "변경한 설정은 적용 후 저장됩니다.";
    }
    private void SwitchMonitor()
    {
        if (refreshing) return;
        if (!StoreMonitor()) { refreshing = true; monitorBox.SelectedItem = monitors.FirstOrDefault(m => m.Id == selectedId); refreshing = false; return; }
        LoadMonitor();
    }
    private void LoadMonitor()
    {
        selectedId = (monitorBox.SelectedItem as MonitorDescriptor)?.Id;
        var value = selectedId is not null ? draft.GetMonitor(selectedId) : new MonitorLayout();
        x.Text = value.X.ToString(CultureInfo.InvariantCulture); y.Text = value.Y.ToString(CultureInfo.InvariantCulture);
        gap.Text = value.Gap.ToString(CultureInfo.InvariantCulture); columns.Text = value.Columns.ToString(CultureInfo.InvariantCulture);
        shape.SelectedIndex = (int)value.Shape; sort.SelectedIndex = (int)value.Sort;
        corner.SelectedItem = value.ExpandedCorner;
    }
    private bool StoreMonitor()
    {
        if (selectedId is null) return true;
        if (!double.TryParse(x.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var px) || !double.TryParse(y.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var py)
            || !double.TryParse(gap.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var spacing) || !int.TryParse(columns.Text, out var count)
            || !double.IsFinite(px) || !double.IsFinite(py) || !double.IsFinite(spacing) || px < 0 || py < 0 || spacing < 0 || spacing > 24 || count < 0 || count > 30)
        { ShowError("위치와 간격은 0 이상의 숫자, 열 수는 0 이상의 정수를 입력하세요."); return false; }
        draft.Monitors[selectedId] = new MonitorLayout { X = px, Y = py, Gap = spacing, Columns = count, Shape = (LayoutShape)shape.SelectedIndex, Sort = (LayoutSort)sort.SelectedIndex, ExpandedCorner = corner.SelectedItem is ExpandedCorner direction ? direction : ExpandedCorner.TopRight };
        return true;
    }
    private void BuildRecorders()
    {
        hotkeyRows.Children.Clear(); recorders.Clear();
        foreach (var (id, label) in HotkeyDefaults.Labels)
        {
            hotkeyRows.Children.Add(Label(label));
            var row = new DockPanel();
            var box = new TextBox { IsReadOnly = true, Text = draft.Hotkeys.GetValueOrDefault(id, ""), Padding = new Thickness(3), ToolTip = "선택한 뒤 키 조합을 누르세요. Esc는 입력 취소" };
            System.Windows.Automation.AutomationProperties.SetName(box, label + " 단축키");
            var clear = Button("해제", () => SetGesture(id, "")); DockPanel.SetDock(clear, Dock.Right); row.Children.Add(clear);
            var reset = Button("기본", () => SetGesture(id, HotkeyDefaults.Create()[id])); DockPanel.SetDock(reset, Dock.Right); row.Children.Add(reset);
            row.Children.Add(box); recorders[id] = box; hotkeyRows.Children.Add(row);
            box.GotKeyboardFocus += (_, _) => { captureId = id; captureOriginal = draft.Hotkeys.GetValueOrDefault(id, ""); capturing = true; CaptureChanged?.Invoke(true); };
            box.LostKeyboardFocus += (_, _) => EndCapture(false);
            box.PreviewKeyDown += (_, e) =>
            {
                e.Handled = true; var key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key == Key.Escape) { CancelCapture(); return; }
                if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
                var modifiers = Keyboard.Modifiers;
                var value = (modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl+" : "") + (modifiers.HasFlag(ModifierKeys.Alt) ? "Alt+" : "") + (modifiers.HasFlag(ModifierKeys.Shift) ? "Shift+" : "") + (modifiers.HasFlag(ModifierKeys.Windows) ? "Win+" : "") + key;
                try { SetGesture(id, HotkeyService.Normalize(value)); } catch (Exception ex) { ShowError(ex.Message); }
            };
        }
    }
    private void SetGesture(string id, string value) { draft.Hotkeys[id] = value; recorders[id].Text = value; }
    private void ResetAll() { EndCapture(); draft = draft with { Hotkeys = HotkeyDefaults.Create() }; BuildRecorders(); }
    private void EndCapture(bool clearFocus = true)
    {
        if (!capturing) return; capturing = false;
        captureId = null;
        if (clearFocus) Keyboard.ClearFocus(); CaptureChanged?.Invoke(false);
    }
    private void CancelCapture()
    {
        if (captureId is string id) SetGesture(id, captureOriginal);
        EndCapture();
    }
    private void Dispatch(bool arrange)
    {
        EndCapture(); if (!StoreMonitor()) return;
        try
        {
            HotkeyService.Validate(draft.Hotkeys);
            var candidate = draft with { AutoArrange = false, OverlayOpacity = opacity.Value / 100, SelectedMonitor = selectedId, PanelLeft = Left, PanelTop = Top, Monitors = new(draft.Monitors), Hotkeys = new(draft.Hotkeys) };
            candidate.Validate();
            if (arrange) ArrangeRequested?.Invoke(candidate); else ApplyRequested?.Invoke(candidate);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    private void PickAnchor()
    {
        if (monitorBox.SelectedItem is not MonitorDescriptor monitor) return;
        EndCapture(); Hide(); CaptureChanged?.Invoke(true);
        try { var picker = new AnchorPickerWindow(monitor); if (picker.ShowDialog() == true && picker.SelectedPoint is Point point) { x.Text = point.X.ToString("0.##", CultureInfo.InvariantCulture); y.Text = point.Y.ToString("0.##", CultureInfo.InvariantCulture); } }
        finally { CaptureChanged?.Invoke(false); Show(); Activate(); }
    }
    private void HidePanel() { EndCapture(); Hide(); Hidden?.Invoke(); }
    public void ShowError(string message) { status.Foreground = Brushes.Firebrick; status.Text = message; }
    public void ShowSuccess(string message = "설정을 저장했습니다.") { status.Foreground = Brushes.DarkGreen; status.Text = message; }
    private sealed class UniformGridShim : System.Windows.Controls.Primitives.UniformGrid { public UniformGridShim() { Columns = 2; } }
}
