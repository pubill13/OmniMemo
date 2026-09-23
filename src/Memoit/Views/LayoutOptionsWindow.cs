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
    private readonly TextBox x = new(), y = new(), gap = new(), columns = new();
    private readonly CheckBox auto = new() { Content = "자동 정렬", Margin = new Thickness(0, 10, 0, 5) };
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

    public LayoutOptionsWindow(LayoutSettings settings, IReadOnlyList<MonitorDescriptor> monitors)
    {
        Title = "OmniMemo · 정렬 옵션"; Width = 340; Height = 570; MinWidth = 340; MinHeight = 450;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/OmniMemo;component/Assets/OmniMemo.ico"));
        foreach (var (control, name) in new (DependencyObject, string)[] { (x, "시작 X"), (y, "시작 Y"), (gap, "간격"), (columns, "열 수"), (shape, "배치 형태"), (sort, "정렬 기준"), (monitorBox, "모니터") })
            System.Windows.Automation.AutomationProperties.SetName(control, name);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (settings.PanelLeft is double left && settings.PanelTop is double top)
        { WindowStartupLocation = WindowStartupLocation.Manual; Left = left; Top = top; }
        ShowInTaskbar = false; FontFamily = new FontFamily("Malgun Gothic"); Background = Brushes.WhiteSmoke;
        var root = new DockPanel { Margin = new Thickness(16) };
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom);
        footer.Children.Add(status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(Button("적용", () => Dispatch(false))); actions.Children.Add(Button("지금 정렬", () => Dispatch(true))); footer.Children.Add(actions); root.Children.Add(footer);
        var tabs = new TabControl(); root.Children.Add(tabs);
        var layout = new StackPanel { Margin = new Thickness(10) };
        layout.Children.Add(Label("모니터")); layout.Children.Add(monitorBox);
        layout.Children.Add(Label("배치 형태")); layout.Children.Add(shape); layout.Children.Add(Label("정렬 기준")); layout.Children.Add(sort);
        layout.Children.Add(Label("시작 위치 X / Y (DIP)"));
        var coords = new UniformGridShim(); coords.Children.Add(x); coords.Children.Add(y); layout.Children.Add(coords);
        layout.Children.Add(Button("화면에서 위치 선택…", PickAnchor));
        layout.Children.Add(Label("메모 사이 간격 (0–24 DIP)")); layout.Children.Add(gap);
        layout.Children.Add(Label("열 수 (0 = 자동, 최대 30)")); layout.Children.Add(columns); layout.Children.Add(auto);
        tabs.Items.Add(new TabItem { Header = "배치", Content = new ScrollViewer { Content = layout, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        var hotkeys = new DockPanel { Margin = new Thickness(8) };
        var reset = Button("모든 단축키 기본값", ResetAll); DockPanel.SetDock(reset, Dock.Bottom); hotkeys.Children.Add(reset);
        hotkeys.Children.Add(new ScrollViewer { Content = hotkeyRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        tabs.Items.Add(new TabItem { Header = "단축키", Content = hotkeys }); Content = root;
        monitorBox.SelectionChanged += (_, _) => SwitchMonitor();
        Closing += (_, e) => { if (!AllowClose) { e.Cancel = true; HidePanel(); } };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; if (capturing) CancelCapture(); else HidePanel(); } };
        RefreshSettings(settings, monitors);
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
        monitors = available; selectedId = null;
        monitorBox.ItemsSource = monitors;
        monitorBox.SelectedItem = monitors.FirstOrDefault(m => m.Id == settings.SelectedMonitor) ?? monitors.FirstOrDefault();
        auto.IsChecked = settings.AutoArrange;
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
    }
    private bool StoreMonitor()
    {
        if (selectedId is null) return true;
        if (!double.TryParse(x.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var px) || !double.TryParse(y.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var py)
            || !double.TryParse(gap.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var spacing) || !int.TryParse(columns.Text, out var count)
            || !double.IsFinite(px) || !double.IsFinite(py) || !double.IsFinite(spacing) || px < 0 || py < 0 || spacing < 0 || spacing > 24 || count < 0 || count > 30)
        { ShowError("위치와 간격은 0 이상의 숫자, 열 수는 0 이상의 정수를 입력하세요."); return false; }
        draft.Monitors[selectedId] = new MonitorLayout { X = px, Y = py, Gap = spacing, Columns = count, Shape = (LayoutShape)shape.SelectedIndex, Sort = (LayoutSort)sort.SelectedIndex };
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
            var candidate = draft with { AutoArrange = auto.IsChecked == true, SelectedMonitor = selectedId, PanelLeft = Left, PanelTop = Top, Monitors = new(draft.Monitors), Hotkeys = new(draft.Hotkeys) };
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
