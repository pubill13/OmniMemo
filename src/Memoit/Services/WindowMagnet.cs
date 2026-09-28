using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Memoit.Services;

/// <summary>Applies magnetism to an absolute cursor-anchored position without accumulating snap offsets.</summary>
public sealed class WindowMagnet : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rect ToRect() => new(Left, Top, Right - Left, Bottom - Top);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private readonly HwndSource source;
    private readonly Func<IEnumerable<Window>> peers;
    private readonly Func<Point> cursorPosition;
    private Vector? dragOffset;

    public WindowMagnet(Window window, Func<IEnumerable<Window>> peers, Func<Point>? cursorPosition = null)
    {
        this.peers = peers;
        this.cursorPosition = cursorPosition ?? GetCursorPosition;
        source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)
            ?? throw new InvalidOperationException("창 핸들이 생성된 후 자석 정렬을 연결해야 합니다.");
        source.AddHook(OnMessage);
    }

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int EnterSizeMove = 0x0231, ExitSizeMove = 0x0232;
        const int Moving = 0x0216;
        if (message == EnterSizeMove)
        {
            dragOffset = GetWindowRect(hwnd, out var initial)
                ? cursorPosition() - new Point(initial.Left, initial.Top) : null;
            return IntPtr.Zero;
        }
        if (message == ExitSizeMove) { dragOffset = null; return IntPtr.Zero; }
        if (message != Moving) return IntPtr.Zero;
        var proposed = Marshal.PtrToStructure<NativeRect>(lParam);
        if (dragOffset is Vector offset)
        {
            // Windows can base its next proposal on our previously snapped rectangle.
            // Always recover the unsnapped position from the original grip instead.
            var position = cursorPosition() - offset;
            int width = proposed.Right - proposed.Left, height = proposed.Bottom - proposed.Top;
            proposed.Left = (int)Math.Round(position.X); proposed.Top = (int)Math.Round(position.Y);
            proposed.Right = proposed.Left + width; proposed.Bottom = proposed.Top + height;
        }
        proposed = Snap(hwnd, proposed);
        Marshal.StructureToPtr(proposed, lParam, false);
        handled = true;
        return new IntPtr(1);
    }

    public static Point GetCursorPosition()
    {
        if (!GetCursorPos(out var point)) throw new System.ComponentModel.Win32Exception();
        return new Point(point.X, point.Y);
    }

    public void MoveTo(Point position)
    {
        if (!GetWindowRect(source.Handle, out var rect)) return;
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        rect.Left = (int)Math.Round(position.X); rect.Top = (int)Math.Round(position.Y);
        rect.Right = rect.Left + width; rect.Bottom = rect.Top + height;
        rect = Snap(source.Handle, rect);
        SetWindowPos(source.Handle, IntPtr.Zero, rect.Left, rect.Top, 0, 0, 0x0015);
    }

    private NativeRect Snap(IntPtr hwnd, NativeRect proposed)
    {
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromRect(ref proposed, 2), ref monitor)) return proposed;
        var others = new List<Rect>();
        foreach (var peer in peers())
        {
            var handle = new WindowInteropHelper(peer).Handle;
            if (handle != hwnd && handle != IntPtr.Zero && GetWindowRect(handle, out var rect)) others.Add(rect.ToRect());
        }
        var point = WindowSnapper.Snap(proposed.ToRect(), monitor.Work.ToRect(), others,
            WindowSnapper.Distance * Math.Max(96, GetDpiForWindow(hwnd)) / 96.0, clampToWorkArea: false);
        int dx = (int)Math.Round(point.X) - proposed.Left, dy = (int)Math.Round(point.Y) - proposed.Top;
        proposed.Left += dx; proposed.Right += dx; proposed.Top += dy; proposed.Bottom += dy;
        return proposed;
    }

    public void Dispose() => source.RemoveHook(OnMessage);
}
