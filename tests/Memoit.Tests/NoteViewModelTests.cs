using System.Windows.Threading;
using Memoit.Models;
using Memoit.ViewModels;
using Xunit;

namespace Memoit.Tests;

[Collection("WPF")]
public sealed class NoteViewModelTests
{
    [Fact]
    public Task ChangesDuringSaveAreFlushedInOrder() => OnDispatcher(async () =>
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bodies = new List<string>();
        using var vm = new NoteViewModel(new Note(), async note =>
        {
            bodies.Add(note.Body);
            if (bodies.Count == 1) { started.SetResult(); await finishFirst.Task; }
        });
        vm.Body = "첫 입력";
        Task<bool> first = vm.FlushAsync();
        await started.Task;
        vm.Body = "최신 입력";
        Task<bool> second = vm.FlushAsync();
        finishFirst.SetResult();
        Assert.True(await first);
        Assert.True(await second);
        Assert.Equal(new[] { "첫 입력", "최신 입력" }, bodies);
        Assert.False(vm.IsDirty);
        Assert.Equal("최신 입력", vm.Body);
    });

    [Fact]
    public Task FailedSaveKeepsTextAndDirtyStateUntilExplicitRetry() => OnDispatcher(async () =>
    {
        int calls = 0;
        using var vm = new NoteViewModel(new Note(), _ => ++calls == 1
            ? Task.FromException(new IOException("쓰기 실패")) : Task.CompletedTask);
        vm.Body = "보존할 한글";
        Assert.False(await vm.FlushAsync());
        Assert.True(vm.IsDirty);
        Assert.Contains("쓰기 실패", vm.SaveStatus);
        Assert.Equal("보존할 한글", vm.Body);
        Assert.True(await vm.FlushAsync());
        Assert.False(vm.IsDirty);
        Assert.Equal("저장됨", vm.SaveStatus);
    });

    [Fact]
    public Task InvalidBoundsDoNotPoisonPendingSave() => OnDispatcher(() =>
    {
        using var vm = new NoteViewModel(new Note(), _ => Task.CompletedTask);
        Note original = vm.Snapshot;
        vm.UpdateBounds(10, 10, double.NaN, 300);
        vm.UpdateBounds(10, 10, 300, double.PositiveInfinity);
        Assert.Equal(original, vm.Snapshot);
        Assert.False(vm.IsDirty);
        return Task.CompletedTask;
    });

    [Fact]
    public Task DisposedDirtyModelDoesNotReportSuccessfulFlush() => OnDispatcher(async () =>
    {
        var vm = new NoteViewModel(new Note(), _ => Task.CompletedTask);
        vm.Body = "unsaved";
        vm.Dispose();
        Assert.False(await vm.FlushAsync());
    });

    private static Task OnDispatcher(Func<Task> test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await test(); completion.SetResult(); }
                catch (Exception ex) { completion.SetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
}
