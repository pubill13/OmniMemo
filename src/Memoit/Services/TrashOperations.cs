using Memoit.Models;
using Memoit.ViewModels;

namespace Memoit.Services;

public sealed record TrashChange(Note Before, DateTimeOffset DeletedAt);
public sealed record TrashResult(IReadOnlyList<TrashChange> Succeeded, IReadOnlyList<Guid> Failed);

public static class TrashOperations
{
    public static async Task<TrashResult> DeleteAsync(IEnumerable<NoteViewModel> models)
    {
        var succeeded = new List<TrashChange>();
        var failed = new List<Guid>();
        foreach (var vm in models.ToArray())
        {
            var before = vm.Snapshot;
            if (before.DeletedAt is not null) continue;
            vm.SetDeleted(true);
            var deletion = vm.Snapshot.DeletedAt!.Value;
            if (await vm.FlushAsync()) succeeded.Add(new(before, deletion));
            else { vm.RestoreLifecycle(before, true); failed.Add(before.Id); }
        }
        return new(succeeded, failed);
    }

    public static async Task<bool> RestoreAsync(NoteViewModel vm, Note target)
    {
        var before = vm.Snapshot;
        vm.RestoreLifecycle(target);
        if (await vm.FlushAsync()) return true;
        vm.RestoreLifecycle(before, true);
        return false;
    }
}
