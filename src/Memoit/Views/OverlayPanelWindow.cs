using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Memoit.Services;
using Memoit.Models;

namespace Memoit.Views;

public sealed class OverlayPanelWindow : Window
{
    private static readonly string[] ColorNames = ["전체", "노랑", "분홍", "초록", "파랑", "보라", "흰색"];
    private readonly List<ToggleButton> colorChips = [];
    private readonly int[] colorCounts = new int[7];
    private readonly List<(Button Button, string Description, string[] Commands)> shortcutButtons = [];
    private readonly ContentControl undoHost = new();
    private string? selectedColor;
    private Dictionary<string, string> hotkeys = [];
    private readonly ToggleButton pinned = new();
    private readonly PagesControl pages = new();

    private readonly TextBlock status = new() { MinHeight = 18, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 7, 2, 0) };
    private readonly DispatcherTimer statusTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    private bool refreshing;
    public bool AllowClose { get; set; }
    public event Action<string>? CommandRequested;
    public event Action<bool, string?>? CollapseRequested;
    public event Action? PreferencesChanged;
    public event Action? Hidden;
    public event Action<string, int>? PageRequested;
    public string? SelectedColor => selectedColor;

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
        shortcutButtons.Add((settingsButton, "설정 열기 / 닫기", ["TogglePanel"]));
        shortcutButtons.Add((close, "패널 숨기기", ["ToggleOverlay"]));
        DockPanel.SetDock(settingsButton, Dock.Right); header.Children.Add(settingsButton);
        pinned.Content = CreateIcon("M 5,2 L 11,2 L 10,7 L 13,10 L 9,10 L 8,15 L 7,10 L 3,10 L 6,7 Z");
        pinned.Style = ControlStyle(typeof(ToggleButton), false);
        pinned.Background = Brushes.Transparent;
        pinned.Width = 28; pinned.Height = 28; pinned.Padding = new Thickness(5); pinned.ToolTip = "패널 항상 위";
        AutomationProperties.SetName(pinned, "패널 항상 위"); DockPanel.SetDock(pinned, Dock.Right); header.Children.Add(pinned);
        var title = new TextBlock { Text = "OmniMemo", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = Brush("#243247"), Cursor = Cursors.SizeAll };
        title.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) { e.Handled = true; DragMove(); PreferencesChanged?.Invoke(); } };
        header.Children.Add(title); content.Children.Add(header);
        content.Children.Add(Section("메모"));
        content.Children.Add(Row(CommandButton("새 메모", "NewNote"), CommandButton("목록·검색", "Search")));
        content.Children.Add(Section("전체 조작"));
        content.Children.Add(Row(CommandButton("모두 접기", "CollapseAll", "ToggleCollapsed"), CommandButton("모두 펼치기", "ExpandAll", "ToggleCollapsed")));
        content.Children.Add(Row(CommandButton("모두 숨기기", "HideAll", "ToggleVisibility"), CommandButton("모두 보이기", "ShowAll", "ToggleVisibility")));
        content.Children.Add(Section("정돈"));
        content.Children.Add(Row(CommandButton("접어서 정돈", "Arrange"), CommandButton("펼쳐서 정돈", "ArrangeExpanded")));
        pages.PageRequested += (monitor, delta) => PageRequested?.Invoke(monitor, delta);
        content.Children.Add(pages);
        var filtered = new StackPanel();
        var chips = new UniformGrid { Columns = 4, Margin = new Thickness(0, 8, 0, 5) };
        for (int i = 0; i < ColorNames.Length; i++)
        {
            int index = i;
            var chip = new ToggleButton { MinHeight = 34, Margin = new Thickness(2), Padding = new Thickness(2), Background = i == 0 ? Brush("#EDF1F6") : Brush(NoteColors.Values[i - 1]) };
            chip.Checked += (_, _) =>
            {
                if (refreshing) return;
                selectedColor = index == 0 ? null : NoteColors.Values[index - 1];
                RefreshChips(); Changed();
            };
            chip.Unchecked += (_, _) =>
            {
                if (!refreshing) RefreshChips();
            };
            colorChips.Add(chip); chips.Children.Add(chip);
        }
        filtered.Children.Add(chips);
        filtered.Children.Add(Row(Button("선택 색상 접기", () => CollapseRequested?.Invoke(true, SelectedColor)), Button("선택 색상 펼치기", () => CollapseRequested?.Invoke(false, SelectedColor))));
        content.Children.Add(new Expander { Header = "색상별 조작", IsExpanded = false, Content = filtered, Margin = new Thickness(2, 12, 2, 0), Foreground = Brush("#526079") });
        content.Children.Add(status); AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        content.Children.Add(undoHost);
        RefreshSettings(settings);
        pinned.Checked += (_, _) => Changed(); pinned.Unchecked += (_, _) => Changed();
        statusTimer.Tick += (_, _) => { statusTimer.Stop(); status.Text = ""; };

        IsVisibleChanged += (_, _) => { if (!IsVisible) FlushPreferences(); };
        Closing += (_, e) => { FlushPreferences(); if (!AllowClose) { e.Cancel = true; Hide(); Hidden?.Invoke(); } else statusTimer.Stop(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }

    public void SetPages(IReadOnlyList<PageDisplay> displays) => pages.SetPages(displays);
    public void SetUndoContent(UIElement? content) => undoHost.Content = content;
    public void SetNotes(IEnumerable<Note> notes)
    {
        var counts = new int[colorCounts.Length];
        foreach (var note in notes.Where(n => n.IsVisible && n.DeletedAt is null))
        {
            counts[0]++;
            int index = Array.FindIndex(NoteColors.Values, c => NoteColors.Matches(note.Color, c));
            if (index >= 0) counts[index + 1]++;
        }
        if (counts.SequenceEqual(colorCounts)) return;
        counts.CopyTo(colorCounts, 0);
        RefreshChips();
    }
    private void RefreshChips()
    {
        bool wasRefreshing = refreshing;
        refreshing = true;
        for (int i = 0; i < colorChips.Count; i++)
        {
            bool active = i == 0 ? selectedColor is null : selectedColor is not null && NoteColors.Matches(selectedColor, NoteColors.Values[i - 1]);
            var chip = colorChips[i]; chip.IsChecked = active;
            chip.Content = new StackPanel { Children = { new TextBlock { Text = (active ? "✓ " : "") + ColorNames[i], FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center }, new TextBlock { Text = colorCounts[i].ToString(), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center } } };
            chip.ToolTip = $"{ColorNames[i]} · 보이는 메모 {colorCounts[i]}개";
            AutomationProperties.SetName(chip, $"{ColorNames[i]} · 보이는 메모 {colorCounts[i]}개" + (active ? " · 선택됨" : ""));
        }
        refreshing = wasRefreshing;
    }
    private Button CommandButton(string label, string command, params string[] aliases)
    {
        var button = Button(label, () => CommandRequested?.Invoke(command));
        shortcutButtons.Add((button, label, new[] { command }.Concat(aliases).ToArray())); return button;
    }
    private void SetShortcutTip(Button button, string description, params string[] commands)
    {
        var keys = commands.Select(command => hotkeys.GetValueOrDefault(command)).Where(key => !string.IsNullOrWhiteSpace(key)).Distinct().ToArray();
        button.ToolTip = description + (keys.Length == 0 ? "" : "\n" + string.Join(" / ", keys));
    }
    public void ShowStatus(string message, bool error = false)
    {
        statusTimer.Stop(); status.Foreground = error ? Brushes.Firebrick : Brush("#526079"); status.Text = message;
        if (!error) statusTimer.Start();
    }
    public void RefreshSettings(LayoutSettings settings)
    {
        refreshing = true;
        selectedColor = settings.OverlayColor is null ? null : NoteColors.Values.FirstOrDefault(c => NoteColors.Matches(c, settings.OverlayColor));
        RefreshChips();
        hotkeys = new Dictionary<string, string>(settings.Hotkeys);
        foreach (var item in shortcutButtons) SetShortcutTip(item.Button, item.Description, item.Commands);
        Opacity = 1;
        pinned.IsChecked = settings.OverlayTopmost; Topmost = settings.OverlayTopmost;
        UpdatePin();
        refreshing = false;
    }
    public void FlushPreferences() { }
    private void Changed() { if (refreshing) return; UpdatePin(); Topmost = SelectedTopmost; PreferencesChanged?.Invoke(); }
    private void UpdatePin()
    {
        var icon = (System.Windows.Shapes.Path)pinned.Content;
        var color = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Brush(SelectedTopmost ? "#176B61" : "#595F5E");
        icon.Stroke = color; icon.Fill = SelectedTopmost ? color : Brushes.Transparent;
        pinned.ToolTip = SelectedTopmost ? "패널 고정됨 · 클릭하여 항상 위 해제" : "패널 고정 안 됨 · 클릭하여 항상 위에 표시";
    }
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
        var button = new Button { Content = label, ToolTip = label, MinHeight = 34, Margin = new Thickness(2), Padding = new Thickness(4, 6, 4, 6) };
        AutomationProperties.SetName(button, label); button.Click += (_, _) => action(); return button;
    }
    private static System.Windows.Shapes.Path CreateIcon(string data) => new() { Data = Geometry.Parse(data), Stroke = Brush("#526079"), StrokeThickness = 1.4, Width = 16, Height = 16, Stretch = Stretch.Uniform };
    private static Button IconButton(string data, string label, Action action)
    {
        var button = Button(label, action); button.Content = CreateIcon(data); button.ToolTip = label; button.Width = 28; button.Height = 28; button.MinHeight = 28; button.Padding = new Thickness(5); return button;
    }
    private static Style ControlStyle(Type type, bool showCheckedBorder = true)
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
        if (type == typeof(ToggleButton) && showCheckedBorder) { var active = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true }; active.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("#426FA6"), "surface")); template.Triggers.Add(active); }
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true }; focus.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("#426FA6"), "surface")); template.Triggers.Add(focus);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .45, "surface")); template.Triggers.Add(disabled);
        style.Setters.Add(new Setter(Control.TemplateProperty, template)); return style;
    }
}
