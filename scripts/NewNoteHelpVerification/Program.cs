using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Memoit;
using Memoit.Models;
using Memoit.Services;
using Memoit.ViewModels;
using Memoit.Views;

internal sealed class VerificationApp(string output, bool interactive) : App
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<string> checks = [];
    private readonly string outputPath = Path.GetFullPath(output);
    private Dictionary<Guid, NoteViewModel> Notes => (Dictionary<Guid, NoteViewModel>)Field("notes")!;
    private Dictionary<Guid, NoteWindow> NoteWindows => (Dictionary<Guid, NoteWindow>)Field("windows")!;
    protected override async void OnStartup(StartupEventArgs e)
    {
        string data = Path.Combine(Path.GetDirectoryName(outputPath)!, "new-note-help-data-" + Guid.NewGuid().ToString("N"));
        string? error = null;
        LayoutOptionsWindow? help = null;
        try
        {
            Directory.CreateDirectory(data);
            SetField("dataDirectory", data);
            var store = new NoteStore(Path.Combine(data, "notes.db"));
            SetField("store", store);
            SetField("backupAttempt", DateOnly.FromDateTime(DateTime.Now));
            await store.InitializeAsync();
            var monitors = MonitorCatalog.All();
            var cursor = System.Windows.Forms.Cursor.Position;
            var monitor = NotePlacement.SelectMonitor(null, new Point(cursor.X, cursor.Y), monitors);
            var settings = new LayoutSettings { Monitors = new() { [monitor.Id] = new MonitorLayout {
                X = Math.Max(0, monitor.WorkArea.Width / monitor.Scale - 44), Y = 40 } } };
            SetField("layoutSettings", settings);
            Call("CreateList");
            var source = await New(null, NoteColors.Values[1]);
            var firstBounds = WindowPlacement.GetBounds(source);
            Require(Math.Abs(firstBounds.Right - monitor.WorkArea.Right) < 3, "Right anchor did not create first note at right edge.");
            Require(MonitorCatalog.ForWindow(source).Id == monitor.Id, "Cursor fallback chose wrong monitor.");
            Require(((NoteViewModel)source.DataContext).Snapshot.Color == NoteColors.Values[1], "Panel selected color lost.");
            var oldBounds = new Dictionary<Guid, Rect>();
            for (int i = 0; i < 5; i++)
            {
                foreach (var pair in NoteWindows) oldBounds[pair.Key] = WindowPlacement.GetBounds(pair.Value);
                var next = await New(source, NoteColors.Values[2]);
                double offset = (i + 1) * 24 * monitor.Scale;
                var expected = firstBounds; expected.Offset(-offset, offset);
                if (monitor.WorkArea.Contains(expected))
                    Require((WindowPlacement.GetBounds(next).Location - expected.Location).Length < 2, "Visible note did not cascade by 24 DIP.");
                Require(MonitorCatalog.ForWindow(next).Id == monitor.Id, "Source monitor inheritance lost.");
                Require(((NoteViewModel)next.DataContext).Snapshot.Color == NoteColors.Values[1], "Source color inheritance lost.");
                foreach (var pair in oldBounds)
                    Require(WindowPlacement.GetBounds(NoteWindows[pair.Key]) == pair.Value, "New note moved an existing note.");
                Require(monitor.WorkArea.Contains(WindowPlacement.GetBounds(next)), "New note escaped work area.");
            }
            Call("RecordNoteActivation", source);
            var inherited = await New(null, null);
            Require(((NoteViewModel)inherited.DataContext).Snapshot.Color == NoteColors.Values[1], "Last-active color inheritance lost.");
            Require(MonitorCatalog.ForWindow(inherited).Id == monitor.Id, "Last-active monitor inheritance lost.");
            checks.Add("Actual App.NewNoteAsync: right anchor, repeated creation, source/panel/last-active color, cursor/source/last-active monitor, unchanged existing bounds.");
            Require(await NoteViewModel.FlushManyAsync(Notes.Values, store.SaveManyAsync), "Save failed.");

            string dataPath = data;
            help = new LayoutOptionsWindow(settings, monitors, dataPath) { ShowInTaskbar = true };
            help.SelectTab(SettingsTab.Help); help.Show();
            await Settle();
            var tabs = Descendants(help).OfType<TabControl>().Single();
            var scroll = (ScrollViewer)((TabItem)tabs.SelectedItem).Content;
            foreach (var size in new[] { new Size(640, 740), new Size(help.MinWidth, help.MinHeight) })
            {
                help.Width = size.Width; help.Height = size.Height;
                scroll.ScrollToTop(); await Settle(); help.UpdateLayout();
                Require(scroll.ViewportHeight >= 44, "Help viewport cannot show two full lines.");
                Require(scroll.ExtentWidth <= scroll.ViewportWidth + 1, "Help has horizontal overflow.");
                Render(help, $"help-{size.Width:0}x{size.Height:0}-top");
                scroll.ScrollToBottom(); await Settle(); help.UpdateLayout();
                Require(scroll.VerticalOffset > 0 && Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 1,
                    "Help could not scroll to bottom.");
                Render(help, $"help-{size.Width:0}x{size.Height:0}-bottom");
            }
            var pathBox = Descendants(scroll).OfType<TextBox>().Single(t => t.Text == dataPath);
            Require(pathBox.IsReadOnly, "Data path must be readonly.");
            pathBox.SelectAll();
            Require(pathBox.SelectedText == dataPath, "Data path cannot be fully selected for copying.");
            pathBox.Copy();
            Require(Clipboard.GetText() == dataPath, "Copied path differs from the data folder.");
            checks.Add("Help default/minimum sizes: no horizontal overflow, scroll to bottom, readonly full data path copied to clipboard; PNG rendering at 100/150/200%.");
            if (interactive)
            {
                help.Width = 640; help.Height = 740; scroll.ScrollToTop();
                help.Title = "OmniMemo 1.13.1 도움말 검증";
                await Task.Delay(TimeSpan.FromMinutes(2));
            }
        }
        catch (Exception ex) { error = ex.ToString(); }
        finally
        {
            if (help is not null) { help.AllowClose = true; help.Close(); }
            foreach (var window in NoteWindows.Values.ToArray()) { window.AllowClose = true; window.Close(); }
            if (Field("list") is Memoit.Views.MainWindow list) { list.AllowClose = true; list.Close(); }
            File.WriteAllText(outputPath, JsonSerializer.Serialize(new { success = error is null, error, dataDirectory = data, checks,
                limitations = "Actual WPF windows and application handlers with isolated data. Help scrolling and clipboard copy are programmatic. Physical mouse clicks, physical multi-monitor and DPI switching are not covered." }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(File.ReadAllText(outputPath));
            Shutdown(error is null ? 0 : 1);
        }
    }
    private async Task<NoteWindow> New(NoteWindow? source, string? color)
    {
        var before = NoteWindows.Keys.ToHashSet();
        var method = typeof(App).GetMethod("NewNoteAsync", Private, null, [typeof(NoteWindow), typeof(string)], null)!;
        await (Task)method.Invoke(this, [source, color])!;
        await Settle();
        return NoteWindows.Single(p => !before.Contains(p.Key)).Value;
    }
    private void Render(Window window, string name)
    {
        foreach (double scale in new[] { 1d, 1.5, 2 })
        {
            var visual = (FrameworkElement)window.Content;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth * scale), (int)Math.Ceiling(visual.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen()) dc.DrawRectangle(new VisualBrush(visual), null, new Rect(0, 0, visual.ActualWidth, visual.ActualHeight));
            bitmap.Render(drawing);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(Path.GetDirectoryName(outputPath)!, $"{name}-{scale * 100:0}.png"));
            encoder.Save(stream);
        }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private Task Settle() => Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task;
    private object? Field(string name) => typeof(App).GetField(name, Private)!.GetValue(this);
    private void SetField(string name, object? value) => typeof(App).GetField(name, Private)!.SetValue(this, value);
    private object? Call(string name, params object[] args) => typeof(App).GetMethod(name, Private)!.Invoke(this, args);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

internal static class Program
{
    [STAThread]
    private static int Main(string[] args) => new VerificationApp(args.FirstOrDefault(a => a != "--interactive") ?? "artifacts/verification/new-note-help-1.13.1.json", args.Contains("--interactive"))
        { ShutdownMode = ShutdownMode.OnExplicitShutdown }.Run();
}
