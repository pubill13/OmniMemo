using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Memoit.Services;
using Memoit.Models;

namespace Memoit.Views;

public enum SettingsTab { Layout, Hotkeys, General, Backup }

public sealed class LayoutOptionsWindow : Window
{
    private LayoutSettings draft = new();
    private IReadOnlyList<MonitorDescriptor> monitors = [];
    private readonly ComboBox monitorBox = new() { DisplayMemberPath = "Name", Margin = new Thickness(0, 4, 0, 10) };
    private readonly ComboBox shape = new() { ItemsSource = new[] { "여러 줄로 배치", "가로 한 줄", "세로 한 줄" } };
    private readonly ComboBox sort = new() { ItemsSource = new[] { "생성순", "색상순", "제목순" } };
    private readonly ComboBox corner = new() { ItemsSource = Enum.GetValues<ExpandedCorner>() };
    private Window? previewWindow;
    private readonly ComboBox columnMode = new() { ItemsSource = new[] { "자동", "직접 지정" }, SelectedIndex = 0 };
    private readonly TextBlock originSummary = new(), shapeHint = new(), colorHint = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Dictionary<ExpandedCorner, RadioButton> cornerButtons = [];
    private readonly TabControl tabs = new();
    private readonly CheckBox autoStart = new() { Content = "Windows 로그인 시 OmniMemo 실행" };
    private readonly TextBox x = new(), y = new(), gap = new(), columns = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) };
    private readonly StackPanel hotkeyRows = new();
    private readonly Dictionary<string, TextBox> recorders = [];
    private readonly StackPanel columnFields = new();
    private readonly ComboBox previewMode = new() { ItemsSource = new[] { "접힌 모습", "펼친 모습" }, SelectedIndex = 0 };
    private readonly Canvas preview = new() { Width = 250, Height = 140, Background = Brushes.White, ClipToBounds = true };
    private readonly TextBlock previewStatus = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray };
    private readonly Func<string, IReadOnlyList<Note>>? previewNotes;
    private string? selectedId;
    private bool refreshing;
    private bool capturing;
    private string? captureId;
    private string captureOriginal = "";
    public bool AllowClose { get; set; }
    public string? SelectedMonitorId => selectedId;
    public event Action<LayoutSettings, bool>? ApplyRequested;
    public event Action<bool>? CaptureChanged;
    public event Action? Hidden;
    public event Action? BackupRequested;
    public event Action? RestoreRequested;

    public LayoutOptionsWindow(LayoutSettings settings, IReadOnlyList<MonitorDescriptor> monitors, string dataPath = "", bool autoStart = false,
        Func<string, IReadOnlyList<Note>>? previewNotes = null)
    {
        this.previewNotes = previewNotes;
        Title = "OmniMemo · 설정"; Width = 560; Height = 620; MinWidth = 300; MinHeight = 250;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/OmniMemo;component/Assets/OmniMemo.ico"));
        foreach (var (control, name) in new (DependencyObject, string)[] { (x, "시작 X"), (y, "시작 Y"), (gap, "간격"), (columns, "한 줄에 놓을 메모 수"), (shape, "배치 형태"), (sort, "정렬 기준"), (monitorBox, "모니터"), (corner, "펼친 메모 시작 방향") })
            System.Windows.Automation.AutomationProperties.SetName(control, name);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (settings.PanelLeft is double left && settings.PanelTop is double top)
        { WindowStartupLocation = WindowStartupLocation.Manual; Left = left; Top = top; }
        ShowInTaskbar = false; FontFamily = new FontFamily("Malgun Gothic"); FontSize = 13; Background = Brushes.WhiteSmoke;
        Resources.Add(typeof(Button), CreateButtonStyle());
        foreach (var type in new[] { typeof(TextBox), typeof(ComboBox) })
        { var style = new Style(type); style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7, 5, 7, 5))); style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.LightSlateGray)); Resources.Add(type, style); }
        var root = new DockPanel { Margin = new Thickness(16) };
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom);
        footer.Children.Add(status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(Button("적용", Dispatch)); footer.Children.Add(actions); root.Children.Add(footer);
        root.Children.Add(tabs);
        var layout = new StackPanel { Margin = new Thickness(10), Background = Brushes.WhiteSmoke };
        var heading = new DockPanel(); var previewButton = Button("미리보기", OpenPreview); DockPanel.SetDock(previewButton, Dock.Right); heading.Children.Add(previewButton); heading.Children.Add(Label("배치할 화면")); layout.Children.Add(heading); layout.Children.Add(monitorBox);
        layout.Children.Add(Section("접었을 때"));
        layout.Children.Add(Label("배치 형태")); layout.Children.Add(shape); layout.Children.Add(shapeHint);
        var originRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) }; var pickButton = Button("화면에서 지정", PickAnchor); DockPanel.SetDock(pickButton, Dock.Right); originRow.Children.Add(pickButton); originSummary.VerticalAlignment = VerticalAlignment.Center; originRow.Children.Add(originSummary); layout.Children.Add(originRow);
        var coords = new UniformGridShim(); var cx = new StackPanel(); cx.Children.Add(Label("시작 X (DIP)")); cx.Children.Add(x); var cy = new StackPanel(); cy.Children.Add(Label("시작 Y (DIP)")); cy.Children.Add(y); coords.Children.Add(cx); coords.Children.Add(cy);
        layout.Children.Add(new Expander { Header = "세부 조정", Content = coords, Margin = new Thickness(0, 6, 0, 4) });
        columnFields.Children.Add(Label("한 줄에 놓을 메모 수")); var columnRow = new UniformGridShim(); columnMode.Margin = new Thickness(0, 0, 8, 0); columnRow.Children.Add(columnMode); columnRow.Children.Add(columns); columnFields.Children.Add(columnRow); layout.Children.Add(columnFields);
        layout.Children.Add(Section("펼쳤을 때"));
        layout.Children.Add(Label("이 모서리에서 화면 안쪽으로 배치합니다"));
        var directions = new UniformGridShim();
        foreach (var direction in new[] { ExpandedCorner.TopLeft, ExpandedCorner.TopRight, ExpandedCorner.BottomLeft, ExpandedCorner.BottomRight })
        {
            var text = (string)new CornerLabelConverter().Convert(direction, typeof(string), null!, CultureInfo.InvariantCulture);
            var button = new RadioButton { Content = text, GroupName = "Corner", Margin = new Thickness(5), Padding = new Thickness(8) };
            System.Windows.Automation.AutomationProperties.SetName(button, "시작 모서리 " + text);
            button.Checked += (_, _) => corner.SelectedItem = direction; cornerButtons[direction] = button; directions.Children.Add(button);
        }
        layout.Children.Add(directions);
        layout.Children.Add(Section("순서와 간격"));
        var commonFields = new Grid(); commonFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) }); commonFields.ColumnDefinitions.Add(new ColumnDefinition());
        var sortField = new StackPanel { Margin = new Thickness(0, 0, 12, 0) }; sortField.Children.Add(Label("정렬 기준")); sortField.Children.Add(sort); commonFields.Children.Add(sortField);
        var gapField = new StackPanel(); gapField.Children.Add(Label("간격 (0–24 DIP)")); gapField.Children.Add(gap); Grid.SetColumn(gapField, 1); commonFields.Children.Add(gapField); layout.Children.Add(commonFields);
        colorHint.Text = "색이 바뀌면 새 줄에 배치하며, 같은 색 안에서는 먼저 만든 메모부터 놓습니다. 줄 구분은 여러 줄 배치에만 적용됩니다."; colorHint.Foreground = Brushes.DimGray; layout.Children.Add(colorHint);
        corner.ItemTemplate = CornerTemplate();
        layout.Children.Add(new TextBlock { Text = "접어서 정돈하면 타일 시작점에 모입니다. 모두 펼치면 선택한 모서리에서 같은 순서로 배치합니다.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Foreground = Brushes.DimGray });
        GroupSections(layout);
        tabs.Items.Add(new TabItem { Header = "배치", Content = new ScrollViewer { Content = layout, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        var hotkeys = new DockPanel { Margin = new Thickness(8) };
        var reset = Button("모든 단축키 기본값", ResetAll); DockPanel.SetDock(reset, Dock.Bottom); hotkeys.Children.Add(reset);
        hotkeys.Children.Add(new ScrollViewer { Content = hotkeyRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        tabs.Items.Add(new TabItem { Header = "단축키", Content = hotkeys });
        tabs.Items.Add(new TabItem { Header = "일반", Content = BuildGeneral() });
        tabs.Items.Add(new TabItem { Header = "백업", Content = BuildBackup(dataPath) }); Content = root;
        SetAutoStart(autoStart);
        Loaded += (_, _) => FitToWorkArea();
        monitorBox.SelectionChanged += (_, _) => SwitchMonitor();
        foreach (var field in new[] { x, y, gap, columns }) field.TextChanged += (_, _) => RefreshPreview();
        foreach (var field in new[] { shape, sort, corner, columnMode, previewMode }) field.SelectionChanged += (_, _) => RefreshPreview();
        IsVisibleChanged += (_, _) => { if (IsVisible) RefreshPreview(); else ClosePreview(); };
        Closing += (_, e) => { ClosePreview(); if (!AllowClose) { e.Cancel = true; HidePanel(); } };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; if (capturing) CancelCapture(); else HidePanel(); } };
        RefreshSettings(settings, monitors);
    }

    public void SelectTab(SettingsTab tab) => tabs.SelectedIndex = (int)tab;
    public void SetAutoStart(bool value)
    {
        bool previous = refreshing; refreshing = true; autoStart.IsChecked = value; refreshing = previous;
    }
    private UIElement BuildGeneral()
    {
        var content = new StackPanel { Margin = new Thickness(10), Background = Brushes.WhiteSmoke };
        content.Children.Add(Section("시작")); content.Children.Add(autoStart);
        GroupSections(content);
        return new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    private UIElement BuildBackup(string dataPath)
    {
        var content = new StackPanel { Margin = new Thickness(10), Background = Brushes.WhiteSmoke };
        content.Children.Add(Section("백업과 복원"));
        content.Children.Add(new TextBlock { Text = "매일 첫 저장 후 자동 백업하며 최근 7개를 보관합니다. 휴지통 메모도 포함됩니다.", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(Button("백업 파일 저장…", () => BackupRequested?.Invoke()));
        content.Children.Add(Button("백업에서 복원…", () => RestoreRequested?.Invoke()));
        content.Children.Add(new TextBlock { Text = "복원하면 전체 메모가 백업 내용으로 바뀝니다. 현재 데이터는 교체 전에 안전 백업합니다.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 8, 0, 8) });
        content.Children.Add(Section("데이터 저장 위치"));
        var path = new TextBox { Text = dataPath, IsReadOnly = true, TextWrapping = TextWrapping.Wrap };
        System.Windows.Automation.AutomationProperties.SetName(path, "데이터 저장 위치");
        content.Children.Add(path);
        GroupSections(content);
        return new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    private void FitToWorkArea()
    {
        var screen = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle).WorkingArea;
        var scale = VisualTreeHelper.GetDpi(this);
        MinWidth = Math.Min(MinWidth, screen.Width / scale.DpiScaleX); MinHeight = Math.Min(MinHeight, screen.Height / scale.DpiScaleY);
        Width = Math.Min(Width, screen.Width / scale.DpiScaleX); Height = Math.Min(Height, screen.Height / scale.DpiScaleY);
        Left = Math.Clamp(Left, screen.Left / scale.DpiScaleX, screen.Right / scale.DpiScaleX - Width);
        Top = Math.Clamp(Top, screen.Top / scale.DpiScaleY, screen.Bottom / scale.DpiScaleY - Height);
    }
    private void OpenPreview()
    {
        if (previewWindow is not null) { previewWindow.Activate(); return; }
        var content = new StackPanel { Margin = new Thickness(18) }; content.Children.Add(previewMode);
        var frame = new Border { Child = preview, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Margin = new Thickness(0, 12, 0, 12), HorizontalAlignment = HorizontalAlignment.Center }; content.Children.Add(frame); content.Children.Add(previewStatus);
        var window = new Window { Title = "OmniMemo · 배치 미리보기", Width = 440, Height = 360, MinWidth = 300, MinHeight = 250, Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, ShowInTaskbar = false, FontFamily = FontFamily, Background = Brushes.WhiteSmoke, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        if (IsVisible) window.Owner = this;
        window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; window.Close(); } };
        window.Closed += (_, _) => { content.Children.Clear(); frame.Child = null; previewWindow = null; };
        previewWindow = window; RefreshPreview(); window.Show();
    }
    private void ClosePreview() => previewWindow?.Close();

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

    private static void GroupSections(StackPanel parent)
    {
        var children = parent.Children.Cast<UIElement>().ToArray(); parent.Children.Clear(); StackPanel? group = null;
        foreach (var child in children)
        {
            if (child is TextBlock { Tag: "Section" })
            {
                group = new StackPanel();
                parent.Children.Add(new Border { Child = group, Background = Brushes.White, CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 8, 0, 2) });
            }
            if (group is null) parent.Children.Add(child); else group.Children.Add(child);
        }
    }
    private static Style CreateButtonStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(235, 239, 244))));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(38, 47, 60))));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(206, 215, 225))));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        var border = new FrameworkElementFactory(typeof(Border)); border.Name = "Frame";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetBinding(FrameworkElement.MarginProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) }); border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        foreach (var (property, color) in new[] { (UIElement.IsMouseOverProperty, Color.FromRgb(221, 230, 240)), (System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Color.FromRgb(202, 216, 232)) })
        { var trigger = new Trigger { Property = property, Value = true }; trigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(color), "Frame")); template.Triggers.Add(trigger); }
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true }; focus.Setters.Add(new Setter(Border.BorderBrushProperty, Brushes.SteelBlue, "Frame")); template.Triggers.Add(focus);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .5, "Frame")); template.Triggers.Add(disabled);
        style.Setters.Add(new Setter(Control.TemplateProperty, template)); return style;
    }

    private static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 8, 0, 4) };
    private static TextBlock Section(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 6, 0, 6), Tag = "Section" };
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
        refreshing = true;
        selectedId = (monitorBox.SelectedItem as MonitorDescriptor)?.Id;
        var value = selectedId is not null ? draft.GetMonitor(selectedId) : new MonitorLayout();
        x.Text = value.X.ToString(CultureInfo.InvariantCulture); y.Text = value.Y.ToString(CultureInfo.InvariantCulture);
        gap.Text = value.Gap.ToString(CultureInfo.InvariantCulture); columns.Text = value.Columns.ToString(CultureInfo.InvariantCulture);
        shape.SelectedIndex = (int)value.Shape; sort.SelectedIndex = (int)value.Sort;
        corner.SelectedItem = value.ExpandedCorner; cornerButtons[value.ExpandedCorner].IsChecked = true;
        columnMode.SelectedIndex = value.Columns == 0 ? 0 : 1; if (value.Columns == 0) columns.Text = "4";
        refreshing = false;
        RefreshPreview();
    }
    private bool StoreMonitor()
    {
        if (selectedId is null) return true;
        if (!TryReadMonitor(out var value)) { ShowError("위치·간격·메모 수를 확인하세요. 간격은 0–24, 직접 지정은 1–30입니다."); return false; }
        draft.Monitors[selectedId] = value;
        return true;
    }
    private bool TryReadMonitor(out MonitorLayout value)
    {
        value = new MonitorLayout();
        if (!double.TryParse(x.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var px) || !double.TryParse(y.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var py)
            || !double.TryParse(gap.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var spacing)
            || !double.IsFinite(px) || !double.IsFinite(py) || !double.IsFinite(spacing) || px < 0 || py < 0 || spacing < 0 || spacing > 24)
            return false;
        int count = 0;
        if (shape.SelectedIndex == (int)LayoutShape.Grid && columnMode.SelectedIndex == 1 && (!int.TryParse(columns.Text, out count) || count < 1 || count > 30)) return false;
        if (shape.SelectedIndex != (int)LayoutShape.Grid) count = selectedId is string id ? draft.GetMonitor(id).Columns : 0;
        if (shape.SelectedIndex < 0 || sort.SelectedIndex < 0) return false;
        value = new MonitorLayout { X = px, Y = py, Gap = spacing, Columns = count, Shape = (LayoutShape)shape.SelectedIndex, Sort = (LayoutSort)sort.SelectedIndex, ExpandedCorner = corner.SelectedItem is ExpandedCorner direction ? direction : ExpandedCorner.TopRight };
        return true;
    }
    public void RefreshPreview()
    {
        if (refreshing) return;
        columnFields.Visibility = shape.SelectedIndex == (int)LayoutShape.Grid ? Visibility.Visible : Visibility.Collapsed;
        columns.Visibility = columnMode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        originSummary.Text = $"시작 위치  X {x.Text} · Y {y.Text}";
        shapeHint.Text = shape.SelectedIndex switch { 0 => "▦  오른쪽으로 채운 뒤 다음 줄로 내려갑니다", 1 => "▪ ▪ ▪ →  한 줄로 오른쪽에 놓습니다", _ => "▤ ↓  한 줄로 아래에 놓습니다" };
        colorHint.Visibility = sort.SelectedIndex == (int)LayoutSort.Color ? Visibility.Visible : Visibility.Collapsed;
        preview.Children.Clear();
        if (!TryReadMonitor(out var options)) { previewStatus.Text = "위치·간격·메모 수를 확인하세요. 간격 0–24, 직접 지정 1–30."; return; }
        if (monitorBox.SelectedItem is not MonitorDescriptor monitor) { previewStatus.Text = "모니터를 선택하세요."; return; }
        if (!double.IsFinite(options.X * monitor.Scale) || !double.IsFinite(options.Y * monitor.Scale))
        { previewStatus.Text = "시작 위치가 너무 큽니다. 화면 안의 위치를 입력하세요."; return; }
        var source = previewNotes?.Invoke(monitor.Id) ?? [];
        bool sample = !source.Any(n => n.IsVisible && n.DeletedAt is null);
        if (sample) source = Enumerable.Range(0, 6).Select(i => new Note { Color = i < 3 ? "#FFF2B2" : "#FFDDE7", Body = $"예시 {i + 1}", CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(i) }).ToArray();
        bool collapsed = previewMode.SelectedIndex == 0;
        var notes = source.Where(n => n.IsVisible && n.DeletedAt is null).Select(n => n with { IsCollapsed = collapsed }).ToArray();
        var result = collapsed ? NoteArrangement.ArrangeCollapsed(notes, monitor.WorkArea, options, monitor.Scale)
            : NoteArrangement.ArrangeExpanded(notes, monitor.WorkArea, options, options.ExpandedCorner, monitor.Scale,
                orderedIds: draft.ArrangementOrder.GetValueOrDefault(monitor.Id));
        double ratio = Math.Min(360 / monitor.WorkArea.Width, 220 / monitor.WorkArea.Height);
        preview.Width = monitor.WorkArea.Width * ratio; preview.Height = monitor.WorkArea.Height * ratio;
        var lookup = notes.ToDictionary(n => n.Id);
        foreach (var (id, bounds) in result.Bounds.Reverse())
        {
            var note = lookup[id];
            var item = new Border { Width = bounds.Width * ratio, Height = bounds.Height * ratio,
                Background = (Brush)new BrushConverter().ConvertFromString(note.Color)!, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(.5), ToolTip = note.Title };
            Canvas.SetLeft(item, (bounds.Left - monitor.WorkArea.Left) * ratio); Canvas.SetTop(item, (bounds.Top - monitor.WorkArea.Top) * ratio);
            preview.Children.Add(item);
        }
        previewStatus.Text = (sample ? "예시 메모 · " : $"보이는 메모 {notes.Length}개 · ")
            + (result.SkippedTiles > 0 ? $"일부 타일을 배치하지 못했습니다 ({result.SkippedTiles}개)." : result.UsedCascade ? "공간에 맞춰 계단식으로 배치합니다." : "실제 메모는 이동하지 않습니다.");
    }
    private void BuildRecorders()
    {
        hotkeyRows.Children.Clear(); recorders.Clear();
        foreach (var (section, ids) in new[] { ("메모", new[] { "NewNote", "Search", "ToggleCurrent" }), ("전체 메모", new[] { "ToggleVisibility", "ToggleCollapsed" }), ("패널", new[] { "ToggleOverlay" }) })
        {
          hotkeyRows.Children.Add(Section(section));
          foreach (var id in ids)
          {
            var label = HotkeyDefaults.Labels[id];
            var row = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
            var title = new TextBlock { Text = label, Width = 190, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(title, Dock.Left); row.Children.Add(title);
            row.SizeChanged += (_, _) => title.Width = Math.Clamp(row.ActualWidth * .4, 85, 190);
            var box = new TextBox { IsReadOnly = true, Text = draft.Hotkeys.GetValueOrDefault(id, ""), Padding = new Thickness(3), ToolTip = "선택한 뒤 키 조합을 누르세요. Esc는 입력 취소" };
            System.Windows.Automation.AutomationProperties.SetName(box, label + " 단축키");
            var clear = Button("해제", () => SetGesture(id, "")); DockPanel.SetDock(clear, Dock.Right); row.Children.Add(clear);
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
    private void Dispatch()
    {
        EndCapture(); if (!StoreMonitor()) return;
        try
        {
            HotkeyService.Validate(draft.Hotkeys);
            var candidate = draft with { AutoArrange = false, SelectedMonitor = selectedId, PanelLeft = Left, PanelTop = Top, Monitors = new(draft.Monitors), Hotkeys = new(draft.Hotkeys) };
            candidate.Validate();
            ApplyRequested?.Invoke(candidate, autoStart.IsChecked == true);
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
