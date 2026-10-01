using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Memoit.Services;
using Memoit.Views;
using Memoit.Models;
using Xunit;

namespace Memoit.Tests;

[Collection("WPF")]
public sealed class LayoutOptionsTests
{
    [Fact]
    public void PreviewUsesSavedExpandedOrderAndDoesNotMutateNotes()
    {
        Sta(() =>
        {
            var first = new Note { Body = "first", Width = 200, Height = 150, IsCollapsed = true, CreatedAt = DateTimeOffset.UnixEpoch };
            var second = new Note { Body = "second", Width = 200, Height = 150, IsCollapsed = true, CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(1) };
            var settings = new LayoutSettings { ArrangementOrder = new() { ["A"] = [second.Id, first.Id] } };
            var monitor = new MonitorDescriptor("A", "화면", new Rect(-1920, 0, 1920, 1080), 1.5);
            var window = new LayoutOptionsWindow(settings, [monitor], previewNotes: _ => [first, second]);
            try
            {
                Field<ComboBox>(window, "previewMode").SelectedIndex = 1;
                var expected = NoteArrangement.ArrangeExpanded([first with { IsCollapsed = false }, second with { IsCollapsed = false }],
                    monitor.WorkArea, new MonitorLayout(), scale: monitor.Scale, orderedIds: [second.Id, first.Id]);
                var canvas = Field<Canvas>(window, "preview");
                var ratio = canvas.Width / monitor.WorkArea.Width;
                foreach (var note in new[] { first, second })
                {
                    var tile = canvas.Children.OfType<Border>().Single(b => Equals(b.ToolTip, note.Title));
                    Assert.Equal((expected.Bounds[note.Id].Left - monitor.WorkArea.Left) * ratio, Canvas.GetLeft(tile), 6);
                    Assert.Equal(expected.Bounds[note.Id].Width * ratio, tile.Width, 6);
                }
                Assert.True(first.IsCollapsed); Assert.True(second.IsCollapsed);
                Field<ComboBox>(window, "shape").SelectedIndex = (int)LayoutShape.Horizontal;
                Assert.Equal(Visibility.Collapsed, Field<StackPanel>(window, "columnFields").Visibility);
                Field<TextBox>(window, "x").Text = "1.7e308";
                Assert.Empty(canvas.Children);
                Assert.Contains("너무", Field<TextBlock>(window, "previewStatus").Text);
            }
            finally { window.AllowClose = true; window.Close(); }
        });
    }

    [Fact]
    public void EmptyPreviewIsClearlySampleAndInvalidInputClearsIt()
    {
        Sta(() =>
        {
            var window = new LayoutOptionsWindow(new LayoutSettings(), [new MonitorDescriptor("A", "화면", new Rect(0, 0, 1920, 1080), 1)]);
            try
            {
                Assert.Contains("예시", Field<TextBlock>(window, "previewStatus").Text);
                Assert.Equal(6, Field<Canvas>(window, "preview").Children.Count);
                Field<TextBox>(window, "gap").Text = "oops";
                Assert.Empty(Field<Canvas>(window, "preview").Children);
                Assert.Contains("확인", Field<TextBlock>(window, "previewStatus").Text);
            }
            finally { window.AllowClose = true; window.Close(); }
        });
    }

