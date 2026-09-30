using System.Windows;
using Memoit.Models;
using Memoit.Services;
using Memoit.Views;

namespace Memoit;

public partial class App
{
    private string? lastActiveNoteMonitor;
    private NoteWindow? lastActiveNoteWindow;
    private readonly Dictionary<Guid, Rect> initialNoteBounds = [];

    private void RecordNoteActivation(NoteWindow window)
    {
        lastActiveNoteWindow = window;
        lastActiveNoteMonitor = MonitorCatalog.ForWindow(window).Id;
    }

    private void ApplyInitialNotePlacement(NoteWindow window, Guid id)
    {
        if (!initialNoteBounds.Remove(id, out var bounds)) return;
        window.BeginArrangement(bounds);
        try { WindowPlacement.MoveTogether([(window, bounds)]); }
        finally { window.EndArrangement(); }
    }

    private Task NewNoteAsync() => NewNoteAsync(null);

    private async Task NewNoteAsync(NoteWindow? source)
    {
        var monitors = MonitorCatalog.All();
        var cursor = System.Windows.Forms.Cursor.Position;
        // An active note can cross monitors without another activation event.
        if (lastActiveNoteWindow is not null && windows.Values.Contains(lastActiveNoteWindow)
            && monitors.Any(m => m.Id == lastActiveNoteMonitor))
            lastActiveNoteMonitor = MonitorCatalog.Nearest(WindowPlacement.GetBounds(lastActiveNoteWindow), monitors).Id;
        var monitor = source is null
            ? NotePlacement.SelectMonitor(lastActiveNoteMonitor, new Point(cursor.X, cursor.Y), monitors)
            : MonitorCatalog.ForWindow(source);
        var occupied = windows.Values.Where(w => w.IsVisible && !w.IsCollapsed)
            .Select(WindowPlacement.GetBounds).ToArray();
        var note = new Note();
        var bounds = NotePlacement.Create(new Size(note.Width, note.Height), monitor,
            layoutSettings.GetMonitor(monitor.Id).ExpandedCorner, occupied,
            source is null ? null : WindowPlacement.GetBounds(source));
        note = note with
        {
            Left = bounds.Left / monitor.Scale, Top = bounds.Top / monitor.Scale,
            Width = bounds.Width / monitor.Scale, Height = bounds.Height / monitor.Scale,
            ExpandedLeft = bounds.Left / monitor.Scale, ExpandedTop = bounds.Top / monitor.Scale
        };
        var vm = AddModel(note, true);
        initialNoteBounds[note.Id] = bounds;
        try { ShowNote(vm); }
        finally { initialNoteBounds.Remove(note.Id); }
        if (!await vm.FlushAsync())
            Error("새 메모를 저장하지 못했습니다. 창의 내용을 복사해 보관하거나 저장을 다시 시도하세요.");
    }
}
