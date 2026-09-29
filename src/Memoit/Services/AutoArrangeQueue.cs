using System.Windows.Threading;

namespace Memoit.Services;

/// <summary>Coalesces background layouts until editing or dragging has finished.</summary>
public sealed class AutoArrangeQueue(Dispatcher dispatcher, Func<bool> canRun, Func<Task> arrange, Action<Exception> failed)
{
    private bool pending, queued, running;

    public void Request() { pending = true; Resume(); }
    public void CancelPending() => pending = false;

    public void Resume()
    {
        if (!pending || queued || running || !canRun()) return;
        queued = true;
        _ = dispatcher.BeginInvoke(async () =>
        {
            queued = false;
            if (!pending || running || !canRun()) return;
            pending = false;
            running = true;
            try { await arrange(); }
            catch (Exception error) { pending = false; failed(error); }
            finally { running = false; Resume(); }
        }, DispatcherPriority.Background);
    }
}
