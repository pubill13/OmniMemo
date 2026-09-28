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
                    var buttons = content.Children.OfType<StackPanel>().SelectMany(row => row.Children.OfType<Button>()).ToArray();
                    buttons.Single(b => Equals(b.Content, "펼치기")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.False(collapsed); Assert.Equal("#FFF2B3", selected);
                    string? command = null; panel.CommandRequested += value => command = value;
                    buttons.Single(b => Equals(b.Content, "가로")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal("ShapeHorizontal", command);
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
