using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace Memoit.Services;

public sealed record MonitorDescriptor(string Id, string Name, Rect WorkArea, double Scale);

public static class MonitorCatalog
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice display, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);

    public static IReadOnlyList<MonitorDescriptor> All() => Forms.Screen.AllScreens.Select(Describe).ToArray();

    public static MonitorDescriptor ForWindow(Window window)
        => Describe(Forms.Screen.FromHandle(new WindowInteropHelper(window).Handle)) with
        { Scale = System.Windows.Media.VisualTreeHelper.GetDpi(window).DpiScaleX };

    private static MonitorDescriptor Describe(Forms.Screen screen)
    {
        var display = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
        bool found = EnumDisplayDevices(screen.DeviceName, 0, ref display, 1);
        string id = found && !string.IsNullOrWhiteSpace(display.Id) ? display.Id : screen.DeviceName;
        var area = screen.WorkingArea;
        var monitor = MonitorFromPoint(new NativePoint { X = area.Left + area.Width / 2, Y = area.Top + area.Height / 2 }, 2);
        double scale = GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 ? dpi / 96.0 : 1;
        return new MonitorDescriptor(id, screen.DeviceName + (screen.Primary ? " (주 화면)" : ""),
            new Rect(area.Left, area.Top, area.Width, area.Height), scale);
    }
}
