using System.Windows;
using Memoit.Models;

namespace Memoit.Services;

public static class TileLayout
{
    private static readonly string[] Colors = ["#FFF2B3", "#FFF2B2", "#FFDDE7", "#DEF0D8", "#DCEBFA", "#EAE0F7", "#FAFAF5"];

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
