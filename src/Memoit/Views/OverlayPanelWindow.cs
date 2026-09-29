using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Memoit.Services;

namespace Memoit.Views;

public sealed class OverlayPanelWindow : Window
{
    private readonly ComboBox color = new() { ItemsSource = new[] { "모든 색상", "노랑", "분홍", "초록", "파랑", "보라", "흰색" }, Margin = new Thickness(0, 8, 0, 5) };
    private readonly ToggleButton pinned = new();
    private readonly Button visibility;
    private readonly TextBlock status = new() { MinHeight = 18, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 7, 2, 0) };
    private readonly DispatcherTimer statusTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private double selectedOpacity;
    private bool refreshing;
    public bool AllowClose { get; set; }
    public event Action<string>? CommandRequested;
    public event Action<bool, string?>? CollapseRequested;
    public event Action? PreferencesChanged;
    public event Action? Hidden;
    public string? SelectedColor => color.SelectedIndex > 0 ? NoteColors.Values[color.SelectedIndex - 1] : null;
    public double SelectedOpacity => selectedOpacity;
    public bool SelectedTopmost => pinned.IsChecked == true;

    public OverlayPanelWindow(LayoutSettings settings)
    {
        Title = "OmniMemo · 데스크톱 패널"; Width = 280; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        AllowsTransparency = true; Background = Brushes.Transparent; FontFamily = new FontFamily("Malgun Gothic"); FontSize = 12;
        Resources.Add(typeof(Button), ControlStyle(typeof(Button)));
        Resources.Add(typeof(ToggleButton), ControlStyle(typeof(ToggleButton)));
        var content = new StackPanel { Margin = new Thickness(14) };
        Content = new Border { CornerRadius = new CornerRadius(12), Background = Brush("#F8FAFC"), BorderBrush = Brush("#D8E0E9"), BorderThickness = new Thickness(1), Child = content };
        var header = new DockPanel { Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 14) };
        var close = IconButton("M 3,3 L 13,13 M 13,3 L 3,13", "패널 숨기기", Close);
        DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        var settingsButton = IconButton("M 2,4 L 14,4 M 2,8 L 14,8 M 2,12 L 14,12 M 5,2 L 5,6 M 11,6 L 11,10 M 7,10 L 7,14", "설정", () => CommandRequested?.Invoke("TogglePanel"));
        DockPanel.SetDock(settingsButton, Dock.Right); header.Children.Add(settingsButton);
        pinned.Content = CreateIcon("M 5,2 L 11,2 L 10,7 L 13,10 L 9,10 L 8,15 L 7,10 L 3,10 L 6,7 Z");
        pinned.Width = 28; pinned.Height = 28; pinned.Padding = new Thickness(5); pinned.ToolTip = "패널 항상 위";
        AutomationProperties.SetName(pinned, "패널 항상 위"); DockPanel.SetDock(pinned, Dock.Right); header.Children.Add(pinned);
        var title = new TextBlock { Text = "OmniMemo", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = Brush("#243247"), Cursor = Cursors.SizeAll };
        title.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) { DragMove(); PreferencesChanged?.Invoke(); } };
        header.Children.Add(title); content.Children.Add(header);
        content.Children.Add(Section("메모"));
        content.Children.Add(Row(Button("새 메모", () => CommandRequested?.Invoke("NewNote")), Button("목록·검색", () => CommandRequested?.Invoke("Search"))));
        content.Children.Add(Section("전체 조작"));
        var arrange = Button("접어서 정돈", () => CommandRequested?.Invoke("Arrange")); arrange.Background = Brush("#E0EAF8");
        var expand = Button("모두 펼치기", () => CommandRequested?.Invoke("ExpandAll")); expand.Background = Brush("#E0EAF8");
        content.Children.Add(Row(arrange, expand));
        visibility = Button("모두 숨기기", () => CommandRequested?.Invoke("ToggleVisibility")); content.Children.Add(visibility);
        var filtered = new StackPanel(); filtered.Children.Add(color);
        filtered.Children.Add(Row(Button("선택 색상 접기", () => CollapseRequested?.Invoke(true, SelectedColor)), Button("선택 색상 펼치기", () => CollapseRequested?.Invoke(false, SelectedColor))));
        content.Children.Add(new Expander { Header = "색상별 조작", IsExpanded = false, Content = filtered, Margin = new Thickness(2, 12, 2, 0), Foreground = Brush("#526079") });
        content.Children.Add(status); AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(color, "패널 색상 필터");
        RefreshSettings(settings);
        color.SelectionChanged += (_, _) => Changed();
        pinned.Checked += (_, _) => Changed(); pinned.Unchecked += (_, _) => Changed();
        statusTimer.Tick += (_, _) => { statusTimer.Stop(); status.Text = ""; };
        Closing += (_, e) => { if (!AllowClose) { e.Cancel = true; Hide(); Hidden?.Invoke(); } else statusTimer.Stop(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }

    public void SetHasVisibleNotes(bool value)
    {
        string label = value ? "모두 숨기기" : "모두 보이기";
        visibility.Content = label; AutomationProperties.SetName(visibility, label);
    }
    public void ShowStatus(string message, bool error = false)
    {
        statusTimer.Stop(); status.Foreground = error ? Brushes.Firebrick : Brush("#526079"); status.Text = message;
        if (!error) statusTimer.Start();
    }
    public void RefreshSettings(LayoutSettings settings)
    {
        refreshing = true;
        color.SelectedIndex = settings.OverlayColor is null ? 0 : Array.FindIndex(NoteColors.Values, c => NoteColors.Matches(c, settings.OverlayColor)) + 1;
        selectedOpacity = settings.OverlayOpacity; Opacity = selectedOpacity;
        pinned.IsChecked = settings.OverlayTopmost; Topmost = settings.OverlayTopmost;
        refreshing = false;
    }
    private void Changed() { if (refreshing) return; Topmost = SelectedTopmost; PreferencesChanged?.Invoke(); }
    private static SolidColorBrush Brush(string value) => (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
    private static TextBlock Section(string text) => new() { Text = text, FontSize = 11, Foreground = Brush("#64748B"), Margin = new Thickness(2, 9, 2, 5) };
    private static Grid Row(params UIElement[] children)
    {
        var row = new Grid();
        for (int i = 0; i < children.Length; i++) { row.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetColumn(children[i], i); row.Children.Add(children[i]); }
        return row;
    }
    private static Button Button(string label, Action action)
    {
        var button = new Button { Content = label, MinHeight = 34, Margin = new Thickness(2), Padding = new Thickness(4, 6, 4, 6) };
        AutomationProperties.SetName(button, label); button.Click += (_, _) => action(); return button;
    }
    private static System.Windows.Shapes.Path CreateIcon(string data) => new() { Data = Geometry.Parse(data), Stroke = Brush("#526079"), StrokeThickness = 1.4, Width = 16, Height = 16, Stretch = Stretch.Uniform };
    private static Button IconButton(string data, string label, Action action)
    {
        var button = Button(label, action); button.Content = CreateIcon(data); button.ToolTip = label; button.Width = 28; button.Height = 28; button.MinHeight = 28; button.Padding = new Thickness(5); return button;
    }
    private static Style ControlStyle(Type type)
    {
        var style = new Style(type);
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brush("#EDF1F6")));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brush("#243247")));
        var border = new FrameworkElementFactory(typeof(Border)); border.Name = "surface";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6)); border.SetValue(Border.BorderThicknessProperty, new Thickness(1)); border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetBinding(FrameworkElement.MarginProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) }); border.AppendChild(presenter);
        var template = new ControlTemplate(type) { VisualTree = border };
        foreach (var (property, background) in new[] { (UIElement.IsMouseOverProperty, "#DCE5F0"), (ButtonBase.IsPressedProperty, "#CAD8E9") })
        { var trigger = new Trigger { Property = property, Value = true }; trigger.Setters.Add(new Setter(Border.BackgroundProperty, Brush(background), "surface")); template.Triggers.Add(trigger); }
        if (type == typeof(ToggleButton)) { var active = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true }; active.Setters.Add(new Setter(Border.BackgroundProperty, Brush("#BCD4F4"), "surface")); template.Triggers.Add(active); }
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true }; focus.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("#426FA6"), "surface")); template.Triggers.Add(focus);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .45, "surface")); template.Triggers.Add(disabled);
        style.Setters.Add(new Setter(Control.TemplateProperty, template)); return style;
    }
}
