using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Memoit;
using Memoit.Models;
using Memoit.Services;
using Memoit.ViewModels;
using Memoit.Views;

internal sealed class VerificationApp : App
{
    private readonly string output;
    private readonly List<object> measurements = [];
    private readonly List<string> checks = [];
    private readonly string data;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private Dictionary<Guid, NoteViewModel> Notes => (Dictionary<Guid, NoteViewModel>)Field("notes")!;
    private Dictionary<Guid, NoteWindow> NoteWindows => (Dictionary<Guid, NoteWindow>)Field("windows")!;
    private NoteStore Store => (NoteStore)Field("store")!;
    private Window? sentinel;
    private TextBox? sentinelEditor;
    private int disabledEvents;

    public VerificationApp(string output)
    {
        this.output = Path.GetFullPath(output);
        data = Path.Combine(Path.GetDirectoryName(this.output)!, "arrangement-data-" + Guid.NewGuid().ToString("N"));
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    // Run the real command handlers and windows without production startup, personal data or hotkeys.
    protected override async void OnStartup(StartupEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(data);
            SetField("dataDirectory", data);
            SetField("store", new NoteStore(Path.Combine(data, "notes.db")));
            await Store.InitializeAsync();
            sentinelEditor = new TextBox { Text = "Verification keyboard focus sentinel", Margin = new Thickness(10) };
            sentinel = new Window { Title = "OmniMemo verification — focus sentinel", Width = 340, Height = 90,
                Content = sentinelEditor, Topmost = true };
            sentinel.Show();
            foreach (bool panel in new[] { false, true })
            {
                await Populate(100, true, panel);
                await Measure("Arrange", 100, panel, "tiles");
                await VerifyIndividual();
                await Populate(20, false, panel);
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    await Measure("CollapseAll", 20, panel, $"expanded-cycle-{repeat + 1}");
                    await Measure("ExpandAll", 20, panel, $"expanded-cycle-{repeat + 1}");
                }
            }
            await Populate(100, false, false);
            await Measure("ExpandAll", 100, false, "overflow-pages");
            await VerifyPageLifecycle();
            checks.Add("No note was disabled by a bulk command; sentinel keyboard and foreground focus stayed unchanged.");
            WriteResult(true, null);
            Console.WriteLine(output);
            Shutdown(0);
        }
        catch (Exception ex)
        {
            WriteResult(false, ex.ToString());
            Console.Error.WriteLine(ex);
            Shutdown(1);
        }
        finally
        {
            foreach (var window in NoteWindows.Values.ToArray()) { window.AllowClose = true; window.Close(); }
            if (Field("overlayPanel") is OverlayPanelWindow overlay) { overlay.AllowClose = true; overlay.Close(); }
            sentinel?.Close();
        }
    }

    private async Task Populate(int count, bool collapsed, bool panel)
    {
        typeof(App).GetMethod("ClearPageSession", Private)!.Invoke(this, null);
        foreach (var window in NoteWindows.Values) { window.AllowClose = true; window.Close(); }
        foreach (var vm in Notes.Values) vm.Dispose();
        NoteWindows.Clear(); Notes.Clear();
        if (Field("overlayPanel") is OverlayPanelWindow previous) { previous.AllowClose = true; previous.Close(); }
        SetField("overlayPanel", null);
        var monitor = MonitorCatalog.All().First();
        var settings = new LayoutSettings { Monitors = new() { [monitor.Id] = new MonitorLayout { Sort = LayoutSort.Title } } };
        SetField("layoutSettings", settings);
        for (int i = 0; i < count; i++)
        {
            var note = new Note { Body = $"검증 메모 {count - i:D3}\n입력 본문", IsCollapsed = collapsed, Width = 240, Height = 180,
                Left = monitor.WorkArea.Left / monitor.Scale + 90 + (i % 10) * 42,
                Top = monitor.WorkArea.Top / monitor.Scale + 120 + (i / 10) * 42,
                CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(i), IsPinned = i % 4 == 0 };
            var vm = new NoteViewModel(note, Store.SaveAsync);
            var window = new NoteWindow(vm) { ShowActivated = false };
            window.IsEnabledChanged += (_, args) => { if (args.NewValue is false) disabledEvents++; };
            Notes.Add(note.Id, vm); NoteWindows.Add(note.Id, window);
            window.Show();
        }
        await Store.SaveManyAsync(Notes.Values.Select(vm => vm.Snapshot).ToArray());
        if (panel)
        {
            var overlay = new OverlayPanelWindow(settings) { ShowActivated = false };
            SetField("overlayPanel", overlay); overlay.Show();
        }
        await RenderSettled();
        sentinel!.Activate(); sentinelEditor!.Focus();
        await RenderSettled();
        Require(sentinelEditor.IsKeyboardFocused, "Sentinel focus could not be established before the command.");
    }

    private async Task Measure(string command, int count, bool panel, string scenario)
    {
        int disabledBefore = disabledEvents;
        var foreground = GetForegroundWindow();
        var watch = Stopwatch.StartNew();
        var task = (Task)typeof(App).GetMethod("RunArrangementAsync", Private)!.Invoke(this, [command, false])!;
        double synchronousReturnMs = watch.Elapsed.TotalMilliseconds;
        await RenderSettled();
        double renderSettledMs = watch.Elapsed.TotalMilliseconds;
        await task;
        double completedMs = watch.Elapsed.TotalMilliseconds;
        Require(disabledEvents == disabledBefore, "A bulk command disabled a note window.");
        Require(sentinelEditor!.IsKeyboardFocused && GetForegroundWindow() == foreground, "A bulk command stole keyboard or foreground focus.");
        Require(Notes.Values.All(vm => !vm.IsDirty), "Bulk persistence failed or left dirty notes: " + string.Join(";", Notes.Values.Where(v => v.IsDirty).Select(v => v.SaveStatus)));
        bool collapsed = command != "ExpandAll";
        Require(NoteWindows.Values.All(w => w.IsCollapsed == collapsed), "Bulk command did not update all note states.");
        var settings = (LayoutSettings)Field("layoutSettings")!;
        var savedSettings = LayoutSettings.Load(Path.Combine(data, "layout.json"));
        Require(settings.ArrangementOrder.All(p => savedSettings.ArrangementOrder[p.Key].SequenceEqual(p.Value)), "Saved arrangement order differs after reload.");
        var timing = typeof(App).GetProperty("LastArrangementTiming", Private)!.GetValue(this)!;
        double Value(string name) => (double)timing.GetType().GetProperty(name)!.GetValue(timing)!;
        measurements.Add(new { command, count, panel, scenario, calculationMs = Value("CalculationMs"), moveMs = Value("MoveMs"),
            saveMs = Value("SaveMs"), synchronousReturnMs, renderSettledMs, completedMs,
            within200Ms = renderSettledMs <= 200 });
        if (command == "ExpandAll")
        {
            var visible = NoteWindows.Where(p => p.Value.IsVisible).ToArray();
            VerifyNoOverlap(visible.Select(p => p.Value));
            Require(Notes.Values.All(n => n.Snapshot.IsVisible), "Paging persisted a hidden state.");
            var firstIds = visible.Select(p => p.Key).ToHashSet();
            var monitors = MonitorCatalog.All();
            foreach (var monitor in monitors)
                typeof(App).GetMethod("ChangeExpandedPage", Private)!.Invoke(this, [monitor.Id, 1]);
            var next = NoteWindows.Where(p => p.Value.IsVisible).ToArray();
            VerifyNoOverlap(next.Select(p => p.Value));
            if (firstIds.Count < count)
                Require(!firstIds.SetEquals(next.Select(p => p.Key)), "Next page did not replace the visible group.");
            Require(Notes.Values.All(n => n.Snapshot.IsVisible), "Page switch altered logical visibility.");
            var absent = NoteWindows.FirstOrDefault(p => !p.Value.IsVisible);
            if (absent.Value is not null)
            {
                typeof(App).GetMethod("RevealPage", Private)!.Invoke(this, [absent.Key]);
                Require(absent.Value.IsVisible, "List reveal did not show the target page.");
            }
            var persisted = await Store.LoadAsync();
            Require(persisted.Where(n => Notes.ContainsKey(n.Id)).All(n => n.IsVisible), "Persisted visibility differs after paging.");
            checks.Add($"{count} notes: non-overlapping pages, next/reveal, logical and persisted visibility preserved (panel={panel}).");
        }
    }

    private static void VerifyNoOverlap(IEnumerable<NoteWindow> windows)
    {
        var bounds = windows.Select(WindowPlacement.GetBounds).ToArray();
        for (int i = 0; i < bounds.Length; i++)
            for (int j = i + 1; j < bounds.Length; j++)
            {
                var intersection = Rect.Intersect(bounds[i], bounds[j]);
                Require(intersection.IsEmpty || intersection.Width < 1 || intersection.Height < 1, "Visible expanded notes overlap.");
            }
    }
    private async Task VerifyPageLifecycle()
    {
        Require(NoteWindows.Values.Count(w => w.IsVisible) < 100, "Overflow case did not paginate.");
        var first = NoteWindows.First(p => p.Value.IsVisible);
        first.Value.Editor.Text = "Updated while paged";
        await Notes[first.Key].FlushAsync();
        var dragged = WindowPlacement.GetBounds(first.Value); dragged.Offset(-20, 15);
        WindowPlacement.Move(first.Value, dragged.Location);
        var monitor = MonitorCatalog.All().First();
        typeof(App).GetMethod("ChangeExpandedPage", Private)!.Invoke(this, [monitor.Id, 1]);
        typeof(App).GetMethod("RevealPage", Private)!.Invoke(this, [first.Key]);
        Require(Near(dragged, WindowPlacement.GetBounds(first.Value)), "Paging overwrote manual drag.");
        Require(Notes[first.Key].Body == "Updated while paged", "Paging overwrote input.");
        var hidden = NoteWindows.First(p => !p.Value.IsVisible);
        Notes[hidden.Key].SetVisible(false); await Notes[hidden.Key].FlushAsync();
        typeof(App).GetMethod("RefreshList", Private)!.Invoke(this, null);
        await (Task)typeof(App).GetMethod("RunArrangementAsync", Private)!.Invoke(this, ["CollapseAll", false])!;
        Require(NoteWindows.Count(p => p.Value.IsVisible && p.Value.IsCollapsed) == 99, "Collapse omitted off-page notes or revealed hidden note.");
        await (Task)typeof(App).GetMethod("SetAllVisibleAsync", Private)!.Invoke(this, [true])!;
        Require(Notes.Values.All(n => n.Snapshot.IsVisible), "ShowAll did not include hidden note.");
        Require(NoteWindows.Values.Count(w => w.IsVisible && w.IsCollapsed) == 99, "ShowAll changed collapsed states.");
        await (Task)typeof(App).GetMethod("RunArrangementAsync", Private)!.Invoke(this, ["ExpandAll", false])!;
        await (Task)typeof(App).GetMethod("SetAllVisibleAsync", Private)!.Invoke(this, [false])!;
        await (Task)typeof(App).GetMethod("SetAllVisibleAsync", Private)!.Invoke(this, [true])!;
        Require(NoteWindows.Values.Count(w => w.IsVisible) < 100, "ShowAll did not restore pages.");
        Require(((LayoutSettings)Field("layoutSettings")!).PagedExpansionActive, "Restart marker missing.");
        typeof(App).GetMethod("ClearPageSession", Private)!.Invoke(this, null);
        foreach (var w in NoteWindows.Values) { w.ShowActivated = false; w.Show(); }
        await (Task)typeof(App).GetMethod("RunArrangementAsync", Private)!.Invoke(this, ["RestorePages", false])!;
        Require(NoteWindows.Values.Count(w => w.IsVisible) < 100, "Restart pages not rebuilt.");
        VerifyNoOverlap(NoteWindows.Values.Where(w => w.IsVisible));
        typeof(App).GetMethod("CreateList", Private)!.Invoke(this, null);
        typeof(App).GetMethod("ShowList", Private)!.Invoke(this, [false]);
        var list = (Memoit.Views.MainWindow)Field("list")!;
        ((Button)list.FindName("ActiveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((Button)list.FindName("TrashButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var trash = (TrashWindow)Field("trashWindow")!;
        Require(trash.IsVisible, "Trash button did not open separate window.");
        ((Button)list.FindName("TrashButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(ReferenceEquals(trash, Field("trashWindow")), "Duplicate trash window created.");
        trash.Close(); Require(Field("trashWindow") is null, "Trash close retained stale window.");
        list.AllowClose = true; list.Close();
        checks.Add("100-note overflow: paging, manual input/drag, hidden exclusion, global collapse/show, restart rebuild, list refresh and trash open/reuse/close verified.");
    }
    private async Task VerifyIndividual()
    {
        var window = NoteWindows.Values.First();
        var work = WindowPlacement.GetWorkArea(window);
        WindowPlacement.Move(window, new Point(work.Left + 100, work.Top + 100));
        var tile = WindowPlacement.GetBounds(window);
        window.ToggleCollapsed(false);
        var expanded = WindowPlacement.GetBounds(window);
        Require(Math.Abs(expanded.Left - tile.Left) < 2 && Math.Abs(expanded.Top - tile.Top) < 2, "Individual expand jumped from clicked tile location.");
        window.ToggleCollapsed(false);
        Require(Near(tile, WindowPlacement.GetBounds(window)), "Individual collapse did not return to tile location.");
        await NoteViewModel.FlushManyAsync(Notes.Values.ToArray(), Store.SaveManyAsync);
        checks.Add("Individual expansion used tile origin and collapse restored that tile origin.");
    }

    private async Task RenderSettled()
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        int result = DwmFlush();
        if (result < 0) Marshal.ThrowExceptionForHR(result);
    }

    private void WriteResult(bool success, string? error)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { success, error, dataDirectory = data,
            measurements, checks, monitors = MonitorCatalog.All(),
            limitations = "In-process real WPF integration; does not prove absence of visual white frames or physical keyboard/IME input. RenderSettled includes dispatcher and DWM synchronization, not pixel capture." }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private object? Field(string name) => typeof(App).GetField(name, Private)!.GetValue(this);
    private void SetField(string name, object? value) => typeof(App).GetField(name, Private)!.SetValue(this, value);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static bool Near(Rect a, Rect b) => Math.Abs(a.X - b.X) < 2 && Math.Abs(a.Y - b.Y) < 2 && Math.Abs(a.Width - b.Width) < 2 && Math.Abs(a.Height - b.Height) < 2;
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
}

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string output = args.Length == 2 && args[0] == "--output" ? args[1] : "artifacts/verification/arrangement-1.7.json";
        var app = new VerificationApp(output);
        return app.Run();
    }
}
