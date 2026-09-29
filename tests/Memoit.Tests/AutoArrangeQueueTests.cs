using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Memoit.Models;
using Memoit.Services;
using Memoit.ViewModels;
using Memoit.Views;
using Xunit;

namespace Memoit.Tests;

[Collection("WPF")]
public sealed class AutoArrangeQueueTests
{
    [Fact]
    public Task EditingDefersRequestsWithoutChangingEditorOrSelection() => Sta(async () =>
    {
        using var vm = new NoteViewModel(new Note(), _ => Task.CompletedTask);
        var window = new NoteWindow(vm) { AllowClose = true };
        try
        {
            window.Show(); window.Activate(); window.Editor.Focus();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(window.EditorHasFocus);
            int calls = 0, disabled = 0;
            window.IsEnabledChanged += (_, _) => { if (!window.IsEnabled) disabled++; };
            var queue = new AutoArrangeQueue(Dispatcher.CurrentDispatcher, () => !window.EditorHasFocus,
                () => { calls++; return Task.CompletedTask; }, error => throw error);
            foreach (var value in new[] { "가", "가나", "가나다", "가나다\n본문" })
            {
                window.Editor.Text = value;
                window.Editor.Select(0, 1);
                queue.Request();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(0, calls); Assert.Equal(0, disabled);
                Assert.True(window.EditorHasFocus);
                Assert.Equal(0, window.Editor.SelectionStart); Assert.Equal(1, window.Editor.SelectionLength);
            }
            window.Hide(); queue.Resume();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(1, calls);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task RequestsDuringSaveCoalesceAndWaitForInteractionEnd() => Sta(async () =>
    {
        bool interaction = false;
        int calls = 0;
        var saving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = new AutoArrangeQueue(Dispatcher.CurrentDispatcher, () => !interaction,
            async () => { calls++; await saving.Task; }, error => throw error);
        queue.Request();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal(1, calls);
        interaction = true;
        for (int i = 0; i < 20; i++) queue.Request();
        saving.SetResult();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal(1, calls);
        interaction = false; queue.Resume();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal(2, calls);
    });

    [Fact]
    public Task ManualMoveCancelsQueuedLayoutAndFailureDoesNotLoop() => Sta(async () =>
    {
        int calls = 0, errors = 0;
        var queue = new AutoArrangeQueue(Dispatcher.CurrentDispatcher, () => true,
            () => { calls++; throw new IOException("disk full"); }, _ => errors++);
        queue.Request(); queue.CancelPending();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal(0, calls);
        queue.Request();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        queue.Resume();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal(1, calls); Assert.Equal(1, errors);
    });

    [Fact]
    public Task InteractionStartingBeforeQueuedCallbackKeepsRequestPending() => Sta(async () =>
    {
        bool idle = true;
        int calls = 0;
        var queue = new AutoArrangeQueue(Dispatcher.CurrentDispatcher, () => idle,
            () => { calls++; return Task.CompletedTask; }, error => throw error);
        queue.Request(); idle = false;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal(0, calls);
        idle = true; queue.Resume();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal(1, calls);
    });

    [Fact]
    public Task CancelledTileCaptureNotifiesDeferredLayout() => Sta(() =>
    {
        using var vm = new NoteViewModel(new Note { IsCollapsed = true }, _ => Task.CompletedTask);
        var window = new NoteWindow(vm) { AllowClose = true };
        try
        {
            typeof(NoteWindow).GetField("tilePress", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(window, new Point(100, 100));
            Assert.True(window.IsDragging);
            int ended = 0; window.InteractionFinished += () => ended++;
            var tile = (Button)window.FindName("CollapsedTile");
            tile.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
                { RoutedEvent = System.Windows.Input.Mouse.LostMouseCaptureEvent });
            Assert.False(window.IsDragging); Assert.Equal(1, ended);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task BodyEditBelowTitleDoesNotNotifyLayoutTitleChange() => Sta(() =>
    {
        using var vm = new NoteViewModel(new Note { Body = "제목\n본문" }, _ => Task.CompletedTask);
        int titles = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Title)) titles++; };
        vm.Body = "제목\n본문 수정";
        Assert.Equal(0, titles);
        vm.Body = "새 제목\n본문 수정";
        Assert.Equal(1, titles);
        return Task.CompletedTask;
    });

    private static Task Sta(Func<Task> test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await test(); completion.SetResult(); }
                catch (Exception ex) { completion.SetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }
}
