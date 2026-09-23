using System.Windows;

namespace Memoit.Services;

public static class WindowSnapper
{
    public const double Distance = 10;

    public static Point Snap(Rect candidate, Rect workArea, IEnumerable<Rect> peers, double distance = Distance)
    {
        double x = candidate.Left, y = candidate.Top;
        var targetsX = new List<double> { workArea.Left, workArea.Right - candidate.Width };
        var targetsY = new List<double> { workArea.Top, workArea.Bottom - candidate.Height };
        foreach (var peer in peers)
        {
            // Only nearby rows/columns attract: a note across the screen must not pull this one.
            if (candidate.Bottom >= peer.Top - distance && candidate.Top <= peer.Bottom + distance)
                targetsX.AddRange([peer.Left - candidate.Width, peer.Right, peer.Left, peer.Right - candidate.Width]);
            if (candidate.Right >= peer.Left - distance && candidate.Left <= peer.Right + distance)
                targetsY.AddRange([peer.Top - candidate.Height, peer.Bottom, peer.Top, peer.Bottom - candidate.Height]);
        }
        var tx = targetsX.OrderBy(v => Math.Abs(v - x)).First();
        var ty = targetsY.OrderBy(v => Math.Abs(v - y)).First();
        if (Math.Abs(tx - x) <= distance) x = tx;
        if (Math.Abs(ty - y) <= distance) y = ty;
        x = Math.Clamp(x, workArea.Left, Math.Max(workArea.Left, workArea.Right - candidate.Width));
        y = Math.Clamp(y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - candidate.Height));
        return new Point(x, y);
    }
}
