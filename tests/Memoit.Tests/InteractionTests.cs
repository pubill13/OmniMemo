using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Memoit.Models;
using Memoit.Services;
using Memoit.ViewModels;
using Memoit.Views;
using Xunit;

namespace Memoit.Tests;

[Collection("WPF")]
public sealed class InteractionTests
{
    [Theory]
    [InlineData(-9, 150, 0, 150)]
    [InlineData(-11, 150, -11, 150)]
    [InlineData(909, 150, 900, 150)]
    [InlineData(911, 150, 911, 150)]
    [InlineData(150, -11, 150, -11)]
    [InlineData(150, 711, 150, 711)]
    public void DragBeyondMagnetRangeAllowsPartialOffscreenPlacement(double x, double y, double expectedX, double expectedY)
        => Assert.Equal(new Point(expectedX, expectedY), WindowSnapper.Snap(
            new Rect(x, y, 100, 100), new Rect(0, 0, 1000, 800), [], clampToWorkArea: false));

    [Theory]
    [InlineData(10, 150, 0, 150)]
    [InlineData(11, 150, 11, 150)]
    [InlineData(891, 150, 900, 150)]
    [InlineData(150, 10, 150, 0)]
    [InlineData(150, 691, 150, 700)]
    public void ScreenEdgesUseTenDipThreshold(double x, double y, double expectedX, double expectedY)
        => Assert.Equal(new Point(expectedX, expectedY), WindowSnapper.Snap(new Rect(x, y, 100, 100), new Rect(0, 0, 1000, 800), []));

    [Theory]
    [InlineData(291, 450, 300, 450)]
    [InlineData(509, 450, 500, 450)]
    [InlineData(405, 450, 400, 450)]
    [InlineData(450, 291, 450, 300)]
    [InlineData(450, 509, 450, 500)]
    [InlineData(450, 405, 450, 400)]
    public void PeerEdgesAlignAndAbutWhenOtherAxisOverlaps(double x, double y, double expectedX, double expectedY)
        => Assert.Equal(new Point(expectedX, expectedY), WindowSnapper.Snap(new Rect(x, y, 100, 100), new Rect(0, 0, 1000, 800), [new Rect(400, 400, 100, 100)]));

    [Fact]
    public void DistantPeerDoesNotAttract()
        => Assert.Equal(new Point(405, 150), WindowSnapper.Snap(new Rect(405, 150, 100, 100), new Rect(0, 0, 1000, 800), [new Rect(400, 500, 100, 100)]));

    [Theory]
    [InlineData(320, 300, 1, 0)]
    [InlineData(320, 300, -1, 0)]
    [InlineData(320, 300, 0, 1)]
    [InlineData(320, 300, 0, -1)]
    [InlineData(36, 36, 1, 0)]
    [InlineData(36, 36, -1, 0)]
    [InlineData(36, 36, 0, 1)]
    [InlineData(36, 36, 0, -1)]
    public void ActiveDragCanCrossEverySharedMonitorBoundary(double width, double height, int dx, int dy)
    {
        var origin = new Rect(-1000, -800, 1000, 800);
        var destination = new Rect(origin.X + dx * 1000, origin.Y + dy * 800, 1000, 800);
        var start = new Point(origin.X + 400, origin.Y + 300);
        var end = new Point(destination.X + 400, destination.Y + 300);
        bool crossed = false;
        for (int i = 0; i <= 100; i++)
        {
            var proposed = new Rect(start + (end - start) * (i / 100.0), new Size(width, height));
            var overlapOrigin = Rect.Intersect(proposed, origin);
            var overlapDestination = Rect.Intersect(proposed, destination);
            double originArea = overlapOrigin.IsEmpty ? 0 : overlapOrigin.Width * overlapOrigin.Height;
            double destinationArea = overlapDestination.IsEmpty ? 0 : overlapDestination.Width * overlapDestination.Height;
            var dominant = destinationArea > originArea ? destination : origin;
            var snapped = WindowSnapper.Snap(proposed, dominant, [], clampToWorkArea: false);
            // Resistance is limited to the magnetic threshold, even while straddling displays.
            Assert.InRange(Math.Abs(snapped.X - proposed.X), 0, WindowSnapper.Distance);
            Assert.InRange(Math.Abs(snapped.Y - proposed.Y), 0, WindowSnapper.Distance);
            crossed |= destination.Contains(new Rect(snapped, proposed.Size));
        }
        Assert.True(crossed);
        var released = WindowSnapper.Snap(new Rect(end, new Size(width, height)), destination, []);
        Assert.True(destination.Contains(new Rect(released, new Size(width, height))));
    }

