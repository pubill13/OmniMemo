using System.Globalization;
using System.Windows;
using Memoit.Models;

namespace Memoit.Services;

public enum ExpandedCorner { TopRight, TopLeft, BottomRight, BottomLeft }

public sealed record ArrangementResult(IReadOnlyDictionary<Guid, Rect> Bounds,
    int SkippedTiles = 0, bool UsedCascade = false);

public static class NoteArrangement
{
    private static readonly string[] Colors = ["#FFF2B3", "#FFF2B2", "#FFDDE7", "#DEF0D8", "#DCEBFA", "#EAE0F7", "#FAFAF5"];

    public static ArrangementResult ArrangeCollapsed(IEnumerable<Note> notes, Rect areaPixels,
        MonitorLayout options, double scale = 1, IReadOnlyDictionary<Guid, Size>? sizesPixels = null)
    {
        Validate(areaPixels, options, scale);
        var ordered = OrderNotes(notes, options.Sort).Where(n => n.IsCollapsed).ToArray();
        var result = new Dictionary<Guid, Rect>();
        double x = options.X * scale, y = options.Y * scale, rowHeight = 0;
        double gap = options.Gap * scale;
        int column = 0, skipped = 0;
        string? previousColor = null;
        foreach (var note in ordered)
        {
            var size = GetSize(note, scale, sizesPixels);
            bool wrap = column > 0 && (options.Shape == LayoutShape.Vertical
                || options.Shape == LayoutShape.Grid && (options.Sort == LayoutSort.Color && previousColor != CanonicalColor(note.Color) || (options.Columns > 0
                    ? column >= options.Columns : x + size.Width > areaPixels.Width)));
            if (wrap) { x = options.X * scale; y += rowHeight + gap; rowHeight = 0; column = 0; }
            var bounds = new Rect(areaPixels.X + x, areaPixels.Y + y, size.Width, size.Height);
            if (areaPixels.Contains(bounds)) result.Add(note.Id, bounds);
            else skipped++;
            x += size.Width + gap;
            rowHeight = Math.Max(rowHeight, size.Height);
            column++;
            previousColor = CanonicalColor(note.Color);
        }
        return new(result, skipped);
    }

    public static ArrangementResult ArrangeExpanded(IEnumerable<Note> notes, Rect areaPixels,
        MonitorLayout options, ExpandedCorner corner = ExpandedCorner.TopRight, double scale = 1,
        IReadOnlyDictionary<Guid, Size>? sizesPixels = null, IReadOnlyList<Guid>? orderedIds = null)
    {
        Validate(areaPixels, options, scale);
        if (!Enum.IsDefined(corner)) throw new ArgumentOutOfRangeException(nameof(corner));
        var ordered = OrderNotes(notes, options.Sort).Where(n => !n.IsCollapsed).ToArray();
        if (orderedIds is not null)
        {
            var ranks = orderedIds.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index);
            ordered = ordered.OrderBy(n => ranks.GetValueOrDefault(n.Id, int.MaxValue)).ToArray();
        }
        var sizes = ordered.ToDictionary(n => n.Id, n =>
        {
            var size = GetSize(n, scale, sizesPixels);
            return new Size(Math.Min(size.Width, areaPixels.Width), Math.Min(size.Height, areaPixels.Height));
        });
        double margin = 8 * scale, gap = options.Gap * scale;
        double x = margin, y = margin, rowHeight = 0;
        var result = new Dictionary<Guid, Rect>();
        bool overflow = false;
        foreach (var note in ordered)
        {
            var size = sizes[note.Id];
            if (x > margin && x + size.Width > areaPixels.Width - margin)
            { x = margin; y += rowHeight + gap; rowHeight = 0; }
            if (x + size.Width > areaPixels.Width - margin || y + size.Height > areaPixels.Height - margin)
            { overflow = true; break; }
            result.Add(note.Id, FromCorner(areaPixels, corner, x, y, size));
            x += size.Width + gap;
            rowHeight = Math.Max(rowHeight, size.Height);
        }
        if (!overflow) return new(result);

