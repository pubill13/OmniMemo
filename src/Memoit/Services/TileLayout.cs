using System.Windows;
using System.Globalization;
using Memoit.Models;

namespace Memoit.Services;

public static class TileLayout
{
    private static readonly string[] Colors = ["#FFF2B3", "#FFF2B2", "#FFDDE7", "#DEF0D8", "#DCEBFA", "#EAE0F7", "#FAFAF5"];

    public static IReadOnlyDictionary<Guid, Point> Arrange(IEnumerable<Note> notes, Rect workAreaPixels,
        MonitorLayout options, double scale = 1, IReadOnlyDictionary<Guid, Size>? sizesPixels = null)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (!double.IsFinite(scale) || scale <= 0 || workAreaPixels.IsEmpty
            || !double.IsFinite(workAreaPixels.X) || !double.IsFinite(workAreaPixels.Y)
            || !double.IsFinite(workAreaPixels.Width) || !double.IsFinite(workAreaPixels.Height)
            || !double.IsFinite(workAreaPixels.Right) || !double.IsFinite(workAreaPixels.Bottom))
            throw new ArgumentException("모니터 영역 또는 배율이 올바르지 않습니다.");
        var titleComparer = StringComparer.Create(CultureInfo.GetCultureInfo("ko-KR"), false);
        var ordered = notes.OrderBy(n => options.Sort == LayoutSort.Color ? ColorOrder(n.Color) : 0)
            .ThenBy(n => options.Sort == LayoutSort.Color && ColorOrder(n.Color) == int.MaxValue ? n.Color : "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => options.Sort == LayoutSort.Title ? n.Title : "", titleComparer)
            .ThenBy(n => n.CreatedAt).ThenBy(n => n.Id).ToArray();
        if (ordered.Select(n => n.Id).Distinct().Count() != ordered.Length)
            throw new ArgumentException("중복된 메모 ID가 있습니다.", nameof(notes));
        double gap = options.Gap * scale;
        double x = workAreaPixels.X + options.X * scale, y = workAreaPixels.Y + options.Y * scale;
        if (!double.IsFinite(gap) || !double.IsFinite(x) || !double.IsFinite(y))
            throw new ArgumentException("타일 위치 또는 배율이 올바르지 않습니다.");
        double originX = x, rowHeight = 0;
        int inRow = 0;
        var positions = new Dictionary<Guid, Point>();
        foreach (var note in ordered)
        {
            Size size;
            if (sizesPixels is not null)
            {
                if (!sizesPixels.TryGetValue(note.Id, out size))
                    throw new ArgumentException("메모의 실제 창 크기가 누락되었습니다.", nameof(sizesPixels));
            }
            else size = options.IncludeExpanded && !note.IsCollapsed
                ? new Size(note.Width * scale, note.Height * scale) : new Size(36 * scale, 36 * scale);
            if (size.IsEmpty || !double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0)
                throw new ArgumentException("메모의 창 크기가 올바르지 않습니다.", nameof(sizesPixels));
            bool wrap = inRow > 0 && (options.Shape == LayoutShape.Vertical
                || options.Shape == LayoutShape.Grid && (options.Columns > 0 ? inRow >= options.Columns : x + size.Width > workAreaPixels.Right));
            if (wrap) { x = originX; y += rowHeight + gap; rowHeight = 0; inRow = 0; }
            var point = new Point(x, y);
            if (!double.IsFinite(x) || !double.IsFinite(y) || !workAreaPixels.Contains(new Rect(point, size)))
                throw new InvalidOperationException("화면에 모든 메모를 겹치지 않게 배치할 공간이 없습니다.");
            positions.Add(note.Id, point);
            x += size.Width + gap;
            rowHeight = Math.Max(rowHeight, size.Height);
            inRow++;
        }
        return positions;
    }

    // All sizes and the work area must use the same coordinate unit.
    public static IReadOnlyDictionary<Guid, Point> Arrange(IEnumerable<Note> notes, Rect area,
        double tileSize = 36, double gap = 8, bool byColor = false)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (area.IsEmpty || !double.IsFinite(area.X) || !double.IsFinite(area.Y)
            || !double.IsFinite(area.Width) || !double.IsFinite(area.Height)
            || !double.IsFinite(tileSize) || !double.IsFinite(gap) || tileSize <= 0 || gap < 0)
            throw new ArgumentException("타일 배치 영역 또는 크기가 올바르지 않습니다.");
        var ordered = notes.OrderBy(n => byColor ? ColorOrder(n.Color) : 0)
            .ThenBy(n => byColor && ColorOrder(n.Color) == int.MaxValue ? n.Color : "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => n.CreatedAt).ThenBy(n => n.Id).ToArray();
        if (ordered.Select(n => n.Id).Distinct().Count() != ordered.Length)
            throw new ArgumentException("중복된 메모 ID가 있습니다.", nameof(notes));
        double step = tileSize + gap;
        double columns = Math.Max(0, Math.Floor((area.Width - gap) / step));
        double rows = Math.Max(0, Math.Floor((area.Height - gap) / step));
        if (ordered.Length > columns * rows)
            throw new InvalidOperationException("화면에 모든 타일을 겹치지 않게 배치할 공간이 없습니다.");
        var result = new Dictionary<Guid, Point>();
        for (int i = 0; i < ordered.Length; i++)
        {
            result.Add(ordered[i].Id, new Point(area.X + gap + i % columns * step,
                area.Y + gap + Math.Floor(i / columns) * step));
        }
        return result;
    }

    private static int ColorOrder(string color)
    {
        int index = Array.FindIndex(Colors, c => string.Equals(c, color, StringComparison.OrdinalIgnoreCase));
        // Both historical yellow colors belong to the same group.
        return index < 0 ? int.MaxValue : Math.Max(0, index - 1);
    }
}
