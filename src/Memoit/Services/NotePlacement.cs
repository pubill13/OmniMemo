using System.Windows;

namespace Memoit.Services;

public sealed record NewNotePlacement(Rect Bounds, Point CollapsedPosition, bool Overlaps);

public static class NotePlacement
{
    public static NewNotePlacement CreateBesideColor(Size sizeDip, MonitorDescriptor monitor,
        MonitorLayout options, IReadOnlyList<Rect> orderedSameColor, IReadOnlyList<Rect> occupied)
    {
        options.Validate();
        var area = monitor.WorkArea;
        double scale = monitor.Scale;
        if (!double.IsFinite(scale) || scale <= 0 || area.IsEmpty || area.Width <= 0 || area.Height <= 0
            || !double.IsFinite(sizeDip.Width) || !double.IsFinite(sizeDip.Height)
            || sizeDip.Width <= 0 || sizeDip.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(sizeDip));
        var size = new Size(Math.Min(sizeDip.Width * scale, area.Width), Math.Min(sizeDip.Height * scale, area.Height));
        var group = orderedSameColor.Where(r => !r.IsEmpty).ToArray();
        var peers = occupied.Concat(group).Where(r => !r.IsEmpty && r.IntersectsWith(area)).Distinct().ToArray();
        double gap = options.Gap * scale;
        var origin = group.Length == 0
            ? new Point(area.Left + options.X * scale, area.Top + options.Y * scale)
            : new Point(group.Min(r => r.Left), group.Min(r => r.Top));
        var preferred = group.Length == 0 ? origin : new Point(group[^1].Right + gap, group[^1].Top);
        var wrapped = group.Length == 0 ? origin : new Point(origin.X, group.Max(r => r.Bottom) + gap);
        Rect Place(Size footprint)
        {
            bool Free(Rect bounds) => area.Contains(bounds) && !peers.Any(r =>
                bounds.Left < r.Right + gap && bounds.Right + gap > r.Left
                && bounds.Top < r.Bottom + gap && bounds.Bottom + gap > r.Top);
            foreach (var point in new[] { preferred, wrapped })
            {
                var candidate = new Rect(point, footprint);
                if (Free(candidate)) return candidate;
            }
            // Rectangle edges are the only candidate coordinates needed for a free footprint.
            // Prefer rows below this color group before searching the remaining work area.
            var xs = peers.SelectMany(r => new[] { r.Right + gap, r.Left - gap - footprint.Width })
                .Concat([origin.X, area.Left, area.Right - footprint.Width]).Distinct().OrderBy(x => x).ToArray();
            var ys = peers.SelectMany(r => new[] { r.Bottom + gap, r.Top - gap - footprint.Height })
                .Concat([preferred.Y, wrapped.Y, area.Top, area.Bottom - footprint.Height]).Distinct()
                .OrderBy(y => y < preferred.Y ? 1 : 0).ThenBy(y => y).ToArray();
            foreach (double y in ys)
                foreach (double x in xs)
                {
                    var candidate = new Rect(new Point(x, y), footprint);
                    if (Free(candidate)) return candidate;
                }
            double steps = group.Length;
            double maxStep = Math.Floor(Math.Min(Math.Max(0, area.Right - origin.X - footprint.Width),
                Math.Max(0, area.Bottom - origin.Y - footprint.Height)) / (24 * scale));
            double offset = steps % (maxStep + 1) * 24 * scale;
            return new Rect(new Point(Math.Clamp(origin.X + offset, area.Left, area.Right - footprint.Width),
                Math.Clamp(origin.Y + offset, area.Top, area.Bottom - footprint.Height)), footprint);
        }
        var bounds = Place(size);
        var tile = Place(new Size(Math.Min(36 * scale, area.Width), Math.Min(36 * scale, area.Height)));
        bool overlaps = peers.Any(r => bounds.Left < r.Right && bounds.Right > r.Left
            && bounds.Top < r.Bottom && bounds.Bottom > r.Top);
        return new(bounds, tile.Location, overlaps);
    }

    public static MonitorDescriptor SelectMonitor(string? lastMonitorId, Point cursor,
        IReadOnlyList<MonitorDescriptor> monitors)
        => monitors.FirstOrDefault(m => m.Id == lastMonitorId)
            ?? MonitorCatalog.Nearest(new Rect(cursor, new Size(1, 1)), monitors);

    public static Rect Create(Size sizeDip, MonitorDescriptor monitor, ExpandedCorner corner,
        IReadOnlyList<Rect> occupied, Rect? source = null)
    {
        var area = monitor.WorkArea;
        double scale = monitor.Scale;
        var size = new Size(Math.Min(sizeDip.Width * scale, area.Width),
            Math.Min(sizeDip.Height * scale, area.Height));
        if (source.HasValue) return new Rect(Cascade(source.Value, size, area, 28 * scale), size);
        double marginX = Math.Min(8 * scale, Math.Max(0, (area.Width - size.Width) / 2));
        double marginY = Math.Min(8 * scale, Math.Max(0, (area.Height - size.Height) / 2));
        bool right = corner is ExpandedCorner.TopRight or ExpandedCorner.BottomRight;
        bool bottom = corner is ExpandedCorner.BottomRight or ExpandedCorner.BottomLeft;
        var origin = new Point(right ? area.Right - marginX - size.Width : area.Left + marginX,
            bottom ? area.Bottom - marginY - size.Height : area.Top + marginY);
        var candidate = new Rect(origin, size);
        double step = 24 * scale;
        while (occupied.Any(rect => Math.Abs(rect.Left - candidate.Left) < scale
            && Math.Abs(rect.Top - candidate.Top) < scale))
        {
            candidate.Offset(right ? -step : step, bottom ? -step : step);
            if (!area.Contains(candidate)) return new Rect(origin, size);
        }
        return candidate;
    }

    public static Point Cascade(Rect source, Size size, Rect workArea, double offset = 28)
    {
        double x = source.Left + offset, y = source.Top + offset;
        if (x + size.Width > workArea.Right) x = source.Left - offset;
        if (y + size.Height > workArea.Bottom) y = source.Top - offset;
        return new Point(
            Math.Clamp(x, workArea.Left, Math.Max(workArea.Left, workArea.Right - size.Width)),
            Math.Clamp(y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - size.Height)));
    }
}