        // Cascade the entire group so the fallback does not depend on where grid overflow occurred.
        result.Clear();
        double offset = 0;
        foreach (var note in ordered)
        {
            var size = sizes[note.Id];
            double insetX = Math.Min(margin, Math.Max(0, (areaPixels.Width - size.Width) / 2));
            double insetY = Math.Min(margin, Math.Max(0, (areaPixels.Height - size.Height) / 2));
            if (insetX + offset + size.Width > areaPixels.Width - insetX
                || insetY + offset + size.Height > areaPixels.Height - insetY) offset = 0;
            result.Add(note.Id, FromCorner(areaPixels, corner, insetX + offset, insetY + offset, size));
            offset += 24 * scale;
        }
        return new(result, UsedCascade: true);
    }

    private static Rect FromCorner(Rect area, ExpandedCorner corner, double x, double y, Size size)
    {
        bool right = corner is ExpandedCorner.TopRight or ExpandedCorner.BottomRight;
        bool bottom = corner is ExpandedCorner.BottomLeft or ExpandedCorner.BottomRight;
        return new Rect(right ? area.Right - x - size.Width : area.Left + x,
            bottom ? area.Bottom - y - size.Height : area.Top + y, size.Width, size.Height);
    }

    public static Note[] OrderNotes(IEnumerable<Note> notes, LayoutSort sort)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (!Enum.IsDefined(sort)) throw new ArgumentOutOfRangeException(nameof(sort));
        var ordered = notes.Where(n => n.IsVisible && n.DeletedAt is null)
            .OrderBy(n => sort == LayoutSort.Color ? ColorOrder(n.Color) : 0)
            .ThenBy(n => sort == LayoutSort.Color && ColorOrder(n.Color) == int.MaxValue ? n.Color : "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => sort == LayoutSort.Title ? n.Title : "", StringComparer.Create(CultureInfo.GetCultureInfo("ko-KR"), false))
            .ThenBy(n => n.CreatedAt).ThenBy(n => n.Id).ToArray();
        if (ordered.Select(n => n.Id).Distinct().Count() != ordered.Length)
            throw new ArgumentException("중복된 메모 ID가 있습니다.", nameof(notes));
        return ordered;
    }

    private static int ColorOrder(string color)
    {
        int index = Array.FindIndex(Colors, c => string.Equals(c, color, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? int.MaxValue : Math.Max(0, index - 1);
    }

    private static string CanonicalColor(string color) => color.Equals("#FFF2B3", StringComparison.OrdinalIgnoreCase)
        ? "#FFF2B2" : color.ToUpperInvariant();

    private static Size GetSize(Note note, double scale, IReadOnlyDictionary<Guid, Size>? sizes)
    {
        Size size;
        if (sizes is not null)
        {
            if (!sizes.TryGetValue(note.Id, out size)) throw new ArgumentException("메모의 실제 창 크기가 누락되었습니다.", nameof(sizes));
        }
        else size = note.IsCollapsed ? new Size(36 * scale, 36 * scale) : new Size(note.Width * scale, note.Height * scale);
        if (size.IsEmpty || !double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0)
            throw new ArgumentException("메모의 창 크기가 올바르지 않습니다.", nameof(sizes));
        return size;
    }

    private static void Validate(Rect area, MonitorLayout options, double scale)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (!double.IsFinite(scale) || scale <= 0 || area.IsEmpty || area.Width <= 0 || area.Height <= 0
            || !double.IsFinite(area.X) || !double.IsFinite(area.Y) || !double.IsFinite(area.Right) || !double.IsFinite(area.Bottom)
            || !double.IsFinite(options.X * scale) || !double.IsFinite(options.Y * scale) || !double.IsFinite(36 * scale))
            throw new ArgumentException("모니터 영역 또는 배율이 올바르지 않습니다.");
    }
}
