using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Memoit;
using Memoit.Models;
using Memoit.Services;
using Memoit.ViewModels;
using Memoit.Views;

internal sealed class SaveVerificationApp(string output, bool interactive = false) : App
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<string> checks = [];
    private readonly List<object> measurements = [];
    private Dictionary<Guid, NoteViewModel> Notes => (Dictionary<Guid, NoteViewModel>)Field("notes")!;
    private Dictionary<Guid, NoteWindow> NoteWindows => (Dictionary<Guid, NoteWindow>)Field("windows")!;
    private NoteStore Store => (NoteStore)Field("store")!;
    private readonly string outputPath = Path.GetFullPath(output);

    protected override async void OnStartup(StartupEventArgs e)
    {
        string data = Path.Combine(Path.GetDirectoryName(outputPath)!, "save-data-" + Guid.NewGuid().ToString("N"));
        int result = 0;
        string? error = null;
        try
        {
            Directory.CreateDirectory(data);
            SetField("dataDirectory", data);
            SetField("store", new NoteStore(Path.Combine(data, "notes.db")));
            // Backup is tested elsewhere; isolate autosave/list work from the first daily backup.
            SetField("backupAttempt", DateOnly.FromDateTime(DateTime.Now));
            await Store.InitializeAsync();
            Call("CreateList");
            var list = (Memoit.Views.MainWindow)Field("list")!;
            var rows = (ListBox)list.FindName("NoteList");
            string body = string.Concat(Enumerable.Repeat("긴 메모 본문 · 반복 입력과 자동 저장 응답성 확인 0123456789\n", 700));
            for (int i = 0; i < (interactive ? 3 : 20); i++)
            {
                var note = new Note { Body = $"검증 {i:D2}\n" + body, Left = 20 + i % 5 * 180,
                    Top = 20 + i / 5 * 160, Width = 240, Height = 180 };
                var vm = (NoteViewModel)Call("AddModel", note, false)!;
                Call("ShowNote", vm, false);
            }
            Call("ShowOverlayPanel", false);
            var overlay = (OverlayPanelWindow)Field("overlayPanel")!;
            if (interactive)
            {
                overlay.ShowInTaskbar = true;
                overlay.Title = "OmniMemo 1.12.2 검증 패널";
                foreach (var window in NoteWindows.Values) window.ShowInTaskbar = true;
                await Task.Delay(TimeSpan.FromMinutes(5));
                Require(await NoteViewModel.FlushManyAsync(Notes.Values, Store.SaveManyAsync), "Interactive save failed.");
                checks.Add("Interactive verification session completed; manual results recorded separately.");
                return;
            }
            var chips = (List<ToggleButton>)typeof(OverlayPanelWindow).GetField("colorChips", Private)!.GetValue(overlay)!;
            var chipContents = chips.Select(c => c.Content).ToArray();
            var pages = (PagesControl)typeof(OverlayPanelWindow).GetField("pages", Private)!.GetValue(overlay)!;
            var monitors = (ComboBox)typeof(PagesControl).GetField("monitors", Private)!.GetValue(pages)!;
            await Settle();
            var oldItems = rows.ItemsSource;
            var oldPages = monitors.ItemsSource;
            foreach (var vm in Notes.Values) vm.Body += "수정";
            Require(await NoteViewModel.FlushManyAsync(Notes.Values, Store.SaveManyAsync), "Batch save failed.");
            await Settle();
            Require(ReferenceEquals(oldItems, rows.ItemsSource), "Hidden list rebuilt on saved callback.");
            Require(chips.Select((c, i) => ReferenceEquals(c.Content, chipContents[i])).All(same => same), "Body-only save rebuilt color chips.");
            Require(ReferenceEquals(oldPages, monitors.ItemsSource), "Unchanged pages rebuilt on saved callback.");
            checks.Add("20-note batch save leaves hidden list, unchanged color-chip visuals and unchanged page ItemsSource intact.");
            Call("ShowList", false);
            Require(rows.Items.Count == 20, "Opening list did not populate current notes.");
            Require((string)rows.Items[0].GetType().GetProperty("Title")!.GetValue(rows.Items[0])! == Notes.Values.OrderByDescending(vm => vm.Snapshot.UpdatedAt).First().Title, "Opening list has stale data.");
            await Settle();
            int replacements = 0;
            var descriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(ListBox));
            EventHandler replaced = (_, _) => replacements++;
            descriptor.AddValueChanged(rows, replaced);
            for (int i = 0; i < 20; i++) Call("OnNoteSaved");
            Require(replacements == 0, "Saved callback rebuilt list synchronously.");
            await Settle();
            descriptor.RemoveValueChanged(rows, replaced);
            Require(replacements == 1, $"Expected one queued batch refresh; got {replacements}.");
            checks.Add("20 saved callbacks coalesce into one deferred visible-list refresh; reopening hidden list uses current snapshots.");
            list.Hide();

            var legacyClock = Stopwatch.StartNew();
            for (int i = 0; i < 100; i++)
                list.SetNotes(Notes.Values.Select(vm => vm.Snapshot));
            measurements.Add(new { scenario = "Legacy hidden-list rebuild equivalent, 100 saves", elapsedMs = legacyClock.Elapsed.TotalMilliseconds });
            var currentClock = Stopwatch.StartNew();
            for (int i = 0; i < 100; i++) Call("OnNoteSaved");
            await Settle();
            measurements.Add(new { scenario = "Current hidden-list callbacks, 100 saves", elapsedMs = currentClock.Elapsed.TotalMilliseconds });

            var target = NoteWindows.Values.First();
            target.Activate(); target.Editor.Focus(); target.Editor.CaretIndex = target.Editor.Text.Length;
            await Settle();
            int saves = 0, edits = 0;
            Notes.Values.First().Saved += () => saves++;
            var intervals = new List<double>();
            var clock = Stopwatch.StartNew();
            double last = 0;
            var heartbeat = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
            heartbeat.Tick += (_, _) => { double now = clock.Elapsed.TotalMilliseconds; intervals.Add(now - last); last = now; };
            heartbeat.Start();
            while (clock.Elapsed < TimeSpan.FromSeconds(12))
            {
                target.Editor.AppendText(" 입력"); edits++;
                await Task.Delay(60);
            }
            await Task.Delay(700);
            heartbeat.Stop();
            Require(await Notes.Values.First().FlushAsync(), "Final editor save failed.");
            var persisted = (await Store.LoadAsync()).Single(n => n.Id == Notes.Values.First().Snapshot.Id);
            Require(persisted.Body == target.Editor.Text, "Saved body differs from editor after repeated typing.");
            Require(saves >= 4, $"Too few autosaves measured: {saves}.");
            var ordered = intervals.Order().ToArray();
            measurements.Add(new { scenario = "20 visible notes, 700-line bodies, continuous editor AppendText and real SQLite autosave", durationMs = clock.Elapsed.TotalMilliseconds,
                noteCount = Notes.Count, initialCharactersPerNote = body.Length, edits, saves, heartbeatSamples = intervals.Count,
                maxHeartbeatIntervalMs = intervals.Max(), p99HeartbeatIntervalMs = ordered[(int)((ordered.Length - 1) * .99)] });
            Require(intervals.Max() < 1000, "Observed a >=1 second UI stall during autosave typing scenario.");
            checks.Add("12-second continuous editor edits with real autosave preserve exact persisted text and contain no >=1 second dispatcher stall.");
        }
        catch (Exception ex) { result = 1; error = ex.ToString(); }
        finally
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, JsonSerializer.Serialize(new { success = result == 0, error, dataDirectory = data, checks, measurements,
                limitations = "In-process WPF editor AppendText and dispatcher heartbeat, not physical keyboard/IME or an end-user workload. Legacy comparison isolates list reconstruction and is not a full old-binary benchmark." }, new JsonSerializerOptions { WriteIndented = true }));
            foreach (var w in NoteWindows.Values.ToArray()) { w.AllowClose = true; w.Close(); }
            if (Field("list") is Memoit.Views.MainWindow list) { list.AllowClose = true; list.Close(); }
            if (Field("overlayPanel") is OverlayPanelWindow overlay) { overlay.AllowClose = true; overlay.Close(); }
            Console.WriteLine(File.ReadAllText(outputPath));
            Shutdown(result);
        }
    }
    private Task Settle() => Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task;
    private object? Field(string name) => typeof(App).GetField(name, Private)!.GetValue(this);
    private void SetField(string name, object? value) => typeof(App).GetField(name, Private)!.SetValue(this, value);
    private object? Call(string name, params object[] args) => typeof(App).GetMethod(name, Private)!.Invoke(this, args);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var app = new SaveVerificationApp(args.Length == 2 && args[0] == "--output" ? args[1] : "artifacts/verification/save-responsiveness.json", args.Contains("--interactive"))
            { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        return app.Run();
    }
}
