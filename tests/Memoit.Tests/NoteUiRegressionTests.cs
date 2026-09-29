using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Memoit.Models;
using Memoit.ViewModels;
using Memoit.Views;
using Memoit.Services;
using Xunit;

namespace Memoit.Tests;

[Collection("WPF")]
public sealed class NoteUiRegressionTests
{
    [Fact]
    public void IndividualExpandUsesClickedTileAndCollapseReturnsToTile() => Sta(() =>
    {
        using var vm = new NoteViewModel(new Note { Left = 160, Top = 170, IsCollapsed = true,
            ExpandedLeft = 600, ExpandedTop = 500, Width = 300, Height = 220 }, _ => Task.CompletedTask);
        var window = new NoteWindow(vm);
        try
        {
            window.Show(); Pump();
            var tile = WindowPlacement.GetPosition(window);
            window.ToggleCollapsed(false); Pump();
            Assert.Equal(tile, WindowPlacement.GetPosition(window));
            window.Left += 100; window.Top += 80; Pump();
            window.ToggleCollapsed(false); Pump();
            Assert.Equal(tile, WindowPlacement.GetPosition(window));
        }
        finally { window.AllowClose = true; window.Close(); }
    });

    [Fact]
    public void EdgeTileExpandsInsideItsMonitorAndReturnsToItsOriginalSpot() => Sta(() =>
    {
        using var vm = new NoteViewModel(new Note { IsCollapsed = true, Width = 310, Height = 230 }, _ => Task.CompletedTask);
        var window = new NoteWindow(vm);
        try
        {
            window.Show(); Pump();
            var work = WindowPlacement.GetWorkArea(window);
            var tileSize = WindowPlacement.GetSize(window);
            var tile = new Point(work.Right - tileSize.Width, work.Bottom - tileSize.Height);
            WindowPlacement.Move(window, tile); Pump();
            window.ToggleCollapsed(false); Pump();
            Assert.True(work.Contains(WindowPlacement.GetBounds(window)));
            window.ToggleCollapsed(false); Pump();
            Assert.Equal(tile, WindowPlacement.GetPosition(window));
        }
        finally { window.AllowClose = true; window.Close(); }
    });

    [Fact]
    public void BatchStateWaitsForFinalNativeBoundsAndDoesNotFocusEditor() => Sta(() =>
    {
        using var vm = new NoteViewModel(new Note { Left = 160, Top = 170, IsCollapsed = true,
            ExpandedLeft = 700, ExpandedTop = 500, Width = 400, Height = 300 }, _ => Task.CompletedTask);
        var window = new NoteWindow(vm);
        try
        {
            window.Show(); Pump();
            ((UIElement)window.FindName("CollapsedTile")).Focus();
            var original = WindowPlacement.GetPosition(window);
            var originalBounds = WindowPlacement.GetBounds(window);
            int sizeChanges = 0;
            window.SizeChanged += (_, _) => sizeChanges++;
            var dpi = VisualTreeHelper.GetDpi(window);
            var target = new Rect(original.X + 40, original.Y + 50, 380 * dpi.DpiScaleX, 280 * dpi.DpiScaleY);
            window.PrepareState(false, target);
            Assert.Equal(original, WindowPlacement.GetPosition(window));
            Assert.Equal(originalBounds, WindowPlacement.GetBounds(window));
            Assert.False(window.EditorHasFocus);
            WindowPlacement.MoveTogether([(window, target)]);
            window.EndArrangement(); Pump();
            Assert.Equal(target, WindowPlacement.GetBounds(window));
            Assert.Equal(380, window.Width); Assert.Equal(280, window.Height);
            Assert.Equal(window.Left, vm.Snapshot.Left); Assert.Equal(window.Top, vm.Snapshot.Top);
            Assert.Equal(380, vm.Snapshot.Width); Assert.Equal(280, vm.Snapshot.Height);
            Assert.True(window.IsEnabled); Assert.False(window.EditorHasFocus);
            Assert.Equal(1, sizeChanges);
        }
        finally { window.AllowClose = true; window.Close(); }
    });

