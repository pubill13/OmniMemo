using System.Windows;

namespace Memoit.Services;

public static class NotePlacement
{
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
