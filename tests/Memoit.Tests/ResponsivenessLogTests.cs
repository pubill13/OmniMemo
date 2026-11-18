using System.Reflection;
using System.Windows.Threading;
using Xunit;

namespace Memoit.Tests;

[Collection("WPF")]
public sealed class ResponsivenessLogTests
{
    [Fact]
    public async Task WritesPendingDelayBeforeUiRecoversAndPreservesItAfterRestart()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"OmniMemo-diagnostics-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "responsiveness.log");
        var type = typeof(App).Assembly.GetType("Memoit.Services.ResponsivenessLog", throwOnError: true)!;
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                var log = NewLog(dispatcher);
                dispatcher.InvokeAsync(() =>
                {
                    using var work = (IDisposable)type.GetMethod("Track", members)!.Invoke(log, ["Editor.TestEdit"])!;
                    started.SetResult(dispatcher);
                    release.Wait(TimeSpan.FromSeconds(15));
                }, DispatcherPriority.Send);
                Dispatcher.Run();
                type.GetMethod("Stop", members)!.Invoke(log, [0]);
                stopped.SetResult();
            }
            catch (Exception ex)
            {
                started.TrySetException(ex);
                stopped.TrySetException(ex);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Dispatcher? ui = null;
        try
        {
            ui = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            string pending = await ReadUntil(path, "ui-pending");
            Assert.Contains("Editor.TestEdit", pending);
            Assert.DoesNotContain("ui-delayed", pending);

            release.Set();
            Assert.Contains("work-ended name=Editor.TestEdit", await ReadUntil(path, "ui-delayed"));
            ui.BeginInvokeShutdown(DispatcherPriority.Send);
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains("session-end exit-code=0", ReadLog(path));

            var restartedLog = NewLog(ui);
            type.GetMethod("Stop", members)!.Invoke(restartedLog, [0]);
            string restarted = ReadLog(path);
            Assert.Equal(2, restarted.Split("session-start").Length - 1);
            Assert.Contains("ui-pending", restarted);
        }
        finally
        {
            release.Set();
            if (ui is { HasShutdownStarted: false }) ui.BeginInvokeShutdown(DispatcherPriority.Send);
            if (ui is not null) await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }

        object NewLog(Dispatcher dispatcher) =>
            Activator.CreateInstance(type, members, null, [dispatcher, directory], null)!;
    }

    private static async Task<string> ReadUntil(string path, string marker)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            try
            {
                string log = ReadLog(path);
                if (log.Contains(marker, StringComparison.Ordinal)) return log;
            }
            catch (IOException)
            {
                // The writer may be opening the file or appending another sample.
            }
            await Task.Delay(25, timeout.Token);
        }
    }

    private static string ReadLog(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
