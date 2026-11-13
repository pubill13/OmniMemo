using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Memoit.Models;
using Memoit.ViewModels;
using Memoit.Views;
using Memoit.Services;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        string output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/verification/large-note.json");
        int lines = args.Length > 1 ? int.Parse(args[1]) : 50_000;
        int width = args.Length > 2 ? int.Parse(args[2]) : 100;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            var measurements = new List<object>();
            string? error = null;
            NoteWindow? window = null;
            try
            {
                string line = "한글 메모 😊 0123456789 ";
                line = string.Concat(Enumerable.Repeat(line, width / line.Length + 1))[..width] + "\n";
                string body = string.Concat(Enumerable.Repeat(line, lines));
                string data = Path.Combine(Path.GetDirectoryName(output)!, "large-note-data-" + Guid.NewGuid().ToString("N"));
                using var store = new NoteStore(Path.Combine(data, "notes.db"));
                await store.InitializeAsync();
                var note = new Note { Body = body };
                await store.SaveAsync(note);
                int saves = 0;
                using var vm = new NoteViewModel(note, async snapshot => { await store.SaveAsync(snapshot); saves++; });
                await Measure("open", () =>
                {
                    window = new NoteWindow(vm) { ShowActivated = false };
                    window.Show();
                });
                var editor = window!.Editor;
                foreach (int offset in new[] { 0, body.Length / 2, body.Length })
                {
                    await Measure("navigate-" + offset, () =>
                    {
                        editor.CaretOffset = offset;
                        editor.ScrollToLine(editor.Document.GetLineByOffset(offset).LineNumber);
                    });
                    for (int i = 0; i < 8; i++)
                        await Measure("edit-" + offset, () => editor.SelectedText = "가");
                    await Measure("undo-" + offset, () => editor.Undo());
                }
                await Measure("scroll-end", editor.ScrollToEnd);
                await Measure("scroll-home", editor.ScrollToHome);
                await Measure("resize", () => window.Width += 100);
                string beforePaste = editor.Text;
                await Measure("bulk-insert", () =>
                {
                    editor.Select(editor.Document.TextLength, 0);
                    editor.SelectedText = body;
                });
                await Measure("bulk-undo", () => editor.Undo());
                if (editor.Text != beforePaste) throw new InvalidOperationException("Bulk undo changed existing text.");
                await Measure("bulk-redo", () => editor.Redo());
                if (editor.Document.TextLength != beforePaste.Length + body.Length)
                    throw new InvalidOperationException("Bulk redo lost text.");
                editor.Undo();
                await Measure("select-delete", () =>
                {
                    editor.Select(editor.Document.TextLength / 2, Math.Min(100, editor.Document.TextLength / 2));
                    editor.SelectedText = "";
                });
                editor.Undo();
                editor.Select(editor.Document.TextLength, 0);
                editor.TextArea.PerformTextInput("한글 입력😊");
                var saveWatch = Stopwatch.StartNew();
                if (!await vm.FlushAsync()) throw new InvalidOperationException("Flush failed.");
                measurements.Add(new { operation = "save", ms = saveWatch.Elapsed.TotalMilliseconds });
                var backupWatch = Stopwatch.StartNew();
                var backup = store.CreateDailyBackupAsync(Path.Combine(data, "backups"));
                await Measure("edit-during-backup", () => editor.TextArea.PerformTextInput("백업 중 입력"));
                await backup;
                measurements.Add(new { operation = "backup", ms = backupWatch.Elapsed.TotalMilliseconds });
                if (!await vm.FlushAsync()) throw new InvalidOperationException("Final flush failed.");
                var heartbeatWatch = Stopwatch.StartNew();
                var heartbeat = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(10) };
                double lastBeat = 0, maxHeartbeatMs = 0;
                heartbeat.Tick += (_, _) =>
                {
                    double now = heartbeatWatch.Elapsed.TotalMilliseconds;
                    maxHeartbeatMs = Math.Max(maxHeartbeatMs, now - lastBeat);
                    lastBeat = now;
                };
                int previousSaves = saves;
                heartbeat.Start();
                try
                {
                    for (int i = 0; i < 100; i++)
                    {
                        editor.TextArea.PerformTextInput("가");
                        await Task.Delay(30);
                    }
                    await Task.Delay(650);
                    if (saves == previousSaves) throw new InvalidOperationException("Autosave did not run.");
                    if (!await vm.FlushAsync()) throw new InvalidOperationException("Autosave flush failed.");
                }
                finally { heartbeat.Stop(); }
                measurements.Add(new { operation = "continuous-edit-autosave", maxHeartbeatMs, saves = saves - previousSaves });
                if (maxHeartbeatMs >= 500) throw new InvalidOperationException("Dispatcher stalled during continuous editing.");
                using var reopened = new NoteStore(Path.Combine(data, "notes.db"));
                var persisted = (await reopened.LoadAsync()).Single();
                if (persisted.Body != editor.Text || vm.Body != editor.Text)
                    throw new InvalidOperationException("Reopened DB, editor and model differ.");

                async Task Measure(string operation, Action action)
                {
                    long allocated = GC.GetAllocatedBytesForCurrentThread();
                    var watch = Stopwatch.StartNew();
                    action();
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    double ms = watch.Elapsed.TotalMilliseconds;
                    long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                    measurements.Add(new { operation, ms, allocatedBytes });
                    Console.WriteLine($"{operation}: {ms:F2} ms");
                }
            }
            catch (Exception ex) { error = ex.ToString(); }
            finally
            {
                if (window is not null) { window.AllowClose = true; window.Close(); }
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.WriteAllText(output, JsonSerializer.Serialize(new { lines, width, error, measurements,
                    workingSet = Environment.WorkingSet }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine(output);
                app.Shutdown(error is null ? 0 : 1);
            }
        };
        app.Run();
    }
}