    [Fact]
    public void NegativeMonitorCoordinatesAndOversizedWindowAreSafe()
    {
        var area = new Rect(-1920, -200, 1920, 1080);
        Assert.Equal(new Point(-1920, -200), WindowSnapper.Snap(new Rect(-1911, -195, 100, 100), area, []));
        Assert.Equal(area.TopLeft, WindowSnapper.Snap(new Rect(-1000, 0, 2400, 1500), area, []));
    }

    [Fact]
    public Task CollapsedMovesPreserveExpandedSizeAndContent() => OnDispatcher(async () =>
    {
        using var vm = new NoteViewModel(new Note { Body = "보존할 내용", Width = 480, Height = 360 }, _ => Task.CompletedTask);
        vm.IsCollapsed = true;
        vm.UpdateBounds(200, 240, 36, 36);
        vm.UpdatePosition(260, 280);
        Assert.Equal(480, vm.Snapshot.Width);
        Assert.Equal(360, vm.Snapshot.Height);
        Assert.Equal(260, vm.Snapshot.Left);
        Assert.Equal(280, vm.Snapshot.Top);
        vm.IsCollapsed = false;
        Assert.Equal("보존할 내용", vm.Body);
        Assert.Equal(480, vm.Snapshot.Width);
        Assert.True(await vm.FlushAsync());
    });

    [Fact]
    public Task MissingFontFallsBackWithoutDestroyingSavedChoice() => OnDispatcher(() =>
    {
        const string missingFont = "OmniMemo missing font 42c1d945";
        using var vm = new NoteViewModel(new Note { FontFamily = missingFont }, _ => Task.CompletedTask);
        Assert.Equal("Malgun Gothic", vm.EffectiveFontFamily);
        Assert.Equal(missingFont, vm.FontFamily);
        Assert.Equal(missingFont, vm.Snapshot.FontFamily);
        vm.FontFamily = "Arial";
        Assert.Equal("Arial", vm.EffectiveFontFamily);
        return Task.CompletedTask;
    });

    [Fact]
    public Task SaveStateTracksFailureExplicitRetryAndNextEdit() => OnDispatcher(async () =>
    {
        int attempts = 0;
        var retry = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var vm = new NoteViewModel(new Note(), _ => ++attempts == 1
            ? Task.FromException(new IOException("디스크 쓰기 실패")) : retry.Task);
        Assert.Equal("Saved", vm.SaveState);
        vm.Body = "첫 편집";
        Assert.Equal("Saving", vm.SaveState);
        Assert.False(await vm.FlushAsync());
        Assert.Equal("Failed", vm.SaveState);
        Assert.Contains("디스크 쓰기 실패", vm.SaveStatus);
        Task<bool> pending = vm.FlushAsync();
        Assert.Equal("Saving", vm.SaveState);
        retry.SetResult();
        Assert.True(await pending);
        Assert.Equal("Saved", vm.SaveState);
        vm.Body = "다음 편집";
        Assert.Equal("Saving", vm.SaveState);
        Assert.True(await vm.FlushAsync());
    });

