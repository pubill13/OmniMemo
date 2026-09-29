using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Memoit.Services;

namespace Memoit.Views;

public sealed class OverlayPanelWindow : Window
{
    private readonly ComboBox color = new() { ItemsSource = new[] { "모든 색상", "노랑", "분홍", "초록", "파랑", "보라", "흰색" } };
    private readonly Slider opacity = new() { Minimum = 30, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true };
    private readonly CheckBox pinned = new() { Content = "항상 위", Margin = new Thickness(3, 5, 3, 5) };
    private bool refreshing;
    public bool AllowClose { get; set; }
    public event Action<string>? CommandRequested;
    public event Action<bool, string?>? CollapseRequested;
    public event Action? PreferencesChanged;
    public event Action? Hidden;
    public string? SelectedColor => color.SelectedIndex > 0 ? NoteColors.Values[color.SelectedIndex - 1] : null;
    public double SelectedOpacity => opacity.Value / 100;
    public bool SelectedTopmost => pinned.IsChecked == true;

    public OverlayPanelWindow(LayoutSettings settings)
    {
        Title = "OmniMemo · 데스크톱 패널"; Width = 272; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        AllowsTransparency = true; Background = Brushes.Transparent; FontFamily = new FontFamily("Malgun Gothic");
        var content = new StackPanel { Margin = new Thickness(12) };
        Content = new Border { CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromRgb(244, 245, 248)), BorderBrush = Brushes.SlateGray, BorderThickness = new Thickness(1), Child = content };
        var header = new DockPanel { Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 6) };
        var close = Button("×", () => Close()); close.ToolTip = "패널 숨기기"; DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        var title = new TextBlock { Text = "OmniMemo", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Cursor = Cursors.SizeAll };
        title.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) { DragMove(); PreferencesChanged?.Invoke(); } };
        header.Children.Add(title); content.Children.Add(header);
        content.Children.Add(Row(Button("새 메모", () => CommandRequested?.Invoke("NewNote")), Button("목록 / 검색", () => CommandRequested?.Invoke("Search"))));
        content.Children.Add(new TextBlock { Text = "보이는 메모 · 색상별 접기 / 펼치기", Margin = new Thickness(2, 9, 0, 4) });
        content.Children.Add(color);
        content.Children.Add(Row(Button("접기", () => CollapseRequested?.Invoke(true, SelectedColor)), Button("펼치기", () => CollapseRequested?.Invoke(false, SelectedColor))));
        content.Children.Add(Row(Button("전체 숨김 / 보임", () => CommandRequested?.Invoke("ToggleVisibility")), Button("모두 정돈", () => CommandRequested?.Invoke("Arrange"))));
        content.Children.Add(Row(Button("접힌 메모 정돈", () => CommandRequested?.Invoke("ArrangeCollapsed")), Button("펼친 메모 정돈", () => CommandRequested?.Invoke("ArrangeExpanded"))));
        content.Children.Add(Button("이전 배치로", () => CommandRequested?.Invoke("UndoArrange")));
        content.Children.Add(new TextBlock { Text = "타일 배치", Margin = new Thickness(2, 7, 0, 2) });
        content.Children.Add(Row(Button("격자", () => CommandRequested?.Invoke("ShapeGrid")), Button("가로", () => CommandRequested?.Invoke("ShapeHorizontal")), Button("세로", () => CommandRequested?.Invoke("ShapeVertical"))));
        content.Children.Add(Row(Button("생성순", () => CommandRequested?.Invoke("SortCreated")), Button("색상순", () => CommandRequested?.Invoke("SortColor")), Button("제목순", () => CommandRequested?.Invoke("SortTitle"))));
        content.Children.Add(Row(pinned, Button("설정…", () => CommandRequested?.Invoke("TogglePanel"))));
        content.Children.Add(new TextBlock { Text = "불투명도 (30–100%)", Margin = new Thickness(2, 5, 0, 0) }); content.Children.Add(opacity);
        foreach (var (element, name) in new (DependencyObject, string)[] { (color, "패널 색상 필터"), (opacity, "패널 불투명도"), (pinned, "패널 항상 위"), (close, "데스크톱 패널 숨기기") })
            System.Windows.Automation.AutomationProperties.SetName(element, name);
        RefreshSettings(settings);
        color.SelectionChanged += (_, _) => Changed();
        pinned.Checked += (_, _) => Changed(); pinned.Unchecked += (_, _) => Changed();
        opacity.ValueChanged += (_, _) => { Opacity = SelectedOpacity; };
        opacity.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent, new System.Windows.Controls.Primitives.DragCompletedEventHandler((_, _) => Changed()));
        opacity.PreviewMouseLeftButtonUp += (_, _) => Changed(); opacity.KeyUp += (_, _) => Changed();
        Closing += (_, e) => { if (!AllowClose) { e.Cancel = true; Hide(); Hidden?.Invoke(); } };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }

    public void RefreshSettings(LayoutSettings settings)
    {
        refreshing = true;
        color.SelectedIndex = settings.OverlayColor is null ? 0 : Array.FindIndex(NoteColors.Values, c => NoteColors.Matches(c, settings.OverlayColor)) + 1;
        opacity.Value = settings.OverlayOpacity * 100; Opacity = settings.OverlayOpacity;
        pinned.IsChecked = settings.OverlayTopmost; Topmost = settings.OverlayTopmost;
        refreshing = false;
    }
    private void Changed() { if (refreshing) return; Topmost = SelectedTopmost; PreferencesChanged?.Invoke(); }
    private static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var child in children) row.Children.Add(child); return row;
    }
    private static Button Button(string label, Action action)
    {
        var button = new Button { Content = label, Padding = new Thickness(7, 4, 7, 4), Margin = new Thickness(2) };
        button.Click += (_, _) => action(); return button;
    }
}
