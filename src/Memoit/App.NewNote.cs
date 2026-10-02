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

    private async Task NewNoteAsync(NoteWindow? source, string? preferredColor = null)
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
        var last = lastActiveNoteWindow?.DataContext as Memoit.ViewModels.NoteViewModel;
        var origin = source?.DataContext as Memoit.ViewModels.NoteViewModel;
        string color = origin?.Snapshot.Color ?? preferredColor
            ?? (last is not null && notes.ContainsKey(last.Snapshot.Id) && last.Snapshot.DeletedAt is null
                ? last.Snapshot.Color : null) ?? NoteColors.Values[0];
        var note = new Note { Color = color };
        var scene = windows.Where(p => p.Value.IsVisible && notes[p.Key].Snapshot.DeletedAt is null)
            .Select(p => (Note: notes[p.Key].Snapshot, Bounds: WindowPlacement.GetBounds(p.Value)))
            .Where(p => MonitorCatalog.Nearest(p.Bounds, monitors).Id == monitor.Id).ToArray();
        var order = layoutSettings.ArrangementOrder.GetValueOrDefault(monitor.Id) ?? [];
        var ranks = order.Select((id, index) => (id, index)).ToDictionary(p => p.id, p => p.index);
        var matching = scene.Where(p => NoteColors.Matches(p.Note.Color, color))
            .OrderBy(p => ranks.GetValueOrDefault(p.Note.Id, int.MaxValue))
            .ThenBy(p => p.Note.CreatedAt).ThenBy(p => p.Note.Id).ToArray();
        var placement = NotePlacement.CreateBesideColor(new Size(note.Width, note.Height), monitor,
            layoutSettings.GetMonitor(monitor.Id), matching.Select(p => p.Bounds).ToArray(),
            scene.Select(p => p.Bounds).ToArray());
        var bounds = placement.Bounds;
        note = note with
        {
            Left = bounds.Left / monitor.Scale, Top = bounds.Top / monitor.Scale,
            Width = bounds.Width / monitor.Scale, Height = bounds.Height / monitor.Scale,
            ExpandedLeft = bounds.Left / monitor.Scale, ExpandedTop = bounds.Top / monitor.Scale,
            CollapsedLeft = placement.CollapsedPosition.X / monitor.Scale,
            CollapsedTop = placement.CollapsedPosition.Y / monitor.Scale
        };
        var vm = AddModel(note, true);
        initialNoteBounds[note.Id] = bounds;
        try { ShowNote(vm); }
        finally { initialNoteBounds.Remove(note.Id); }
        if (!await vm.FlushAsync())
            Error("새 메모를 저장하지 못했습니다. 창의 내용을 복사해 보관하거나 저장을 다시 시도하세요.");
        var nextOrder = order.Where(id => notes.ContainsKey(id))
            .Concat(scene.OrderBy(p => p.Note.CreatedAt).ThenBy(p => p.Note.Id).Select(p => p.Note.Id))
            .Distinct().ToList();
        int index = nextOrder.FindLastIndex(id => NoteColors.Matches(notes[id].Snapshot.Color, color));
        nextOrder.Insert(index < 0 ? nextOrder.Count : index + 1, note.Id);
        layoutSettings = layoutSettings with
        { ArrangementOrder = new(layoutSettings.ArrangementOrder) { [monitor.Id] = nextOrder } };
        try { await PersistLayoutAsync(); }
        catch (Exception ex) { ArrangementNotice("새 메모의 배치 순서를 저장하지 못했습니다: " + ex.Message, true); }
        if (placement.Overlaps)
            overlayPanel?.ShowStatus("화면에 빈 공간이 없어 같은 색 메모 근처에 겹쳐 열었습니다.");
    }
}
