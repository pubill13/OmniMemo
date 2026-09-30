using System.Windows.Threading;
using Memoit.Models;
using Memoit.Services;
using Memoit.ViewModels;
using Memoit.Views;
using Xunit;

namespace Memoit.Tests;

[Collection("WPF")]
public sealed class TrashOperationsTests
{
    [Fact]
    public Task PartialFailureKeepsSuccessfulDeletionAndRestoresFailedHiddenState() => Sta(async () =>
    {
        var hidden = new Note { IsVisible = false, IsCollapsed = true, Left = 240, Top = 80 };
        using var good = new NoteViewModel(new Note(), _ => Task.CompletedTask);
        using var bad = new NoteViewModel(hidden, _ => Task.FromException(new IOException("disk full")));
        var result = await TrashOperations.DeleteAsync([good, bad]);
        Assert.Equal(good.Snapshot.Id, Assert.Single(result.Succeeded).Before.Id);
        Assert.Equal(hidden.Id, Assert.Single(result.Failed));
        Assert.NotNull(good.Snapshot.DeletedAt); Assert.False(good.Snapshot.IsVisible);
        Assert.Null(bad.Snapshot.DeletedAt); Assert.False(bad.Snapshot.IsVisible);
        Assert.True(bad.Snapshot.IsCollapsed); Assert.Equal(240, bad.Snapshot.Left);
        Assert.True(bad.IsDirty); Assert.Equal("Failed", bad.SaveState);
        Assert.Contains("disk full", bad.SaveStatus);
    });

    [Fact]
    public Task RestorePreservesLatestTextAndFailureCanRetry() => Sta(async () =>
    {
        bool fail = false;
        var before = new Note { Body = "before", IsVisible = false, IsCollapsed = true,
            Left = 200, Top = 140, CollapsedLeft = 200, CollapsedTop = 140, Width = 410, Height = 350 };
        using var vm = new NoteViewModel(before, _ => fail ? Task.FromException(new IOException("denied")) : Task.CompletedTask);
        var change = Assert.Single((await TrashOperations.DeleteAsync([vm])).Succeeded);
        vm.Body = "newer text"; vm.Color = "#FFDDE7";
        fail = true;
        Assert.False(await TrashOperations.RestoreAsync(vm, change.Before));
        Assert.Equal(change.DeletedAt, vm.Snapshot.DeletedAt);
        Assert.Equal("newer text", vm.Body); Assert.Equal("Failed", vm.SaveState);
        fail = false;
        Assert.True(await TrashOperations.RestoreAsync(vm, change.Before));
        Assert.Null(vm.Snapshot.DeletedAt); Assert.False(vm.Snapshot.IsVisible);
        Assert.True(vm.IsCollapsed); Assert.Equal(410, vm.Snapshot.Width);
        Assert.Equal(200, vm.Snapshot.CollapsedLeft);
        Assert.Equal("newer text", vm.Body); Assert.Equal("#FFDDE7", vm.Color);
        Assert.False(vm.IsDirty);
    });

    [Fact]
    public Task DeleteSkipsDuplicatesAndAlreadyDeletedNotes() => Sta(async () =>
    {
        int saves = 0;
        using var active = new NoteViewModel(new Note(), _ => { saves++; return Task.CompletedTask; });
        var deletedAt = DateTimeOffset.UtcNow.AddDays(-1);
        using var trash = new NoteViewModel(new Note { DeletedAt = deletedAt, IsVisible = false },
            _ => throw new InvalidOperationException("Existing trash must not be saved"));
        var result = await TrashOperations.DeleteAsync([active, active, trash]);
        Assert.Single(result.Succeeded);
        Assert.Empty(result.Failed);
        Assert.Equal(1, saves);
        Assert.Equal(deletedAt, trash.Snapshot.DeletedAt);
    });

    [Fact]
    public Task RestoreFlushesEditsAndDragMadeDuringItsSave() => Sta(async () =>
    {
        var saved = new List<Note>();
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var before = new Note { Body = "initial", Left = 100, Top = 150 };
        using var vm = new NoteViewModel(before with { DeletedAt = DateTimeOffset.UtcNow, IsVisible = false }, async note =>
        {
            saved.Add(note);
            if (saved.Count == 1) await released.Task;
        });
        var restore = TrashOperations.RestoreAsync(vm, before);
        Assert.False(restore.IsCompleted);
        vm.Body = "typed while restoring";
        vm.UpdateBounds(420, 330, 500, 400);
        released.SetResult();
        Assert.True(await restore);
        Assert.Equal(2, saved.Count);
        Assert.Equal("typed while restoring", saved[^1].Body);
        Assert.Equal(420, saved[^1].Left);
        Assert.Equal(500, saved[^1].Width);
        Assert.True(saved[^1].IsVisible);
        Assert.Null(saved[^1].DeletedAt);
        Assert.False(vm.IsDirty);
    });

    [Fact]
    public Task NoticePausesAndExpiresOnlyOnce() => Sta(() =>
    {
        using var notice = new UndoNotice(2);
        int expired = 0; notice.Expired += () => expired++;
        notice.Advance(TimeSpan.FromSeconds(9), false);
        notice.Advance(TimeSpan.FromSeconds(30), true);
        Assert.Equal(0, expired);
        notice.Advance(TimeSpan.FromSeconds(1), false);
        notice.Advance(TimeSpan.FromSeconds(10), false);
        Assert.Equal(1, expired);
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
