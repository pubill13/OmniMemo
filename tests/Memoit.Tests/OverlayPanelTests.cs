using System.Reflection;
using System.Windows;
using System.Windows.Controls;
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
}
