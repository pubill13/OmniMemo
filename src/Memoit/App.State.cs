using System.Windows;
using System.Diagnostics;
using Memoit.Services;
using Memoit.ViewModels;

namespace Memoit;

public partial class App
{
    private async void RunStateChange(string command)
    {
        if (arranging || busy || shuttingDown) return;
        arranging = true;
        try
        {
            bool collapsed = command == "CollapseAll" || command == "ToggleCollapsed"
                && windows.Any(p => IsLogicallyVisible(p.Key) && !p.Value.IsCollapsed);
            await ChangeCollapsedStateAsync(collapsed);
        }
        catch (Exception ex) { ArrangementNotice(ex.Message, true); }
        finally { arranging = false; RefreshList(); }
    }

    private async Task ChangeCollapsedStateAsync(bool collapsed, string? color = null, bool filtered = false)
    {
        var watch = Stopwatch.StartNew();
        var selected = windows.Where(p => IsLogicallyVisible(p.Key)
            && (!filtered || NoteColors.Matches(notes[p.Key].Snapshot.Color, color))).ToArray();
        var monitors = MonitorCatalog.All();
        var moves = selected.Where(p => p.Value.IsCollapsed != collapsed).Select(p =>
        {
            var current = WindowPlacement.GetBounds(p.Value);
            var monitor = MonitorCatalog.Nearest(current, monitors);
            var note = notes[p.Key].Snapshot;
            var target = collapsed ? new Rect(current.Location, new Size(36 * monitor.Scale, 36 * monitor.Scale))
                : new Rect(current.Location, new Size(Math.Min(note.Width * monitor.Scale, monitor.WorkArea.Width),
                    Math.Min(note.Height * monitor.Scale, monitor.WorkArea.Height)));
            if (!collapsed)
                target.Location = new Point(Math.Clamp(target.Left, monitor.WorkArea.Left, monitor.WorkArea.Right - target.Width),
                    Math.Clamp(target.Top, monitor.WorkArea.Top, monitor.WorkArea.Bottom - target.Height));
            return (Id: p.Key, Window: p.Value, Bounds: target);
        }).ToArray();
        double calculationMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        if (!filtered) ClearPageSession();
        else
            foreach (var session in pageSessions.Values)
                foreach (var page in session.Pages)
                    foreach (var move in moves) page.Remove(move.Id);
        try
        {
            foreach (var move in moves) move.Window.PrepareState(collapsed, move.Bounds);
            WindowPlacement.MoveTogether(moves.Select(m => ((Window)m.Window, m.Bounds)).ToArray());
        }
        finally { foreach (var move in moves) move.Window.EndArrangement(); }
        foreach (var pair in selected)
            if (!pair.Value.IsVisible && (!filtered || moves.Any(m => m.Id == pair.Key)))
            { pair.Value.ShowActivated = false; pair.Value.Show(); }
        RefreshPageControls();
        layoutSettings = layoutSettings with { PagedExpansionActive = pageSessions.Count > 0 };
        double moveMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        if (!await NoteViewModel.FlushManyAsync(moves.Select(m => notes[m.Id]), store.SaveManyAsync))
            throw new IOException("메모 상태를 저장하지 못했습니다. 화면 상태는 유지됩니다. 저장 다시 시도를 이용하세요.");
        await PersistLayoutAsync();
        LastArrangementTiming = new(calculationMs, moveMs, watch.Elapsed.TotalMilliseconds, moves.Length);
    }
}
