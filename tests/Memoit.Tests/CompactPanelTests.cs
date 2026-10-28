using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Memoit.Services;
using Memoit.Views;
using Xunit;

namespace Memoit.Tests;

[Collection("WPF")]
public sealed class CompactPanelTests
{
    [Fact]
    public void CompactCommandsKeepTheirMeaningAndShortcutTips() => Sta(() =>
    {
        var panel = new OverlayPanelWindow(new LayoutSettings { OverlayCompact = true });
        try
        {
            Assert.True(panel.IsCompact);
            Assert.Equal(240, panel.Width);
            var root = (FrameworkElement)panel.Content;
            root.Measure(new Size(240, double.PositiveInfinity));
            Assert.Equal(88, root.DesiredSize.Height);
            string? received = null;
            panel.CommandRequested += command => received = command;
            foreach (var (label, command) in new[] { ("새 메모", "NewNote"), ("목록·검색", "Search"), ("모두 접기", "CollapseAll"), ("모두 펼치기", "ExpandAll"), ("모두 숨기기", "HideAll"), ("모두 보이기", "ShowAll"), ("접어서 정돈", "Arrange"), ("펼쳐서 정돈", "ArrangeExpanded") })
            {
                var button = Descendants(panel).OfType<Button>().Single(b => AutomationProperties.GetName(b) == label);
                Assert.IsType<System.Windows.Shapes.Path>(button.Content);
                Assert.Contains(label, button.ToolTip.ToString());
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(command, received);
            }
            var collapse = Descendants(panel).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "모두 접기");
            Assert.Contains("Ctrl+Alt+Shift+C", collapse.ToolTip.ToString());
        }
        finally { panel.AllowClose = true; panel.Close(); }
    });

    [Fact]
    public void ModeSwitchPreservesPinColorAndAnchorWithoutRepeatedEvents() => Sta(() =>
    {
        var panel = new OverlayPanelWindow(new LayoutSettings { OverlayTopmost = true, OverlayColor = "#FFDDE7" }) { Left = 140, Top = 160 };
        try
        {
            int changed = 0;
            panel.CompactChanged += () => changed++;
            panel.SetCompact(true);
            Assert.Equal(140, panel.Left); Assert.Equal(160, panel.Top);
            Assert.True(panel.Topmost); Assert.Equal("#FFDDE7", panel.SelectedColor);
            var pin = Descendants(panel).OfType<ToggleButton>().Single(b => AutomationProperties.GetName(b) == "패널 항상 위");
            pin.IsChecked = false;
            Assert.False(panel.Topmost); Assert.False(panel.SelectedTopmost);
            panel.SetCompact(false);
            Assert.Equal(280, panel.Width); Assert.False(panel.SelectedTopmost);
            Assert.Equal(140, panel.Left); Assert.Equal(160, panel.Top);
            panel.SetCompact(false);
            Assert.Equal(2, changed);
        }
        finally { panel.AllowClose = true; panel.Close(); }
    });

    [Fact]
    public void CompactErrorsRemainDiscoverableAfterPopupDismissal() => Sta(() =>
    {
        var panel = new OverlayPanelWindow(new LayoutSettings { OverlayCompact = true });
        try
        {
            panel.ShowStatus("저장 실패", true);
            var alert = Descendants(panel).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "오류 안내 열기");
            Assert.Equal(Visibility.Visible, alert.Visibility);
            panel.SetCompact(false);
            Assert.Contains(Descendants(panel).OfType<TextBlock>(), t => t.Text == "저장 실패");
            panel.SetCompact(true);
            Assert.Equal(Visibility.Visible, alert.Visibility);
            panel.ShowStatus("완료");
            Assert.Equal(Visibility.Collapsed, alert.Visibility);
        }
        finally { panel.AllowClose = true; panel.Close(); }
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
