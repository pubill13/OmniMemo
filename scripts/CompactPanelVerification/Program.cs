using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Memoit;
using Memoit.Models;
using Memoit.Services;
using Memoit.ViewModels;
using Memoit.Views;

internal sealed class CompactVerificationApp(string output, bool interactive) : App
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<string> checks = [];
    private readonly string outputPath = Path.GetFullPath(output);
    private Dictionary<Guid, NoteViewModel> Notes => (Dictionary<Guid, NoteViewModel>)Field("notes")!;
    private Dictionary<Guid, NoteWindow> NoteWindows => (Dictionary<Guid, NoteWindow>)Field("windows")!;
    private NoteStore Store => (NoteStore)Field("store")!;
    protected override async void OnStartup(StartupEventArgs e)
    {
        string data = Path.Combine(Path.GetDirectoryName(outputPath)!, "compact-data-" + Guid.NewGuid().ToString("N"));
        string? error = null;
        try
        {
            Directory.CreateDirectory(data);
            SetField("dataDirectory", data);
            SetField("store", new NoteStore(Path.Combine(data, "notes.db")));
            SetField("backupAttempt", DateOnly.FromDateTime(DateTime.Now));
            SetField("layoutSettings", new LayoutSettings { OverlayLeft = 600, OverlayTop = 120,
                OverlayTopmost = true, OverlayColor = NoteColors.Values[1] });
            await Store.InitializeAsync();
            Call("CreateList"); Call("CreateTray");
            for (int i = 0; i < (interactive ? 3 : 12); i++)
            {
                var vm = (NoteViewModel)Call("AddModel", new Note { Body = $"미니패널 검증 {i + 1}\n개인 데이터와 분리된 테스트 메모", Width = interactive ? 250 : 900,
                    Height = interactive ? 180 : 700, Left = 30 + i * 45, Top = 30 + i * 25 }, true)!;
                Call("ShowNote", vm, false);
            }
            Require(await NoteViewModel.FlushManyAsync(Notes.Values, Store.SaveManyAsync), "Initial save failed.");
            Call("ShowOverlayPanel", true);
            var panel = (OverlayPanelWindow)Field("overlayPanel")!;
            await Settle();
            if (interactive)
            {
                panel.ShowInTaskbar = true; panel.Title = "OmniMemo 1.13.0 패널 검증";
                foreach (var window in NoteWindows.Values) window.ShowInTaskbar = true;
                await Task.Delay(TimeSpan.FromMinutes(5));
                Require(await NoteViewModel.FlushManyAsync(Notes.Values, Store.SaveManyAsync), "Interactive save failed.");
                checks.Add("Interactive session ended; manual observations recorded separately.");
                return;
            }
            double left = panel.Left, top = panel.Top;
            string? color = panel.SelectedColor;
            panel.SetCompact(true); await Settle();
            Require(panel.IsCompact && Math.Abs(panel.ActualWidth - 240) < .1 && Math.Abs(panel.ActualHeight - 88) < .1,
                $"Unexpected compact dimensions {panel.ActualWidth} x {panel.ActualHeight}.");
            Require(panel.Left == left && panel.Top == top && panel.Topmost && panel.SelectedColor == color, "Compact transition changed position/pin/color.");
            var saved = LayoutSettings.Load(Path.Combine(data, "layout.json"));
            Require(saved.OverlayCompact && saved.OverlayTopmost && saved.OverlayColor == color, "Compact preferences were not persisted.");
            var restored = new OverlayPanelWindow(saved);
            try { Require(restored.IsCompact && restored.SelectedTopmost && restored.SelectedColor == color, "Fresh panel did not restore mode/pin/color."); }
            finally { restored.AllowClose = true; restored.Close(); }
            Render(panel, "compact");
            panel.SetCompact(false); await Settle();
            Require(!panel.IsCompact && panel.Width == 280 && panel.Left == left && panel.Top == top && panel.Topmost, "Expanded transition lost state.");
            Render(panel, "panel");
            checks.Add("240x88 compact layout, same-window round trip, position/pin/color, JSON preferences and fresh-instance restoration.");

            foreach (bool compact in new[] { false, true })
            {
                panel.SetCompact(compact); panel.Hide();
                var tray = (System.Windows.Forms.NotifyIcon)Field("tray")!;
                typeof(System.Windows.Forms.NotifyIcon).GetMethod("OnDoubleClick", Private)!.Invoke(tray, [EventArgs.Empty]);
                await Settle();
                Require(ReferenceEquals(panel, Field("overlayPanel")) && panel.IsVisible && panel.IsCompact == compact,
                    "Tray double-click failed to show the existing panel in its current mode.");
                Require(GetForegroundWindow() == new WindowInteropHelper(panel).Handle, "Tray double-click did not foreground panel.");
                typeof(System.Windows.Forms.NotifyIcon).GetMethod("OnDoubleClick", Private)!.Invoke(tray, [EventArgs.Empty]);
                Require(panel.IsVisible, "Repeated tray double-click hid panel.");
            }
            Require(((Memoit.Views.MainWindow)Field("list")!).IsVisible == false && Field("layoutPanel") is null, "Tray double-click opened list/settings.");
            checks.Add("Real NotifyIcon DoubleClick event opens/foregrounds existing panel in both modes and never toggles it closed.");

            panel.SetCompact(false);
            await (Task)Call("RunArrangementAsync", "ArrangeExpanded", false)!;
            Require(Field("pageWindow") is not Window normalPager || !normalPager.IsVisible, "Normal panel also displayed standalone pager.");
            panel.SetCompact(true); await Settle();
            Require(Field("pageWindow") is Window { IsVisible: true }, "Mini panel lost page navigation.");
            panel.SetCompact(false); await Settle();
            Require(Field("pageWindow") is Window { IsVisible: false }, "Normal panel did not reclaim page navigation.");
            checks.Add("Paged expansion uses standalone pager in mini mode and panel controls in normal mode.");

            var sample = Notes.Values.First().Snapshot;
            Call("ShowDeleteUndo", (IReadOnlyList<TrashChange>)new[] { new TrashChange(sample, DateTimeOffset.UtcNow) }, false, new Point(100, 100));
            var notice = (UndoNotice)Field("undoNotice")!;
            notice.Advance(TimeSpan.FromSeconds(7), false);
            var remaining = typeof(UndoNotice).GetField("remaining", Private)!;
            var before = (TimeSpan)remaining.GetValue(notice)!;
            panel.SetCompact(true); await Settle();
            Require(ReferenceEquals(notice, Field("undoNotice")), "Compact transition replaced undo notice.");
            Require((TimeSpan)remaining.GetValue(notice)! <= before, "Compact transition reset undo timeout.");
            Require(Field("undoWindow") is Window { IsVisible: true }, "Compact transition hid undo action.");
            panel.SetCompact(false); await Settle();
            Require(ReferenceEquals(notice, Field("undoNotice")), "Return transition replaced undo notice.");
            notice.Advance(TimeSpan.FromSeconds(4), false);
            Require(Field("undoNotice") is null, "Undo timeout did not expire after reparenting.");
            checks.Add("Undo notice reparenting preserves the same instance and remaining expiry time across mode switches.");
        }
        catch (Exception ex) { error = ex.ToString(); }
        finally
        {
            Call("ClearDeleteUndo");
            foreach (var window in NoteWindows.Values.ToArray()) { window.AllowClose = true; window.Close(); }
            if (Field("pageWindow") is PagerWindow pager) { pager.AllowClose = true; pager.Close(); }
            if (Field("overlayPanel") is OverlayPanelWindow panel) { panel.AllowClose = true; panel.Close(); }
            if (Field("list") is Memoit.Views.MainWindow list) { list.AllowClose = true; list.Close(); }
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, JsonSerializer.Serialize(new { success = error is null, error, dataDirectory = data, checks,
                limitations = "WPF integration with real NotifyIcon event invocation, not physical notification-area double-click. Rendered at 100/150/200 percent; physical monitor DPI switching and IME are not covered." }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(File.ReadAllText(outputPath));
            Shutdown(error is null ? 0 : 1);
        }
    }
    private void Render(OverlayPanelWindow panel, string name)
    {
        panel.UpdateLayout();
        foreach (double scale in new[] { 1d, 1.5, 2 })
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(panel.ActualWidth * scale), (int)Math.Ceiling(panel.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render((Visual)panel.Content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(Path.GetDirectoryName(outputPath)!, $"{name}-{scale * 100:0}.png"));
            encoder.Save(stream);
        }
    }
    private Task Settle() => Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task;
    private object? Field(string name) => typeof(App).GetField(name, Private)!.GetValue(this);
    private void SetField(string name, object? value) => typeof(App).GetField(name, Private)!.SetValue(this, value);
    private object? Call(string name, params object[] args) => typeof(App).GetMethod(name, Private)!.Invoke(this, args);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        int outputIndex = Array.IndexOf(args, "--output");
        string output = outputIndex >= 0 && outputIndex + 1 < args.Length ? args[outputIndex + 1] : "artifacts/verification/compact-panel-1.13.0.json";
        return new CompactVerificationApp(output, args.Contains("--interactive")) { ShutdownMode = ShutdownMode.OnExplicitShutdown }.Run();
    }
}
