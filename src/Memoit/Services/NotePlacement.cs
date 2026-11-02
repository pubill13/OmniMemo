using System.Windows;

namespace Memoit.Services;

public static class NotePlacement
{
    public static Rect ExpandAtTile(Point tile, Size expandedPixels, Rect workArea)
    {
        double width = Math.Min(expandedPixels.Width, workArea.Width);
        double height = Math.Min(expandedPixels.Height, workArea.Height);
        return new Rect(Math.Clamp(tile.X, workArea.Left, workArea.Right - width),
            Math.Clamp(tile.Y, workArea.Top, workArea.Bottom - height), width, height);
    }

    public static Rect CreateAtTileOrigin(Size sizeDip, MonitorDescriptor monitor,
        MonitorLayout options, IReadOnlyList<Rect> expandedNotes)
    {
        options.Validate();
        var area = monitor.WorkArea;
        double scale = monitor.Scale;
        if (!double.IsFinite(scale) || scale <= 0 || area.IsEmpty || area.Width <= 0 || area.Height <= 0
            || !double.IsFinite(sizeDip.Width) || !double.IsFinite(sizeDip.Height)
            || sizeDip.Width <= 0 || sizeDip.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(sizeDip));
        var size = new Size(Math.Min(sizeDip.Width * scale, area.Width), Math.Min(sizeDip.Height * scale, area.Height));
        var origin = new Point(
            Math.Clamp(area.Left + options.X * scale, area.Left, area.Right - size.Width),
            Math.Clamp(area.Top + options.Y * scale, area.Top, area.Bottom - size.Height));
        double step = 24 * scale;
        // At a right/bottom start point, cascade inward rather than immediately wrapping.
        double dx = origin.X + size.Width + step > area.Right ? -step : step;
        double dy = origin.Y + size.Height + step > area.Bottom ? -step : step;
        var candidate = new Rect(origin, size);
        var leastOccupied = candidate;
        int lowestCount = int.MaxValue;
        do
        {
            int count = expandedNotes.Count(r => !r.IsEmpty && Math.Abs(r.Left - candidate.Left) < scale
                && Math.Abs(r.Top - candidate.Top) < scale);
            if (count == 0) return candidate;
            if (count < lowestCount)
            {
                lowestCount = count;
                leastOccupied = candidate;
            }
            candidate.Offset(dx, dy);
        } while (area.Contains(candidate));
        return leastOccupied;
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