    [Fact]
    public void CollapsedMoveReopenAndExpandKeepActualSizeAndContent() => Sta(() =>
    {
        var note = new Note { Body = "업무 메모 😊\n접어도 그대로 유지", Width = 410, Height = 370, IsCollapsed = true, FontFamily = "Consolas", FontSize = 20 };
        using var vm = new NoteViewModel(note, _ => Task.CompletedTask);
        var window = new NoteWindow(vm);
        try
        {
            window.Show(); Pump();
            Assert.Equal(36, window.ActualWidth); Assert.Equal(36, window.ActualHeight);
            Assert.False(window.Editor.IsVisible);
            window.Left = 430; window.Top = 250; Pump();
            Assert.Equal(430, vm.Snapshot.Left); Assert.Equal(250, vm.Snapshot.Top);
            Assert.Equal(410, vm.Snapshot.Width); Assert.Equal(370, vm.Snapshot.Height);
            window.Hide(); window.Show(); Pump();
            Assert.Equal(36, window.ActualWidth);
            window.ToggleCollapsed(); Pump();
            Assert.Equal(410, window.ActualWidth); Assert.Equal(370, window.ActualHeight);
            Assert.Equal(430, window.Left); Assert.Equal(250, window.Top);
            Assert.Equal(note.Body, window.Editor.Text);
            Assert.Equal(20, window.Editor.FontSize);
            Assert.Equal("Consolas", window.Editor.FontFamily.Source);
            window.Editor.CaretIndex = 3;
            window.ToggleCollapsed(); window.ToggleCollapsed(); Pump();
            Assert.Equal(3, window.Editor.CaretIndex);
        }
        finally { window.AllowClose = true; window.Close(); }
    });

    [Fact]
    public void ThinScrollbarHasWorkingTrackAndThemeAndFailureDot() => Sta(() =>
    {
        bool fail = false;
        using var vm = new NoteViewModel(new Note
        {
            Body = "작은 생각을 놓치지 않도록\n\n" + string.Join("\n", Enumerable.Range(1, 25).Select(i => $"{i:00}  오늘의 할 일과 아이디어")),
            Color = "#DCEBFA", FontSize = 16
        }, _ => fail ? Task.FromException(new IOException("테스트 저장 오류")) : Task.CompletedTask);
        var window = new NoteWindow(vm);
        try
        {
            window.Show(); Pump();
            var root = (Grid)window.FindName("Root");
            Assert.Equal(22, root.RowDefinitions[0].ActualHeight);
            Assert.Equal(new Thickness(8, 7, 8, 7), window.Editor.Padding);
            var pin = Assert.Single(Descendants<ToggleButton>(root), button => button is not CheckBox);
            var head = (System.Windows.Shapes.Path)pin.Template.FindName("PinHead", pin);
            Assert.Equal(Colors.Transparent, ((SolidColorBrush)head.Fill).Color);
            pin.IsChecked = true; Pump();
            Assert.True(vm.IsPinned);
            Assert.True(window.Topmost);
            Assert.NotEqual(Colors.Transparent, ((SolidColorBrush)head.Fill).Color);
            pin.IsChecked = false; Pump();
            Assert.False(window.Topmost);
            Assert.Empty(Descendants<CheckBox>(root));
            Assert.Equal(2, root.RowDefinitions.Count);
            var viewer = Descendants<ScrollViewer>(window.Editor).Single();
            var bar = Descendants<ScrollBar>(window.Editor).Single(b => b.Orientation == Orientation.Vertical);
            Assert.Equal(6, bar.ActualWidth);
            Assert.True(bar.Maximum > 0);
            var track = (Track)bar.Template.FindName("PART_Track", bar);
            Assert.True(track.IsDirectionReversed);
            Assert.True(track.ViewportSize > 0);
            viewer.ScrollToVerticalOffset(90); Pump();
            Assert.True(bar.Value > 0); Assert.Equal(bar.Value, track.Value);
            viewer.ScrollToVerticalOffset(0); Pump();
            var thumb = track.Thumb;
            var surface = (Border)thumb.Template.FindName("ThumbSurface", thumb);
            var first = ((SolidColorBrush)surface.Background).Color;
            vm.Color = "#FFDDE7"; Pump();
            Assert.NotEqual(first, ((SolidColorBrush)surface.Background).Color);
            thumb.Tag = true; Pump();
            Assert.Equal(SystemColors.WindowTextColor, ((SolidColorBrush)surface.Background).Color);
            Assert.Equal(1, surface.Opacity);
            thumb.ClearValue(FrameworkElement.TagProperty); Pump();
            Assert.Equal(.3, surface.Opacity);
            Assert.True(vm.FlushAsync().GetAwaiter().GetResult()); Pump();
            Render(window, "expanded.png");
            window.ToggleCollapsed(); Pump(); Render(window, "collapsed.png");
            window.ToggleCollapsed();
            fail = true; vm.Body += "\n저장 실패 검증";
            Assert.False(vm.FlushAsync().GetAwaiter().GetResult()); Pump();
            var dot = Descendants<Ellipse>(root).Single();
            Assert.Equal(Color.FromRgb(193, 61, 61), ((SolidColorBrush)dot.Fill).Color);
            Assert.Contains("테스트 저장 오류", dot.ToolTip.ToString());
            Render(window, "save-error.png");
        }
        finally { window.AllowClose = true; window.Close(); }
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Render(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OMNIMEMO_VISUAL_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(System.IO.Path.Combine(directory, name)); encoder.Save(file);
    }
    private static void Sta(Action test)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception ex) { error = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "WPF test timed out");
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
