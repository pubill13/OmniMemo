using System.Windows;
using Memoit.Models;

namespace Memoit.Services;

public sealed record ArrangementSnapshot(Guid Id, bool IsCollapsed, Rect Bounds, string MonitorId);

public sealed class ArrangementHistory
{
    private ArrangementSnapshot[] previous = [];
    public void Capture(IEnumerable<ArrangementSnapshot> snapshots) => previous = snapshots.ToArray();
    public void Clear() => previous = [];

    public IReadOnlyDictionary<Guid, Rect> GetRestorable(IEnumerable<Note> notes, IReadOnlyList<MonitorDescriptor> monitors)
    {
        var current = notes.ToDictionary(n => n.Id);
        var result = new Dictionary<Guid, Rect>();
        if (monitors.Count == 0) return result;
        foreach (var item in previous)
        {
            if (!current.TryGetValue(item.Id, out var note) || note.DeletedAt is not null
                || !note.IsVisible || note.IsCollapsed != item.IsCollapsed) continue;
            var bounds = item.Bounds;
            if (!monitors.Any(m => m.Id == item.MonitorId))
            {
                var area = MonitorCatalog.Nearest(bounds, monitors).WorkArea;
                double width = Math.Min(bounds.Width, area.Width), height = Math.Min(bounds.Height, area.Height);
                bounds = new Rect(Math.Clamp(bounds.X, area.Left, area.Right - width),
                    Math.Clamp(bounds.Y, area.Top, area.Bottom - height), width, height);
            }
            result[item.Id] = bounds;
        }
        return result;
    }
}
