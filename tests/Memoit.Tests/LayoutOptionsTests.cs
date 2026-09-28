using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Memoit.Services;
using Memoit.Views;
using Xunit;

namespace Memoit.Tests;

[Collection("WPF")]
public sealed class LayoutOptionsTests
{
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
                Assert.Equal(0, Field<ComboBox>(window, "target").SelectedIndex);
                Assert.Equal("정렬 대상", System.Windows.Automation.AutomationProperties.GetName(Field<ComboBox>(window, "target")));
                Field<ComboBox>(window, "target").SelectedIndex = 1;
                Field<ComboBox>(window, "monitorBox").SelectedIndex = 1;
                Assert.Equal(0, Field<ComboBox>(window, "target").SelectedIndex);
                Field<TextBox>(window, "x").Text = "456";
                Field<ComboBox>(window, "monitorBox").SelectedIndex = 0;
                Assert.Equal("123", Field<TextBox>(window, "x").Text);
                Assert.Equal(1, Field<ComboBox>(window, "target").SelectedIndex);
                Assert.True(Field<LayoutSettings>(window, "draft").Monitors["A"].IncludeExpanded);
                Assert.False(Field<LayoutSettings>(window, "draft").Monitors["B"].IncludeExpanded);
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
                bool called = false; window.ApplyRequested += _ => called = true;
                Field<TextBox>(window, "gap").Text = "-1";
                typeof(LayoutOptionsWindow).GetMethod("Dispatch", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [false]);
                Assert.False(called);
                Assert.NotEmpty(Field<TextBlock>(window, "status").Text);
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
