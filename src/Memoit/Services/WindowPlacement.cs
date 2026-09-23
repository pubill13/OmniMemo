using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Memoit.Services;

public static class WindowPlacement
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    public static Point GetPosition(Window window)
    {
        if (!GetWindowRect(new WindowInteropHelper(window).Handle, out var rect))
            throw new System.ComponentModel.Win32Exception();
        return new Point(rect.Left, rect.Top);
    }

    public static System.Windows.Rect GetWorkArea(Window window)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(new WindowInteropHelper(window).Handle, 2), ref info))
            throw new System.ComponentModel.Win32Exception();
        return new System.Windows.Rect(info.Work.Left, info.Work.Top,
            info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
    }

    public static void Move(Window window, Point position)
    {
        if (!SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero,
            (int)Math.Round(position.X), (int)Math.Round(position.Y), 0, 0, 0x0015))
            throw new System.ComponentModel.Win32Exception();
    }

    public static void Cascade(Window window, Window source)
    {
        if (!GetWindowRect(new WindowInteropHelper(window).Handle, out var targetRect)
            || !GetWindowRect(new WindowInteropHelper(source).Handle, out var sourceRect))
            throw new System.ComponentModel.Win32Exception();
        var scale = System.Windows.Media.VisualTreeHelper.GetDpi(source).DpiScaleX;
        Move(window, NotePlacement.Cascade(
            new System.Windows.Rect(sourceRect.Left, sourceRect.Top, sourceRect.Right - sourceRect.Left, sourceRect.Bottom - sourceRect.Top),
            new Size(targetRect.Right - targetRect.Left, targetRect.Bottom - targetRect.Top), GetWorkArea(source), 28 * scale));
    }

    public static void KeepOnScreen(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect)) return;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info)) return;
        int width = Math.Min(rect.Right - rect.Left, info.Work.Right - info.Work.Left);
        int height = Math.Min(rect.Bottom - rect.Top, info.Work.Bottom - info.Work.Top);
        int x = Math.Clamp(rect.Left, info.Work.Left, info.Work.Right - width);
        int y = Math.Clamp(rect.Top, info.Work.Top, info.Work.Bottom - height);
        SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, 0x0014);
    }
}
