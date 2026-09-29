using System.Diagnostics;
using System.Windows;
using Memoit.Services;
using Memoit.ViewModels;
using Memoit.Views;

namespace Memoit;

public partial class App
{
    private bool arranging;
    private readonly ArrangementHistory arrangementHistory = new();

    private void ExecuteCommand(string command)
    {
        if (busy || shuttingDown) return;
        if (command == "TogglePanel") { ToggleLayoutPanel(); return; }
        if (command == "ToggleOverlay") { ToggleOverlayPanel(); return; }
        if (command == "ShowList") { ShowList(); return; }
        if (command == "Search") { ShowList(true); return; }
        if (command is "Arrange" or "ArrangeCollapsed" or "ArrangeExpanded" or "UndoArrange" or "RetrySave"
            or "SortCreated" or "SortColor" or "SortTitle" or "ShapeGrid" or "ShapeHorizontal" or "ShapeVertical")
        { RunArrangement(command); return; }
        RunOperation(async () =>
        {
            switch (command)
            {
                case "NewNote": await NewNoteAsync(); break;
                case "ToggleVisibility": await SetAllVisibleAsync(!windows.Values.Any(w => w.IsVisible)); break;
                case "HideAll": await SetAllVisibleAsync(false); break;
                case "ShowAll": await SetAllVisibleAsync(true); break;
                case "ToggleCollapsed": await SetAllCollapsedAsync(windows.Values.Any(w => w.IsVisible && !w.IsCollapsed)); break;
                case "CollapseAll": await SetAllCollapsedAsync(true); break;
                case "ExpandAll": await SetAllCollapsedAsync(false); break;
            }
        });
    }

    private async Task SetAllCollapsedAsync(bool collapsed)
    {
        var visible = windows.Where(p => p.Value.IsVisible && notes[p.Key].Snapshot.DeletedAt is null).ToArray();
        foreach (var pair in visible)
            if (pair.Value.IsCollapsed != collapsed) pair.Value.ToggleCollapsed();
        if (!await NoteViewModel.FlushManyAsync(visible.Select(p => notes[p.Key]), store.SaveManyAsync))
            throw new IOException("일부 메모를 저장하지 못했습니다. 변경 내용은 유지됩니다. 트레이의 미저장 메모 다시 저장을 사용하세요.");
    }

    private void SaveLayoutSettings(LayoutSettings value)
    {
        value.Validate();
        HotkeyService.Validate(value.Hotkeys);
        var previous = layoutSettings;
        try
        {
            hotkeys?.Configure(value.Hotkeys);
            if (hotkeys?.Suspended == true) hotkeys.EndCapture();
            value.Save(Path.Combine(dataDirectory, "layout.json"));
        }
        catch (Exception saveError)
        {
            try
            {
                hotkeys?.Configure(previous.Hotkeys);
                if (hotkeys?.Suspended == true) hotkeys.EndCapture();
            }
            catch (Exception rollbackError) { throw new AggregateException("설정 저장과 기존 단축키 복구에 실패했습니다.", saveError, rollbackError); }
            throw;
        }
        layoutSettings = value;
        foreach (var window in windows.Values)
            window.CollapseGesture = value.Hotkeys.GetValueOrDefault("ToggleCurrent", "");
    }

