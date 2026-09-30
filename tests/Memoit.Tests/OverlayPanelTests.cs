using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Memoit.Models;
using Memoit.Services;
using Memoit.Views;
using Xunit;

namespace Memoit.Tests;

[Collection("WPF")]
public sealed class OverlayPanelTests
{
    [Fact]
    public void OverlayPreferencesRoundTripAndRejectInvalidValuesWithoutReplacingFile()
    {
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var settings = new LayoutSettings { OverlayLeft = -240, OverlayTop = 80, OverlayOpacity = .55,
                OverlayVisible = true, OverlayTopmost = true, OverlayColor = "#FFF2B3" };
            settings.Save(file);
            var restored = LayoutSettings.Load(file);
            Assert.Equal(-240, restored.OverlayLeft); Assert.Equal(80, restored.OverlayTop);
            Assert.Equal(.55, restored.OverlayOpacity); Assert.True(restored.OverlayVisible);
            Assert.True(restored.OverlayTopmost); Assert.Equal("#FFF2B3", restored.OverlayColor);
            string original = File.ReadAllText(file);
            Assert.Throws<InvalidDataException>(() => (settings with { OverlayOpacity = .1 }).Save(file));
            Assert.Throws<InvalidDataException>(() => (settings with { OverlayColor = "unknown" }).Save(file));
            Assert.Equal(original, File.ReadAllText(file));
        }
        finally { File.Delete(file); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    [Theory]
    [InlineData("#FFF2B2", "#FFF2B3", true)]
    [InlineData("#fff2b3", "#FFF2B3", true)]
    [InlineData("#FFDDE7", "#FFF2B3", false)]
    [InlineData("#FFDDE7", null, true)]
    public void ColorFilterIncludesLegacyYellowAndAll(string actual, string? filter, bool expected)
        => Assert.Equal(expected, NoteColors.Matches(actual, filter));

    [Fact]
    public void PanelRestoresPreferencesAndDispatchesExplicitColorActions()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var panel = new OverlayPanelWindow(new LayoutSettings { OverlayOpacity = .55, OverlayTopmost = true, OverlayColor = "#FFF2B3" });
                try
                {
                    Assert.Equal(.55, panel.Opacity); Assert.True(panel.Topmost); Assert.False(panel.ShowInTaskbar);
                    Assert.Equal("#FFF2B3", panel.SelectedColor);
                    panel.SetNotes([new Note { Color = "#FFF2B2" }, new Note { Color = "#FFF2B3" }, new Note { Color = "#FFDDE7" }, new Note { IsVisible = false }, new Note { DeletedAt = DateTimeOffset.UtcNow }]);
                    Assert.Empty(Descendants(panel).OfType<ComboBox>());
                    var chips = Descendants(panel).OfType<ToggleButton>().Where(c => AutomationProperties.GetName(c).Contains("보이는 메모")).ToArray();
                    Assert.Equal(7, chips.Length);
                    Assert.Contains("전체 · 보이는 메모 3개", AutomationProperties.GetName(chips[0]));
                    Assert.Contains("노랑 · 보이는 메모 2개 · 선택됨", AutomationProperties.GetName(chips[1]));
                    Assert.True(chips[1].IsChecked); Assert.True(chips[1].Focusable);
                    bool? collapsed = null; string? selected = null;
                    panel.CollapseRequested += (value, color) => { collapsed = value; selected = color; };
                    var content = (StackPanel)((Border)panel.Content).Child;
                    var buttons = Descendants(panel).OfType<Button>().ToArray();
                    var expander = Descendants(panel).OfType<Expander>().Single(); Assert.False(expander.IsExpanded);
                    Assert.Equal(280, panel.Width);
                    var root = (FrameworkElement)panel.Content; root.Measure(new Size(280, double.PositiveInfinity)); root.Arrange(new Rect(0, 0, 280, root.DesiredSize.Height));
                    var arrange = buttons.Single(b => Equals(b.Content, "접어서 정돈"));
                    var expand = buttons.Single(b => Equals(b.Content, "모두 펼치기"));
                    Assert.Equal(arrange.ActualWidth, expand.ActualWidth);
                    Assert.True(arrange.ActualWidth >= 110);
                    Assert.Contains("Ctrl+Alt+Shift+C", arrange.ToolTip.ToString());
                    var custom = new LayoutSettings(); custom.Hotkeys["ToggleCollapsed"] = "Ctrl+Alt+Q";
                    panel.RefreshSettings(custom);
                    Assert.Equal("접어서 정돈\nCtrl+Alt+Q", arrange.ToolTip);
                    Assert.Equal("모두 펼치기\nCtrl+Alt+Q", expand.ToolTip);
                    ((IToggleProvider)new ToggleButtonAutomationPeer(chips[1])).Toggle();
                    Assert.Equal("#FFF2B3", panel.SelectedColor);
                    Assert.Single(chips, c => c.IsChecked == true);
                    ((IToggleProvider)new ToggleButtonAutomationPeer(chips[1])).Toggle();
                    Assert.True(chips[1].IsChecked);
                    var undo = new Button { Content = "실행 취소" }; panel.SetUndoContent(undo);
                    panel.ShowStatus("완료"); Assert.Contains(undo, Descendants(panel));
                    panel.SetUndoContent(null); Assert.DoesNotContain(undo, Descendants(panel));
                    buttons.Single(b => Equals(b.Content, "선택 색상 펼치기")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.False(collapsed); Assert.Equal("#FFF2B3", selected);
                    string? command = null; panel.CommandRequested += value => command = value;
                    buttons.Single(b => Equals(b.Content, "접어서 정돈")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal("Arrange", command);
                    buttons.Single(b => Equals(b.Content, "모두 펼치기")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal("ExpandAll", command);
                    panel.SetHasVisibleNotes(false); Assert.Contains(buttons, b => Equals(b.Content, "모두 보이기"));
                    panel.SetHasVisibleNotes(true); Assert.Contains(buttons, b => Equals(b.Content, "모두 숨기기"));
                    Assert.DoesNotContain(buttons, b => Equals(b.Content, "이전 배치로"));
                    Assert.Empty(Descendants(panel).OfType<Slider>());
                    panel.ShowStatus("저장 실패", true); Assert.Contains(Descendants(panel).OfType<TextBlock>(), b => b.Text == "저장 실패");
                    int changes = 0; panel.PreferencesChanged += () => changes++;
                    panel.RefreshSettings(new LayoutSettings { OverlayOpacity = .3, OverlayTopmost = false });
                    Assert.Equal(0, changes); Assert.Equal(.3, panel.Opacity); Assert.False(panel.Topmost); Assert.Null(panel.SelectedColor);
                }
                finally { panel.AllowClose = true; panel.Close(); }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void ExpandedPanelRendersChipsWithoutClippingAtScaledResolution(double scale)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var panel = new OverlayPanelWindow(new LayoutSettings());
                try
                {
                    panel.SetNotes(Enumerable.Range(0, 123).Select(i => new Note { Color = NoteColors.Values[i % 6] }));
                    Descendants(panel).OfType<Expander>().Single().IsExpanded = true;
                    var root = (FrameworkElement)panel.Content;
                    root.Measure(new Size(280, double.PositiveInfinity));
                    root.Arrange(new Rect(0, 0, 280, root.DesiredSize.Height)); root.UpdateLayout();
                    var chips = Descendants(panel).OfType<ToggleButton>().Where(c => AutomationProperties.GetName(c).Contains("보이는 메모")).ToArray();
                    ((IToggleProvider)new ToggleButtonAutomationPeer(chips[2])).Toggle();
                    root.UpdateLayout();
                    Assert.Equal(NoteColors.Values[1], panel.SelectedColor);
                    Assert.Single(chips, c => c.IsChecked == true);
                    foreach (var chip in chips)
                    {
                        Assert.True(chip.IsTabStop);
                        Assert.True(chip.ActualWidth >= 50);
                        var stack = Assert.IsType<StackPanel>(chip.Content);
                        Assert.True(stack.ActualWidth <= chip.ActualWidth);
                        Assert.True(stack.ActualHeight <= chip.ActualHeight);
                    }
                    var selectedBorder = Assert.IsType<Border>(chips[2].Template.FindName("surface", chips[2]));
                    Assert.Equal(Color.FromRgb(0x42, 0x6f, 0xa6), Assert.IsType<SolidColorBrush>(selectedBorder.BorderBrush).Color);
                    Assert.All(Descendants(panel).OfType<Button>(), b => Assert.False(string.IsNullOrWhiteSpace(b.ToolTip?.ToString())));
                    var image = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * scale), (int)Math.Ceiling(root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    image.Render(root);
                    Assert.Equal((int)Math.Ceiling(280 * scale), image.PixelWidth);
                    string? output = Environment.GetEnvironmentVariable("OMNIMEMO_TEST_RENDER_DIR");
                    if (!string.IsNullOrEmpty(output))
                    {
                        Directory.CreateDirectory(output);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                        using var stream = File.Create(Path.Combine(output, $"panel-{scale * 100:0}.png")); encoder.Save(stream);
                    }
                }
                finally { panel.AllowClose = true; panel.Close(); }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Panel rendering timed out.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
