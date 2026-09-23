using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Memoit.Services;

namespace Memoit.Views;

public sealed class AnchorPickerWindow : Window
{
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    private readonly MonitorDescriptor monitor;
    private readonly Border tile = new() { Width = 36, Height = 36, Background = Brushes.LightGoldenrodYellow, BorderBrush = Brushes.White, BorderThickness = new Thickness(1), IsHitTestVisible = false };
    private readonly Canvas canvas = new();
    public Point? SelectedPoint { get; private set; }

    public AnchorPickerWindow(MonitorDescriptor monitor)
    {
        this.monitor = monitor;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/OmniMemo;component/Assets/OmniMemo.ico"));
        Title = "시작 위치 선택"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = true; AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(85, 0, 0, 0)); Cursor = Cursors.Cross;
        var hint = new TextBlock { Text = "첫 메모 위치를 클릭하세요 · Esc 취소", Foreground = Brushes.White, Background = Brushes.Black, Padding = new Thickness(12), IsHitTestVisible = false };
        Canvas.SetLeft(hint, 16); Canvas.SetTop(hint, 16); canvas.Children.Add(hint); canvas.Children.Add(tile); Content = canvas;
        SourceInitialized += (_, _) =>
        {
            var area = monitor.WorkArea;
            if (!SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), (int)area.X, (int)area.Y, (int)area.Width, (int)area.Height, 0x0040))
                throw new System.ComponentModel.Win32Exception();
        };
        MouseMove += (_, e) => MovePreview(e.GetPosition(canvas));
        MouseLeftButtonDown += (_, e) => { SelectedPoint = Clamp(e.GetPosition(canvas)); DialogResult = true; };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; DialogResult = false; } };
    }
    private Point Clamp(Point point) => new(Math.Clamp(point.X, 0, Math.Max(0, ActualWidth - 36)), Math.Clamp(point.Y, 0, Math.Max(0, ActualHeight - 36)));
    private void MovePreview(Point point)
    {
        point = Clamp(point); Canvas.SetLeft(tile, point.X); Canvas.SetTop(tile, point.Y);
    }
}