    private async void RunArrangement(string command)
    {
        if (arranging || busy || shuttingDown) return;
        arranging = true;
        try
        {
            if (command == "RetrySave")
            {
                if (!await NoteViewModel.FlushManyAsync(notes.Values, store.SaveManyAsync))
                    throw new IOException("저장하지 못했습니다. 저장 상태 아이콘에서 원인을 확인한 뒤 다시 시도하세요.");
                ArrangementNotice("미저장 메모를 저장했습니다.");
                return;
            }
            LayoutSort? sort = command switch { "SortCreated" => LayoutSort.Created, "SortColor" => LayoutSort.Color, "SortTitle" => LayoutSort.Title, _ => null };
            LayoutShape? shape = command switch { "ShapeGrid" => LayoutShape.Grid, "ShapeHorizontal" => LayoutShape.Horizontal, "ShapeVertical" => LayoutShape.Vertical, _ => null };
            var clock = Stopwatch.StartNew();
            var monitors = MonitorCatalog.All();
            if (sort.HasValue || shape.HasValue)
            {
                var updated = new Dictionary<string, MonitorLayout>(layoutSettings.Monitors);
                foreach (var monitor in monitors)
                {
                    var prior = layoutSettings.GetMonitor(monitor.Id);
                    updated[monitor.Id] = prior with { Sort = sort ?? prior.Sort, Shape = shape ?? prior.Shape };
                }
                SaveLayoutSettings(layoutSettings with { Monitors = updated });
                RefreshLayoutPanel();
            }
            var candidates = windows.Where(p => p.Value.IsVisible && notes[p.Key].Snapshot.DeletedAt is null)
                .Select(p => (Id: p.Key, Window: p.Value, Bounds: WindowPlacement.GetBounds(p.Value))).ToArray();
            var moves = new Dictionary<Guid, Rect>();
            int skipped = 0;
            if (command == "UndoArrange")
            {
                foreach (var item in arrangementHistory.GetRestorable(notes.Values.Select(n => n.Snapshot), monitors))
                    if (windows.TryGetValue(item.Key, out var window) && window.IsVisible) moves[item.Key] = item.Value;
                if (moves.Count == 0) { ArrangementNotice("복원할 이전 배치가 없습니다."); return; }
            }
            else
            {
                foreach (var group in candidates.GroupBy(p => MonitorCatalog.Nearest(p.Bounds, monitors)))
                {
                    var monitor = group.Key;
                    var options = layoutSettings.GetMonitor(monitor.Id);
                    var snapshots = group.Select(p => notes[p.Id].Snapshot).ToArray();
                    var sizes = group.ToDictionary(p => p.Id, p => p.Bounds.Size);
                    if (command != "ArrangeExpanded")
                    {
                        var result = NoteArrangement.ArrangeCollapsed(snapshots, monitor.WorkArea, options, monitor.Scale, sizes);
                        skipped += result.SkippedTiles;
                        foreach (var pair in result.Bounds) moves[pair.Key] = pair.Value;
                    }
                    if (command != "ArrangeCollapsed")
                    {
                        var result = NoteArrangement.ArrangeExpanded(snapshots, monitor.WorkArea, options, options.ExpandedCorner, monitor.Scale, sizes);
                        foreach (var pair in result.Bounds) moves[pair.Key] = pair.Value;
                    }
                }
                arrangementHistory.Capture(candidates.Where(p => moves.ContainsKey(p.Id)).Select(p =>
                    new ArrangementSnapshot(p.Id, p.Window.IsCollapsed, p.Bounds, MonitorCatalog.Nearest(p.Bounds, monitors).Id)));
            }
            var originals = candidates.ToDictionary(p => p.Id, p => p.Bounds);
            var changed = moves.Where(p => originals.TryGetValue(p.Key, out var original) && !NearlyEqual(original, p.Value)).ToArray();
            double calculatedMs = clock.Elapsed.TotalMilliseconds;
            foreach (var pair in changed) windows[pair.Key].BeginArrangement(pair.Value);
            try { WindowPlacement.MoveTogether(changed.Select(p => ((Window)windows[p.Key], p.Value)).ToArray()); }
            finally { foreach (var pair in changed) windows[pair.Key].EndArrangement(); }
            double movedMs = clock.Elapsed.TotalMilliseconds;
            // Capture only after WPF has observed every move, then serialize alongside ordinary autosaves.
            bool saved = await NoteViewModel.FlushManyAsync(changed.Select(p => notes[p.Key]), store.SaveManyAsync);
            double storedMs = clock.Elapsed.TotalMilliseconds;
            LastArrangementTiming = new ArrangementTiming(calculatedMs, movedMs - calculatedMs, storedMs - movedMs, changed.Length);
            if (command == "UndoArrange") arrangementHistory.Clear();
            if (!saved) throw new IOException("배치는 변경했지만 저장하지 못했습니다. 트레이의 미저장 메모 다시 저장을 눌러 주세요.");
            ArrangementNotice(skipped > 0 ? $"일부 타일을 배치하지 못했습니다 ({skipped}개). 타일 시작점·간격·열 수를 조정해 주세요."
                : command == "UndoArrange" ? "이전 배치로 돌아갔습니다." : "메모를 정돈했습니다.", skipped > 0);
        }
        catch (Exception ex) { ArrangementNotice(ex.Message, true); }
        finally { arranging = false; RefreshList(); }
    }

    internal sealed record ArrangementTiming(double CalculationMs, double MoveMs, double SaveMs, int MovedCount);
    internal ArrangementTiming? LastArrangementTiming { get; private set; }
    private static bool NearlyEqual(Rect a, Rect b) => Math.Abs(a.X - b.X) < .5 && Math.Abs(a.Y - b.Y) < .5
        && Math.Abs(a.Width - b.Width) < .5 && Math.Abs(a.Height - b.Height) < .5;

