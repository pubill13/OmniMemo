using System.Windows;
using Memoit.Services;
using Memoit.Views;

namespace Memoit;

public partial class App
{
    private List<TrashChange> undoTrash = [];
    private UndoNotice? undoNotice;
    private Window? undoWindow;

    private async Task DeleteSelectedAsync(IReadOnlyList<Guid> ids)
    {
        var targets = ids.Distinct().Where(id => notes.TryGetValue(id, out var vm) && vm.Snapshot.DeletedAt is null).ToArray();
        if (targets.Length == 0) return;
        string summary = string.Join(", ", targets.Take(3).Select(id => notes[id].Title));
        if (targets.Length > 3) summary += $" 외 {targets.Length - 3}개";
        if (MessageBox.Show(list, $"선택한 {targets.Length}개 메모를 휴지통으로 이동할까요?\n{summary}", "선택 삭제", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await DeleteNotesAsync(targets, true);
    }

    private async Task DeleteNotesAsync(IReadOnlyList<Guid> ids, bool fromList)
    {
        var targets = ids.Distinct().Where(id => notes.TryGetValue(id, out var vm) && vm.Snapshot.DeletedAt is null).ToArray();
        if (targets.Length == 0) return;
        var anchorWindow = windows.GetValueOrDefault(targets[0]);
        Point anchor = anchorWindow is null ? new Point(100, 100) : new Point(anchorWindow.Left, anchorWindow.Top);
        var result = await TrashOperations.DeleteAsync(targets.Select(id => notes[id]));
        foreach (var item in result.Succeeded)
            if (windows.TryGetValue(item.Before.Id, out var window)) window.Hide();
        if (result.Succeeded.Count > 0) ShowDeleteUndo(result.Succeeded, fromList, anchor);
        if (result.Failed.Count > 0) ArrangementNotice($"{result.Failed.Count}개 메모를 삭제하지 못했습니다. 해당 메모는 이전 상태로 유지됩니다. 저장 상태에서 원인을 확인하세요.", true);
    }

    private async Task RestoreTrashAsync(Guid id)
    {
        if (!notes.TryGetValue(id, out var vm) || vm.Snapshot.DeletedAt is null) return;
        if (await TrashOperations.RestoreAsync(vm, vm.Snapshot with { DeletedAt = null, IsVisible = true }))
            ShowNote(vm);
        else ArrangementNotice("메모를 복원하지 못했습니다. 휴지통에서 다시 시도하세요.", true);
    }

    private void ShowDeleteUndo(IReadOnlyList<TrashChange> changes, bool fromList, Point anchor)
    {
        ClearDeleteUndo();
        undoTrash = changes.ToList();
        undoNotice = new UndoNotice(changes.Count);
        undoNotice.UndoRequested += () => { if (!busy && !arranging) RunOperation(UndoDeleteAsync); };
        undoNotice.Expired += ClearDeleteUndo;
        if (fromList && list?.IsVisible == true) list.SetUndoContent(undoNotice);
        else if (overlayPanel?.IsVisible == true) overlayPanel.SetUndoContent(undoNotice);
        else if (list?.IsVisible == true) list.SetUndoContent(undoNotice);
        else
        {
            undoWindow = new Window { Title = "OmniMemo · 삭제 실행 취소", ShowActivated = false, ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow, ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.Height, Width = 310, Left = anchor.X, Top = anchor.Y,
                Content = undoNotice, Topmost = true };
            undoWindow.Closed += (_, _) => ClearDeleteUndo();
            undoWindow.Show(); WindowPlacement.KeepOnScreen(undoWindow);
        }
    }

    private async Task UndoDeleteAsync()
    {
        if (undoNotice is null) return;
        var notice = undoNotice;
        notice.SetWorking(true);
        var failed = new List<TrashChange>();
        foreach (var item in undoTrash.ToArray())
        {
            if (!notes.TryGetValue(item.Before.Id, out var vm) || vm.Snapshot.DeletedAt != item.DeletedAt) continue;
            if (!await TrashOperations.RestoreAsync(vm, item.Before)) { failed.Add(item); continue; }
            if (vm.Snapshot.IsVisible)
            {
                // Rebuild the window from the restored geometry without activating it.
                if (windows.Remove(item.Before.Id, out var old)) { old.AllowClose = true; old.Close(); }
                ShowNote(vm, false);
            }
        }
        undoTrash = failed;
        if (failed.Count == 0) ClearDeleteUndo();
        else notice.ShowFailure(failed.Count);
    }

    private void ClearDeleteUndo()
    {
        undoNotice?.Dispose(); undoNotice = null; undoTrash.Clear();
        list?.SetUndoContent(null); overlayPanel?.SetUndoContent(null);
        var window = undoWindow; undoWindow = null;
        if (window is not null) { window.Content = null; window.Close(); }
    }
}
