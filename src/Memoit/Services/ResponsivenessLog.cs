using System.Diagnostics;
using System.Windows.Threading;

namespace Memoit.Services;

internal sealed class ResponsivenessLog
{
    private const int ProbeIntervalMs = 100;
    private const int SlowMs = 150;
    private const int LongOperationMs = 1000;
    private const int MaxLogBytes = 256 * 1024;
    private readonly object gate = new();
    private readonly HashSet<Operation> active = [];
    private readonly Queue<string> pending = new(64);
    private readonly string path;
    private readonly Dispatcher dispatcher;
    private readonly Thread worker;
    private volatile bool stopping;

    internal ResponsivenessLog(Dispatcher dispatcher, string directory)
    {
        this.dispatcher = dispatcher;
        path = Path.Combine(directory, "responsiveness.log");
        worker = new Thread(Watch) { IsBackground = true, Name = "OmniMemo responsiveness monitor" };
        worker.Start();
    }

    // Names and details come from code and numeric measurements, never from note data.
    internal IDisposable Track(string name)
    {
        var operation = new Operation(this, name);
        lock (gate) active.Add(operation);
        return operation;
    }

    internal void Stop(int exitCode)
    {
        lock (gate)
        {
            Enqueue($"session-end exit-code={exitCode}");
            stopping = true;
        }
        worker.Join(250);
    }

    private void Watch()
    {
        Write([$"{DateTimeOffset.Now:O} session-start version={typeof(App).Assembly.GetName().Version} pid={Environment.ProcessId}"]);
        Probe? probe = null;
        double nextReportMs = SlowMs;
        while (!stopping)
        {
            if (probe is null && !dispatcher.HasShutdownStarted)
                probe = new Probe(dispatcher);

            if (probe is not null)
            {
                long completed = Volatile.Read(ref probe.Completed);
                double elapsedMs = completed == 0 ? Stopwatch.GetElapsedTime(probe.Started).TotalMilliseconds
                    : Stopwatch.GetElapsedTime(probe.Started, completed).TotalMilliseconds;
                if (completed != 0 || probe.Dispatch.Status == DispatcherOperationStatus.Aborted)
                {
                    if (completed != 0 && elapsedMs >= SlowMs)
                        RecordProbe("ui-delayed", elapsedMs, includeActive: nextReportMs == SlowMs);
                    probe = null;
                    nextReportMs = SlowMs;
                }
                else if (elapsedMs >= nextReportMs)
                {
                    RecordProbe("ui-pending", elapsedMs, includeActive: true);
                    nextReportMs = nextReportMs < LongOperationMs ? LongOperationMs : elapsedMs + 5000;
                }
            }

            lock (gate)
            {
                foreach (var operation in active)
                {
                    if (operation.Reported || operation.ElapsedMs < LongOperationMs) continue;
                    operation.Reported = true;
                    Enqueue($"work-pending name={operation.Name} elapsed-ms={operation.ElapsedMs:F0}");
                }
            }
            Drain();
            Thread.Sleep(ProbeIntervalMs);
        }
        Drain();
    }

    private void RecordProbe(string kind, double elapsedMs, bool includeActive)
    {
        lock (gate)
        {
            string context = includeActive
                ? " active=[" + string.Join(", ", active.Take(12).Select(op => $"{op.Name} {op.ElapsedMs:F0}ms")) + "]"
                : "";
            Enqueue($"{kind} elapsed-ms={elapsedMs:F0}{context}");
        }
    }

    private void Enqueue(string message)
    {
        if (pending.Count == 64) pending.Dequeue();
        pending.Enqueue($"{DateTimeOffset.Now:O} pid={Environment.ProcessId} {message}");
    }

    private void Drain()
    {
        string[] lines;
        lock (gate)
        {
            if (pending.Count == 0) return;
            lines = pending.ToArray();
            pending.Clear();
        }
        Write(lines);
    }

    private void Write(string[] lines)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var file = new FileInfo(path);
            if (file.Exists && file.Length >= MaxLogBytes)
                File.Move(path, path + ".previous", overwrite: true);
            File.AppendAllLines(path, lines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"OmniMemo could not write responsiveness diagnostics ({ex.GetType().Name}).");
        }
    }

    private sealed class Probe
    {
        internal readonly long Started = Stopwatch.GetTimestamp();
        internal long Completed;
        internal readonly DispatcherOperation Dispatch;

        internal Probe(Dispatcher dispatcher) =>
            Dispatch = dispatcher.BeginInvoke(DispatcherPriority.Input,
                new Action(() => Volatile.Write(ref Completed, Stopwatch.GetTimestamp())));
    }

    private sealed class Operation(ResponsivenessLog log, string name) : IDisposable
    {
        private readonly long started = Stopwatch.GetTimestamp();
        internal string Name { get; } = name;
        internal double ElapsedMs => Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        internal bool Reported { get; set; }

        public void Dispose()
        {
            double elapsedMs = ElapsedMs;
            lock (log.gate)
            {
                if (log.active.Remove(this) && elapsedMs >= SlowMs)
                    log.Enqueue($"work-ended name={Name} elapsed-ms={elapsedMs:F0}");
            }
        }
    }
}
