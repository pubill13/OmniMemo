using System.Windows;
using Memoit.Views;

namespace Memoit;

public partial class App
{
    private void ShowTrashWindow()
    {
        if (busy || shuttingDown) return;
        if (trashWindow is null)
        {
            trashWindow = new TrashWindow();
            trashWindow.RestoreRequested += id => RunOperation(() => RestoreTrashAsync(id));
            trashWindow.PermanentDeleteRequested += id => RunOperation(() => PermanentDeleteTrashAsync([id]));
            trashWindow.PermanentDeleteSelectedRequested += ids => RunOperation(() => PermanentDeleteTrashAsync(ids));
            trashWindow.EmptyTrashRequested += ids => RunOperation(() => PermanentDeleteTrashAsync(ids, true));
            trashWindow.Closed += (_, _) => trashWindow = null;
        }
        trashWindow.SetNotes(notes.Values.Select(n => n.Snapshot));
        trashWindow.Show(); trashWindow.WindowState = WindowState.Normal; trashWindow.Activate();
    }

    private async Task PermanentDeleteTrashAsync(IReadOnlyList<Guid> ids, bool emptyTrash = false)
    {
        var targets = ids.Distinct().Where(id => notes.TryGetValue(id, out var vm) && vm.Snapshot.DeletedAt is not null).ToArray();
        if (targets.Length == 0) return;
        string message = emptyTrash ? $"휴지통의 모든 메모 {targets.Length}개를 영구 삭제할까요?\n검색 결과에 없는 메모도 포함되며, 복구할 수 없습니다."
            : $"선택한 {targets.Length}개 메모를 영구 삭제할까요? 복구할 수 없습니다.";
        if (MessageBox.Show(trashWindow!, message, emptyTrash ? "휴지통 비우기" : "영구 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        var failures = new List<string>();
        foreach (var id in targets)
        {
            if (!notes.TryGetValue(id, out var vm) || vm.Snapshot.DeletedAt is null) continue;
            try
            {
                if (!await vm.FlushAsync()) throw new IOException(vm.SaveStatus);
                await store.DeletePermanentlyAsync(id);
                if (windows.Remove(id, out var window)) { window.AllowClose = true; window.Close(); }
                vm.Dispose(); notes.Remove(id);
            }
            catch (Exception ex) { failures.Add(ex.Message); }
        }
        RefreshList();
        if (failures.Count > 0) ArrangementNotice($"{failures.Count}개 메모를 영구 삭제하지 못했습니다. 휴지통에서 다시 시도하세요.\n{failures[0]}", true);
    }
}
