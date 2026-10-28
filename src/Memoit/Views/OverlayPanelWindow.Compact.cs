using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Memoit.Services;

namespace Memoit.Views;

public sealed partial class OverlayPanelWindow
{
    private readonly StackPanel normalContent = new() { Margin = new Thickness(14) };
    private readonly StackPanel compactContent = new() { Margin = new Thickness(8, 4, 8, 6) };
    private readonly Border panelBorder = new() { CornerRadius = new CornerRadius(12), Background = Brush("#F8FAFC"), BorderBrush = Brush("#D8E0E9"), BorderThickness = new Thickness(1) };
    private readonly ToggleButton compactPinned = new();
    private readonly Button compactError = new() { Content = "!", FontSize = 10, Width = 20, Height = 12, Padding = new Thickness(0), Visibility = Visibility.Collapsed, Foreground = Brushes.Firebrick, ToolTip = "오류 안내 열기" };
    private readonly TextBlock popupMessage = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 280, Margin = new Thickness(8), Foreground = Brushes.Firebrick };
    private readonly Popup statusPopup = new() { Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
    private bool statusIsError;
    private bool syncingPin;
    public bool IsCompact { get; private set; }
    public event Action? CompactChanged;

    public void SetCompact(bool compact)
    {
        if (IsCompact == compact) return;
        IsCompact = compact;
        panelBorder.Child = compact ? compactContent : normalContent;
        Width = compact ? 240 : 280;
        Title = compact ? "OmniMemo · 미니패널" : "OmniMemo · 패널";
        // Only expanding can move the panel off screen. Measure its new size before clamping.
        if (!compact && IsLoaded)
        {
            UpdateLayout();
            WindowPlacement.KeepOnScreen(this);
        }
        statusPopup.IsOpen = compact && IsVisible && statusIsError;
        if (!refreshing) { CompactChanged?.Invoke(); PreferencesChanged?.Invoke(); }
    }

    private void DragPanel(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        for (var source = e.OriginalSource as DependencyObject; source is not null && source != sender;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source))
            if (source is ButtonBase) return;
        e.Handled = true;
        statusPopup.IsOpen = false;
        DragMove();
        PreferencesChanged?.Invoke();
    }

    private void BuildCompactContent()
    {
        var grip = new Grid { Height = 12, Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        AutomationProperties.SetName(grip, "미니패널 이동");
        grip.MouseLeftButtonDown += DragPanel;
        grip.Children.Add(new Border { Width = 34, Height = 3, CornerRadius = new CornerRadius(2), Background = Brush("#BCC9D7"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        compactError.HorizontalAlignment = HorizontalAlignment.Right;
        AutomationProperties.SetName(compactError, "오류 안내 열기");
        compactError.Click += (_, _) => statusPopup.IsOpen = true;
        grip.Children.Add(compactError);
        compactContent.Children.Add(grip);
        var grid = new UniformGrid { Rows = 2, Columns = 6, Height = 64 };
        grid.Children.Add(CompactCommand("M 8,2 L 8,14 M 2,8 L 14,8", "새 메모", "NewNote"));
        grid.Children.Add(CompactCommand("M 11,6 A 5,5 0 1 1 1,6 A 5,5 0 1 1 11,6 M 10,10 L 15,15", "목록·검색", "Search"));
        grid.Children.Add(CompactCommand("M 2,3 L 8,9 14,3 M 3,13 L 13,13", "모두 접기", "CollapseAll", "ToggleCollapsed"));
        grid.Children.Add(CompactCommand("M 2,10 L 8,4 14,10 M 3,14 L 13,14", "모두 펼치기", "ExpandAll", "ToggleCollapsed"));
        grid.Children.Add(CompactCommand("M 1,8 Q 8,-2 15,8 Q 8,18 1,8 M 2,14 L 14,2", "모두 숨기기", "HideAll", "ToggleVisibility"));
        grid.Children.Add(CompactCommand("M 1,8 Q 8,-2 15,8 Q 8,18 1,8 M 10,8 A 2,2 0 1 1 6,8 A 2,2 0 1 1 10,8", "모두 보이기", "ShowAll", "ToggleVisibility"));
        grid.Children.Add(CompactCommand("M 1,1 L 6,1 6,6 1,6 Z M 10,1 L 15,1 15,6 10,6 Z M 1,10 L 6,10 6,15 1,15 Z M 10,10 L 15,10 15,15 10,15 Z", "접어서 정돈", "Arrange"));
        grid.Children.Add(CompactCommand("M 1,1 L 7,1 7,15 1,15 Z M 10,1 L 16,1 16,15 10,15 Z M 1,5 L 7,5 M 10,5 L 16,5", "펼쳐서 정돈", "ArrangeExpanded"));
        compactPinned.Content = CreateIcon("M 5,2 L 11,2 L 10,7 L 13,10 L 9,10 L 8,15 L 7,10 L 3,10 L 6,7 Z");
        compactPinned.Style = ControlStyle(typeof(ToggleButton), false);
        compactPinned.Width = 28; compactPinned.Height = 28; compactPinned.Padding = new Thickness(5);
        AutomationProperties.SetName(compactPinned, "패널 항상 위");
        compactPinned.Checked += (_, _) => { if (!syncingPin) pinned.IsChecked = true; };
        compactPinned.Unchecked += (_, _) => { if (!syncingPin) pinned.IsChecked = false; };
        grid.Children.Add(compactPinned);
        grid.Children.Add(CompactCommand("M 2,4 L 14,4 M 2,8 L 14,8 M 2,12 L 14,12 M 5,2 L 5,6 M 11,6 L 11,10 M 7,10 L 7,14", "설정", "TogglePanel"));
        grid.Children.Add(IconButton("M 1,6 L 1,1 6,1 M 10,1 L 15,1 15,6 M 15,10 L 15,15 10,15 M 6,15 L 1,15 1,10", "일반 패널로 복귀", () => SetCompact(false)));
        var close = IconButton("M 3,3 L 13,13 M 13,3 L 3,13", "패널 숨기기", Close);
        shortcutButtons.Add((close, "패널 숨기기", ["ToggleOverlay"]));
        grid.Children.Add(close);
        compactContent.Children.Add(grid);
        statusPopup.PlacementTarget = this;
        var popupBody = new StackPanel();
        popupBody.Children.Add(popupMessage);
        var dismiss = Button("닫기", () => statusPopup.IsOpen = false);
        popupBody.Children.Add(dismiss);
        statusPopup.Child = new Border { Background = Brushes.White, BorderBrush = Brushes.Firebrick, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = popupBody };
        AutomationProperties.SetLiveSetting(popupMessage, AutomationLiveSetting.Assertive);
        Closed += (_, _) => statusPopup.IsOpen = false;
    }

    private Button CompactCommand(string icon, string label, string command, params string[] aliases)
    {
        var button = IconButton(icon, label, () => CommandRequested?.Invoke(command));
        shortcutButtons.Add((button, label, new[] { command }.Concat(aliases).ToArray()));
        return button;
    }
}