    [Fact]
    public Task AllAppWindowsOmitTaskbarEvenWithNativeHandles() => OnDispatcher(() =>
    {
        using var vm = new NoteViewModel(new Note(), _ => Task.CompletedTask);
        Window[] windows = [new MainWindow { AllowClose = true }, new SettingsWindow(false, Path.GetTempPath()), new NoteWindow(vm) { AllowClose = true }];
        try
        {
            foreach (var window in windows)
            {
                Assert.False(window.ShowInTaskbar);
                window.ShowActivated = false;
                window.Show();
                var handle = new WindowInteropHelper(window).Handle;
                Assert.NotEqual(IntPtr.Zero, handle);
                Assert.Equal(0, GetWindowLong(handle, -20) & 0x00040000); // WS_EX_APPWINDOW
            }
        }
        finally { foreach (var window in windows) window.Close(); }
        return Task.CompletedTask;
    });

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [Fact]
    public Task NativeMovingMessageSnapsProposedRectangleBeforeMouseRelease() => OnDispatcher(() =>
    {
        using var vm = new NoteViewModel(new Note(), _ => Task.CompletedTask);
        var window = new NoteWindow(vm) { AllowClose = true, ShowActivated = false };
        IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try
        {
            window.Show();
            var hwnd = new WindowInteropHelper(window).Handle;
            using var magnet = new WindowMagnet(window, () => [window]);
            var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            Assert.True(GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref monitor));
            var proposed = new NativeRect { Left = monitor.Work.Left + 5, Top = monitor.Work.Top + 150,
                Right = monitor.Work.Left + 325, Bottom = monitor.Work.Top + 450 };
            Marshal.StructureToPtr(proposed, memory, false);
            SendMessage(hwnd, 0x0216, IntPtr.Zero, memory);
            var snapped = Marshal.PtrToStructure<NativeRect>(memory);
            Assert.Equal(monitor.Work.Left, snapped.Left);
            Assert.Equal(320, snapped.Right - snapped.Left);
            Assert.Equal(proposed.Top, snapped.Top);
            // A proposed drag across the top-left edge must not be clamped to this monitor.
            proposed = new NativeRect { Left = monitor.Work.Left - 80, Top = monitor.Work.Top - 80,
                Right = monitor.Work.Left + 240, Bottom = monitor.Work.Top + 220 };
            Marshal.StructureToPtr(proposed, memory, false);
            SendMessage(hwnd, 0x0216, IntPtr.Zero, memory);
            snapped = Marshal.PtrToStructure<NativeRect>(memory);
            Assert.InRange(Math.Abs(snapped.Left - proposed.Left), 0, 30);
            Assert.InRange(Math.Abs(snapped.Top - proposed.Top), 0, 30);
        }
        finally { Marshal.FreeHGlobal(memory); window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RepeatedNativeSnapFeedbackDoesNotShiftOriginalCursorGrip() => OnDispatcher(() =>
    {
        using var vm = new NoteViewModel(new Note(), _ => Task.CompletedTask);
        var window = new NoteWindow(vm) { AllowClose = true, ShowActivated = false };
        IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try
        {
            window.Show();
            var hwnd = new WindowInteropHelper(window).Handle;
            Assert.True(GetWindowRect(hwnd, out var initial));
            var area = WindowPlacement.GetWorkArea(window);
            var grip = new Vector(100, 11);
            var cursor = new Point(initial.Left, initial.Top) + grip;
            using var magnet = new WindowMagnet(window, () => [window], () => cursor);
            SendMessage(hwnd, 0x0231, IntPtr.Zero, IntPtr.Zero);
            var proposed = initial;
            // Feed each adjusted rectangle back into the next native proposal, as the
            // Windows move loop may do. The cursor must release in either direction.
            foreach (int x in Enumerable.Range(-40, 81).Concat(Enumerable.Range(-40, 81).Reverse()))
            {
                cursor = new Point(area.Left + x, area.Top + 200) + grip;
                Marshal.StructureToPtr(proposed, memory, false);
                SendMessage(hwnd, 0x0216, IntPtr.Zero, memory);
                proposed = Marshal.PtrToStructure<NativeRect>(memory);
                var expected = WindowSnapper.Snap(new Rect(cursor - grip,
                    new Size(initial.Right - initial.Left, initial.Bottom - initial.Top)), area, [],
                    WindowSnapper.Distance * System.Windows.Media.VisualTreeHelper.GetDpi(window).DpiScaleX,
                    clampToWorkArea: false);
                Assert.Equal((int)Math.Round(expected.X), proposed.Left);
                Assert.Equal((int)Math.Round(expected.Y), proposed.Top);
            }
            SendMessage(hwnd, 0x0232, IntPtr.Zero, IntPtr.Zero);
            // A second drag must establish a new grip rather than retaining the old one.
            cursor = new Point(initial.Left + 20, initial.Top + 15);
            SendMessage(hwnd, 0x0231, IntPtr.Zero, IntPtr.Zero);
            cursor += new Vector(60, 80);
            Marshal.StructureToPtr(initial, memory, false);
            SendMessage(hwnd, 0x0216, IntPtr.Zero, memory);
            proposed = Marshal.PtrToStructure<NativeRect>(memory);
            var secondExpected = WindowSnapper.Snap(new Rect(initial.Left + 60, initial.Top + 80,
                initial.Right - initial.Left, initial.Bottom - initial.Top), area, [],
                WindowSnapper.Distance * System.Windows.Media.VisualTreeHelper.GetDpi(window).DpiScaleX,
                clampToWorkArea: false);
            Assert.Equal((int)Math.Round(secondExpected.X), proposed.Left);
            Assert.Equal((int)Math.Round(secondExpected.Y), proposed.Top);
            SendMessage(hwnd, 0x0232, IntPtr.Zero, IntPtr.Zero);
        }
        finally { Marshal.FreeHGlobal(memory); window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task CollapsedPhysicalMoveSnapsBesidePeerWithoutResizingEitherSnapshot() => OnDispatcher(async () =>
    {
        using var vm = new NoteViewModel(new Note { IsCollapsed = true, Width = 480, Height = 360 }, _ => Task.CompletedTask);
        using var peerVm = new NoteViewModel(new Note { IsCollapsed = true, Width = 420, Height = 320 }, _ => Task.CompletedTask);
        var window = new NoteWindow(vm) { AllowClose = true, ShowActivated = false };
        var peer = new NoteWindow(peerVm) { AllowClose = true, ShowActivated = false };
        try
        {
            window.Show(); peer.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var hwnd = new WindowInteropHelper(window).Handle;
            var peerHandle = new WindowInteropHelper(peer).Handle;
            var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            Assert.True(GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref monitor));
            Assert.True(SetWindowPos(peerHandle, IntPtr.Zero, monitor.Work.Left + 200, monitor.Work.Top + 200, 0, 0, 0x0015));
            Assert.True(GetWindowRect(peerHandle, out var peerRect));
            Assert.True(GetWindowRect(hwnd, out var originalRect));
            using var magnet = new WindowMagnet(window, () => [window, peer]);
            // MoveTo receives physical pixels from PointToScreen, not WPF device-independent units.
            magnet.MoveTo(new Point(peerRect.Right + 5, peerRect.Top + 4));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(GetWindowRect(hwnd, out var moved));
            Assert.Equal(peerRect.Right, moved.Left);
            Assert.Equal(peerRect.Top, moved.Top);
            Assert.Equal(originalRect.Right - originalRect.Left, moved.Right - moved.Left);
            Assert.Equal(originalRect.Bottom - originalRect.Top, moved.Bottom - moved.Top);
            Assert.Equal(36, window.Width);
            Assert.Equal(36, window.Height);
            Assert.Equal(480, vm.Snapshot.Width);
            Assert.Equal(360, vm.Snapshot.Height);
            Assert.Equal(window.Left, vm.Snapshot.Left);
            Assert.Equal(window.Top, vm.Snapshot.Top);
            Assert.True(vm.IsCollapsed);
            Assert.Equal(420, peerVm.Snapshot.Width);
            Assert.Equal(320, peerVm.Snapshot.Height);
        }
        finally { window.Close(); peer.Close(); }
    });

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    private static Task OnDispatcher(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await action(); completion.SetResult(); }
                catch (Exception error) { completion.SetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }
}