    private void ArrangementNotice(string message, bool warning = false)
    {
        if (warning) layoutPanel?.ShowError(message); else layoutPanel?.ShowSuccess(message);
        tray?.ShowBalloonTip(5000, "OmniMemo", message, warning ? System.Windows.Forms.ToolTipIcon.Warning : System.Windows.Forms.ToolTipIcon.Info);
    }

    private void ShowArrangementUpgradeNotice()
    {
        if (!layoutSettings.NeedsManualArrangementNotice) return;
        ArrangementNotice("자동 정렬이 정돈 명령으로 바뀌었습니다. 접기·펼치기나 이동 후에는 위치를 유지하며, 필요할 때 미니 패널이나 트레이에서 정돈하세요.");
        try { SaveLayoutSettings(layoutSettings with { NeedsManualArrangementNotice = false }); }
        catch (Exception ex) { ArrangementNotice("변경 안내 상태를 저장하지 못했습니다: " + ex.Message, true); }
    }

    private void RefreshLayoutPanel()
    {
        if (layoutPanel?.IsVisible == true) layoutPanel.RefreshSettings(layoutSettings, MonitorCatalog.All());
    }

    private void ToggleLayoutPanel()
    {
        if (layoutPanel?.IsVisible == true) { layoutPanel.Close(); return; }
        OpenSettings(0);
    }

    private void OpenSettings(int tab)
    {
        if (layoutPanel is null)
        {
            layoutPanel = new LayoutOptionsWindow(layoutSettings, MonitorCatalog.All(), dataDirectory, StartupRegistration.IsEnabled());
            layoutPanel.ApplyRequested += candidate => ApplyPanelSettings(candidate, false);
            layoutPanel.ArrangeRequested += candidate => ApplyPanelSettings(candidate, true);
            layoutPanel.AutoStartChanged += enabled =>
            {
                try { StartupRegistration.SetEnabled(enabled); }
                catch (Exception ex) { layoutPanel.SetAutoStart(!enabled); layoutPanel.ShowError("자동 실행 설정을 변경하지 못했습니다: " + ex.Message); }
            };
            layoutPanel.BackupRequested += () => RunOperation(BackupAsync);
            layoutPanel.RestoreRequested += () => RunOperation(RestoreAsync);
            layoutPanel.CaptureChanged += capture =>
            {
                try { if (capture) hotkeys?.BeginCapture(); else hotkeys?.EndCapture(); }
                catch (Exception ex) { layoutPanel.ShowError("단축키 등록을 복구하지 못했습니다: " + ex.Message); }
            };
            layoutPanel.Hidden += () =>
            {
                try { SaveLayoutSettings(layoutSettings with { PanelLeft = layoutPanel.Left, PanelTop = layoutPanel.Top, SelectedMonitor = layoutPanel.SelectedMonitorId }); }
                catch (Exception ex) { ArrangementNotice("설정 창 위치를 저장하지 못했습니다: " + ex.Message, true); }
            };
            if (layoutSettings.PanelLeft is double left && layoutSettings.PanelTop is double top)
            { layoutPanel.Left = left; layoutPanel.Top = top; }
        }
        else layoutPanel.RefreshSettings(layoutSettings, MonitorCatalog.All());
        layoutPanel.SelectTab(tab);
        layoutPanel.Show(); WindowPlacement.KeepOnScreen(layoutPanel); layoutPanel.Activate();
    }

    private void ApplyPanelSettings(LayoutSettings candidate, bool arrange)
    {
        try
        {
            candidate = candidate with
            {
                OverlayLeft = layoutSettings.OverlayLeft, OverlayTop = layoutSettings.OverlayTop,
                OverlayOpacity = layoutSettings.OverlayOpacity, OverlayTopmost = layoutSettings.OverlayTopmost,
                OverlayColor = layoutSettings.OverlayColor, OverlayVisible = layoutSettings.OverlayVisible,
                NeedsManualArrangementNotice = layoutSettings.NeedsManualArrangementNotice
            };
            SaveLayoutSettings(candidate);
            layoutPanel?.ShowSuccess();
            if (arrange) RunArrangement("Arrange");
        }
        catch (Exception ex) { layoutPanel?.ShowError(ex.Message); }
    }
}
