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
            using var lines = new StringReader(Body);
            while (lines.ReadLine() is { } line)
                if (!string.IsNullOrWhiteSpace(line)) return line.Trim()[..Math.Min(80, line.Trim().Length)];
            return "새 메모";
        }
    }
}
