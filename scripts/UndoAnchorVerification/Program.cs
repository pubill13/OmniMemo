using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Memoit;
using Memoit.Models;
using Memoit.Services;
using Memoit.ViewModels;
using Memoit.Views;

internal sealed class VerificationApp : App
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly string output = Path.GetFullPath("artifacts/verification/undo-anchor-1.13.3.json");
    private readonly List<string> checks = [];
    private Dictionary<Guid, NoteViewModel> Notes => (Dictionary<Guid, NoteViewModel>)Field("notes")!;
    private Dictionary<Guid, NoteWindow> WindowsById => (Dictionary<Guid, NoteWindow>)Field("windows")!;
    protected override async void OnStartup(StartupEventArgs e)
    {
        string data = Path.Combine(Path.GetDirectoryName(output)!, "undo-anchor-data-" + Guid.NewGuid().ToString("N"));
        string? error = null;
        try
        {
            Directory.CreateDirectory(data);
            Set("dataDirectory", data);
            var store = new NoteStore(Path.Combine(data, "notes.db"));
            Set("store", store);
            Set("backupAttempt", DateOnly.FromDateTime(DateTime.Now));
            await store.InitializeAsync();
            var monitor = MonitorCatalog.All().First();
            var options = new MonitorLayout { X = monitor.WorkArea.Width / monitor.Scale - 44, Y = 40 };
            typeof(MonitorLayout).GetProperty("NewNoteX")!.SetValue(options, 120d);
            typeof(MonitorLayout).GetProperty("NewNoteY")!.SetValue(options, 140d);
            var settings = new LayoutSettings { Monitors = new() { [monitor.Id] = options } };
            settings.Save(Path.Combine(data, "layout.json"));
            Set("layoutSettings", LayoutSettings.Load(Path.Combine(data, "layout.json")));
            Set("lastActiveNoteMonitor", monitor.Id);
            Call("CreateList");
            var first = await NewNote();
            var origin = WindowPlacement.GetBounds(first);
            Require(Math.Abs(origin.Left - monitor.WorkArea.Left - 120 * monitor.Scale) < 2
                && Math.Abs(origin.Top - monitor.WorkArea.Top - 140 * monitor.Scale) < 2, "New note did not use separately saved origin.");
            var second = await NewNote();
            Require((WindowPlacement.GetBounds(second).Location - origin.Location - new Vector(24 * monitor.Scale, 24 * monitor.Scale)).Length < 2,
                "Second note did not cascade from independent origin.");
            Require(WindowPlacement.GetBounds(first) == origin, "Existing note moved.");
            checks.Add("Separate monitor origin survives JSON reload and controls actual creation; second note cascades 24 DIP and first stays fixed.");

            var list = (Memoit.Views.MainWindow)Field("list")!;
            foreach (string context in new[] { "standalone", "panel", "mini", "list" })
            {
                list.Hide();
                if (Field("overlayPanel") is OverlayPanelWindow previous) previous.Hide();
                if (context is "panel" or "mini")
                {
                    Call("ShowOverlayPanel", false);
                    ((OverlayPanelWindow)Field("overlayPanel")!).SetCompact(context == "mini");
                }
                if (context == "list") Call("ShowList", false);
                var note = await NewNote();
                note.ToggleCollapsed(false);
                if (context == "standalone")
                    WindowPlacement.Move(note, new Point(monitor.WorkArea.Right - 36 * monitor.Scale, monitor.WorkArea.Bottom - 36 * monitor.Scale));
                var id = ((NoteViewModel)note.DataContext).Snapshot.Id;
                var before = Notes[id].Snapshot;
                var menu = ((Button)note.FindName("CollapsedTile")).ContextMenu;
                var delete = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "휴지통으로 이동"));
                delete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await Until(() => Field("undoNotice") is not null && !(bool)Field("busy")!);
                Require(!note.IsVisible && Notes[id].Snapshot.DeletedAt is not null, "Menu deletion did not hide/deactivate note.");
                var notice = (UndoNotice)Field("undoNotice")!;
                if (context is "standalone" or "mini")
                {
                    var window = (Window)Field("undoWindow")!;
                    await Settle();
                    CheckWindow(window, notice);
                    CaptureWindow(window, context + "-full-window");
                    RenderNotice(notice, context);
                    notice.ShowFailure(99999999);
                    await Settle();
                    CheckWindow(window, notice);
                    CaptureWindow(window, context + "-failure-full-window");
                }
                else Require(Field("undoWindow") is null && notice.IsVisible, "Embedded undo notice was not visible in host.");
                var button = (Button)typeof(UndoNotice).GetField("undo", Private)!.GetValue(notice)!;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => Field("undoNotice") is null && !(bool)Field("busy")!);
                Require(Notes[id].Snapshot.DeletedAt is null && WindowsById[id].IsVisible && Notes[id].Snapshot.IsCollapsed == before.IsCollapsed,
                    "Undo did not restore note state.");
                checks.Add(context + ": actual tile context-menu delete event and undo button restore succeed; notice remains accessible.");
            }
            using (var notice = new UndoNotice(1))
            {
                bool expired = false; notice.Expired += () => expired = true;
                notice.Advance(TimeSpan.FromSeconds(11), true);
                Require(!expired, "Paused notice expired.");
                notice.Advance(TimeSpan.FromSeconds(9), false);
                Require(!expired, "Notice expired before 10 seconds.");
                notice.Advance(TimeSpan.FromSeconds(2), false);
                Require(expired, "Notice did not expire.");
            }
            checks.Add("10-second expiry and paused timer verified. Native full-window screenshots include title and bottom frame; synthetic 100/150/200% content renders are separate.");
        }
        catch (Exception ex) { error = ex.ToString(); }
        finally
        {
            Call("ClearDeleteUndo");
            foreach (var window in WindowsById.Values.ToArray()) { window.AllowClose = true; window.Close(); }
            if (Field("overlayPanel") is OverlayPanelWindow panel) { panel.AllowClose = true; panel.Close(); }
            if (Field("list") is Memoit.Views.MainWindow list) { list.AllowClose = true; list.Close(); }
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonSerializer.Serialize(new { success = error is null, error, dataDirectory = data, checks,
                limitations = "Context-menu and undo button events are programmatically invoked in real windows. PrintWindow captures actual current-DPI native frame. 150/200% captures are synthetic content renders, not physical DPI switching. No personal database opened." }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(File.ReadAllText(output));
            Shutdown(error is null ? 0 : 1);
        }
    }
    private async Task<NoteWindow> NewNote()
    {
        var before = WindowsById.Keys.ToHashSet();
        var method = typeof(App).GetMethod("NewNoteAsync", Private, null, [typeof(NoteWindow), typeof(string)], null)!;
        await (Task)method.Invoke(this, [null, null])!;
        await Settle();
        return WindowsById.Single(pair => !before.Contains(pair.Key)).Value;
    }
    private static void CheckWindow(Window window, UndoNotice notice)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        Require(GetClientRect(hwnd, out var client), "GetClientRect failed.");
        var point = new NativePoint(); Require(ClientToScreen(hwnd, ref point), "ClientToScreen failed.");
        var clientBounds = new Rect(point.X, point.Y, client.Right, client.Bottom);
        var top = notice.PointToScreen(new Point(0, 0));
        var bottom = notice.PointToScreen(new Point(notice.ActualWidth, notice.ActualHeight));
        Require(clientBounds.Contains(new Rect(top, bottom)), "Undo notice escapes native client area.");
        Require(clientBounds.Bottom - bottom.Y >= 7,
            $"Standalone notice lacks bottom padding: clientBottom={clientBounds.Bottom}, noticeBottom={bottom.Y}, window={WindowPlacement.GetBounds(window)}, notice={notice.ActualWidth}x{notice.ActualHeight}");
        foreach (string field in new[] { "message", "undo" })
        {
            var child = (FrameworkElement)typeof(UndoNotice).GetField(field, Private)!.GetValue(notice)!;
            var rect = new Rect(child.PointToScreen(new Point()), child.PointToScreen(new Point(child.ActualWidth, child.ActualHeight)));
            Require(clientBounds.Contains(rect), field + " escapes native client area.");
        }
        Require(WindowPlacement.GetWorkArea(window).Contains(WindowPlacement.GetBounds(window)), "Undo window escapes work area.");
    }
    private void CaptureWindow(Window window, string name)
    {
        var bounds = WindowPlacement.GetBounds(window);
        using var bitmap = new System.Drawing.Bitmap((int)bounds.Width, (int)bounds.Height);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        var hdc = graphics.GetHdc();
        try { Require(PrintWindow(new WindowInteropHelper(window).Handle, hdc, 2), "PrintWindow failed."); }
        finally { graphics.ReleaseHdc(hdc); }
        bitmap.Save(Path.Combine(Path.GetDirectoryName(output)!, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
    }
    private void RenderNotice(UndoNotice notice, string name)
    {
        foreach (double scale in new[] { 1d, 1.5, 2 })
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(notice.ActualWidth * scale), (int)Math.Ceiling(notice.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(notice), null, new Rect(0, 0, notice.ActualWidth, notice.ActualHeight));
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(Path.GetDirectoryName(output)!, $"{name}-content-{scale * 100:0}.png")); encoder.Save(stream);
        }
    }
    private async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++) await Task.Delay(25);
        Require(condition(), "Timed out waiting for application operation.");
        await Settle();
    }
    private Task Settle() => Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task;
    private object? Field(string name) => typeof(App).GetField(name, Private)!.GetValue(this);
    private void Set(string name, object value) => typeof(App).GetField(name, Private)!.SetValue(this, value);
    private object? Call(string name, params object[] args) => typeof(App).GetMethod(name, Private)!.Invoke(this, args);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
}
internal static class Program
{
    [STAThread] private static int Main() => new VerificationApp { ShutdownMode = ShutdownMode.OnExplicitShutdown }.Run();
}