    [Fact]
    public void MonitorDraftsSurviveSwitchAndDoNotMutateSavedSettings()
    {
        Sta(() =>
        {
            var saved = new LayoutSettings();
            var monitors = new[] { new MonitorDescriptor("A", "첫 화면", new Rect(0, 0, 1920, 1080), 1), new MonitorDescriptor("B", "둘째 화면", new Rect(1920, 0, 1920, 1080), 1) };
            var window = new LayoutOptionsWindow(saved, monitors);
            try
            {
                Field<TextBox>(window, "x").Text = "123";
                Assert.Equal(ExpandedCorner.TopRight, Field<ComboBox>(window, "corner").SelectedItem);
                Assert.Equal("펼친 메모 시작 방향", System.Windows.Automation.AutomationProperties.GetName(Field<ComboBox>(window, "corner")));
                Field<ComboBox>(window, "corner").SelectedItem = ExpandedCorner.BottomLeft;
                Field<ComboBox>(window, "monitorBox").SelectedIndex = 1;
                Assert.Equal(ExpandedCorner.TopRight, Field<ComboBox>(window, "corner").SelectedItem);
                Field<TextBox>(window, "x").Text = "456";
                Field<ComboBox>(window, "monitorBox").SelectedIndex = 0;
                Assert.Equal("123", Field<TextBox>(window, "x").Text);
                Assert.Equal(ExpandedCorner.BottomLeft, Field<ComboBox>(window, "corner").SelectedItem);
                Assert.Equal(ExpandedCorner.BottomLeft, Field<LayoutSettings>(window, "draft").Monitors["A"].ExpandedCorner);
                Assert.Equal(ExpandedCorner.TopRight, Field<LayoutSettings>(window, "draft").Monitors["B"].ExpandedCorner);
                Assert.Empty(saved.Monitors);
            }
            finally { window.AllowClose = true; window.Close(); }
        });
    }
    [Fact]
    public void InvalidLayoutDoesNotDispatchApply()
    {
        Sta(() =>
        {
            var window = new LayoutOptionsWindow(new LayoutSettings(), [new MonitorDescriptor("A", "화면", new Rect(0, 0, 1920, 1080), 1)]);
            try
            {
                bool called = false; window.ApplyRequested += (_, _) => called = true;
                Field<TextBox>(window, "gap").Text = "-1";
                typeof(LayoutOptionsWindow).GetMethod("Dispatch", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                Assert.False(called);
                Assert.NotEmpty(Field<TextBlock>(window, "status").Text);
            }
            finally { window.AllowClose = true; window.Close(); }
        });
    }
    [Fact]
    public void UnifiedSettingsExposeFourTabsAndStartupOnlyOnApply()
    {
        Sta(() =>
        {
            var window = new LayoutOptionsWindow(new LayoutSettings { OverlayOpacity = .55 }, [], "C:/data", true);
            try
            {
                var tabs = Field<TabControl>(window, "tabs");
                Assert.Equal(new[] { "배치", "단축키", "일반", "백업" }, tabs.Items.Cast<TabItem>().Select(t => t.Header));
                window.SelectTab(SettingsTab.General); Assert.Equal(2, tabs.SelectedIndex);
                window.SetAutoStart(false);
                Field<CheckBox>(window, "autoStart").IsChecked = true;
                Assert.DoesNotContain("ToggleAuto", Field<Dictionary<string, TextBox>>(window, "recorders").Keys);
                LayoutSettings? candidate = null; window.ApplyRequested += (value, startup) => { candidate = value; Assert.True(startup); };
                Assert.Null(typeof(LayoutOptionsWindow).GetField("opacity", BindingFlags.Instance | BindingFlags.NonPublic)); window.Left = 0; window.Top = 0;
                typeof(LayoutOptionsWindow).GetMethod("Dispatch", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                Assert.NotNull(candidate); Assert.Equal(.55, candidate.OverlayOpacity);
            }
            finally { window.AllowClose = true; window.Close(); }
        });
    }
    [Fact]
    public void ColumnModeValidatesManualCountAndPreviewIsNotEmbedded()
    {
        Sta(() =>
        {
            var window = new LayoutOptionsWindow(new LayoutSettings(), [new MonitorDescriptor("A", "화면", new Rect(0, 0, 1920, 1080), 1)]);
            try
            {
                Assert.Null(Field<Canvas>(window, "preview").Parent);
                Assert.Equal(6, Field<Dictionary<string, TextBox>>(window, "recorders").Count);
                var mode = Field<ComboBox>(window, "columnMode"); var count = Field<TextBox>(window, "columns");
                Assert.Equal(Visibility.Collapsed, count.Visibility);
                mode.SelectedIndex = 1; count.Text = "0";
                Assert.Contains("확인", Field<TextBlock>(window, "previewStatus").Text);
                count.Text = "3";
                Assert.NotEmpty(Field<Canvas>(window, "preview").Children);
                LayoutSettings? applied = null; window.ApplyRequested += (value, _) => applied = value;
                window.Left = 0; window.Top = 0;
                typeof(LayoutOptionsWindow).GetMethod("Dispatch", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                Assert.Equal(3, applied!.GetMonitor("A").Columns);
                mode.SelectedIndex = 0;
                typeof(LayoutOptionsWindow).GetMethod("Dispatch", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                Assert.Equal(0, applied.GetMonitor("A").Columns);
                mode.SelectedIndex = 1; count.Text = "invalid"; Field<ComboBox>(window, "shape").SelectedIndex = (int)LayoutShape.Horizontal;
                typeof(LayoutOptionsWindow).GetMethod("Dispatch", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                Assert.Equal(LayoutShape.Horizontal, applied.GetMonitor("A").Shape);
                Assert.Equal(0, applied.GetMonitor("A").Columns);
            }
            finally { window.AllowClose = true; window.Close(); }
        });
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    [Fact]
    public void RecorderCancelRestoresDraftGestureAndReleasesCapture()
    {
        Sta(() =>
        {
            var settings = new LayoutSettings();
            var window = new LayoutOptionsWindow(settings, []);
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(LayoutOptionsWindow).GetField("capturing", flags)!.SetValue(window, true);
                typeof(LayoutOptionsWindow).GetField("captureId", flags)!.SetValue(window, "ToggleCurrent");
                typeof(LayoutOptionsWindow).GetField("captureOriginal", flags)!.SetValue(window, "Ctrl+Shift+Space");
                typeof(LayoutOptionsWindow).GetMethod("SetGesture", flags)!.Invoke(window, ["ToggleCurrent", "Ctrl+Alt+X"]);
                bool? capture = null; window.CaptureChanged += value => capture = value;
                typeof(LayoutOptionsWindow).GetMethod("CancelCapture", flags)!.Invoke(window, null);
                Assert.Equal("Ctrl+Shift+Space", Field<LayoutSettings>(window, "draft").Hotkeys["ToggleCurrent"]);
                Assert.False(capture);
            }
            finally { window.AllowClose = true; window.Close(); }
        });
    }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
