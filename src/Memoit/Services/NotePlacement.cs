using System.Windows;

namespace Memoit.Services;

public static class NotePlacement
{
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
