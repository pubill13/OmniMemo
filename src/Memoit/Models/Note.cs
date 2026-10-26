namespace Memoit.Models;

public sealed record Note
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Body { get; init; } = "";
    public string Color { get; init; } = "#FFF2B2";
    public double FontSize { get; init; } = 16;
    public string FontFamily { get; init; } = "Malgun Gothic";
    public bool IsCollapsed { get; init; }
    public double Left { get; init; } = 100;
    public double Top { get; init; } = 100;
    public double? CollapsedLeft { get; init; }
    public double? CollapsedTop { get; init; }
    public double? ExpandedLeft { get; init; }
    public double? ExpandedTop { get; init; }
    public double Width { get; init; } = 320;
    public double Height { get; init; } = 300;
    public bool IsVisible { get; init; } = true;
    public bool IsPinned { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; init; }
    public string Title
    {
        get
        {
            // Titles need only a short slice; don't allocate whole lines from large pasted notes.
            ReadOnlySpan<char> remaining = Body.AsSpan();
            while (!remaining.IsEmpty)
            {
                int end = remaining.IndexOfAny('\r', '\n');
                ReadOnlySpan<char> line = (end < 0 ? remaining : remaining[..end]).Trim();
                if (!line.IsEmpty) return line[..Math.Min(80, line.Length)].ToString();
                if (end < 0) break;
                remaining = remaining[(end + 1)..];
            }
            return "새 메모";
        }
    }
}
