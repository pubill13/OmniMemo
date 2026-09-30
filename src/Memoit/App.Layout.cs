using System.Diagnostics;
using System.Windows;
using Memoit.Services;
using Memoit.ViewModels;
using Memoit.Views;

namespace Memoit;

public partial class App
{
    private bool arranging;
    private readonly object layoutFileGate = new();

    private void ExecuteCommand(string command)
    {
        if (busy || shuttingDown) return;
        if (command == "TogglePanel") { ToggleLayoutPanel(); return; }
        if (command == "ToggleOverlay") { ToggleOverlayPanel(); return; }
        if (command == "ShowList") { ShowList(); return; }
        if (command == "Search") { ShowList(true); return; }
        if (command is "Arrange" or "CollapseAll" or "ExpandAll" or "ToggleCollapsed" or "RetrySave"
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
            }
        });
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
            lock (layoutFileGate)
            {
                value.Save(Path.Combine(dataDirectory, "layout.json"));
                layoutSettings = value;
            }
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
        foreach (var window in windows.Values)
            window.CollapseGesture = value.Hotkeys.GetValueOrDefault("ToggleCurrent", "");
    }

    private async void RunArrangement(string command) => await RunArrangementAsync(command);

    private async Task RunArrangementAsync(string command)
    {
        if (arranging || busy || shuttingDown) return;
        arranging = true;
        try
        {
            if (command == "RetrySave")
            {
                if (!await NoteViewModel.FlushManyAsync(notes.Values.ToArray(), store.SaveManyAsync))
                    throw new IOException("저장하지 못했습니다. 저장 상태 아이콘에서 원인을 확인한 뒤 다시 시도하세요.");
                await PersistLayoutAsync();
                ArrangementNotice("미저장 메모를 저장했습니다.");
                return;
            }
            var clock = Stopwatch.StartNew();
            var monitors = MonitorCatalog.All();
            var candidates = windows.Where(p => p.Value.IsVisible && notes[p.Key].Snapshot.DeletedAt is null)
                .Select(p => (Id: p.Key, Window: p.Value, Bounds: WindowPlacement.GetBounds(p.Value))).ToArray();
            bool collapsed = command != "ExpandAll";
            if (command == "ToggleCollapsed") collapsed = candidates.Any(p => !p.Window.IsCollapsed);
            LayoutSort? sort = command switch { "SortCreated" => LayoutSort.Created, "SortColor" => LayoutSort.Color, "SortTitle" => LayoutSort.Title, _ => null };
            LayoutShape? shape = command switch { "ShapeGrid" => LayoutShape.Grid, "ShapeHorizontal" => LayoutShape.Horizontal, "ShapeVertical" => LayoutShape.Vertical, _ => null };
            var updated = new Dictionary<string, MonitorLayout>(layoutSettings.Monitors);
            var orders = layoutSettings.ArrangementOrder.ToDictionary(p => p.Key, p => new List<Guid>(p.Value));
            var moves = new Dictionary<Guid, Rect>();
            var stacking = new List<Window>();
            int skipped = 0;
            foreach (var group in candidates.GroupBy(p => MonitorCatalog.Nearest(p.Bounds, monitors)))
            {
                var monitor = group.Key;
                var prior = layoutSettings.GetMonitor(monitor.Id);
                var options = prior with { Sort = sort ?? prior.Sort, Shape = shape ?? prior.Shape };
                updated[monitor.Id] = options;
                var snapshots = group.Select(p => notes[p.Id].Snapshot with { IsCollapsed = collapsed }).ToArray();
                var sizes = group.ToDictionary(p => p.Id, p => collapsed ? new Size(36 * monitor.Scale, 36 * monitor.Scale)
                    : p.Window.IsCollapsed ? new Size(notes[p.Id].Snapshot.Width * monitor.Scale, notes[p.Id].Snapshot.Height * monitor.Scale) : p.Bounds.Size);
                ArrangementResult result;
                if (collapsed)
                {
                    orders[monitor.Id] = NoteArrangement.OrderNotes(snapshots, options.Sort).Select(n => n.Id).ToList();
                    result = NoteArrangement.ArrangeCollapsed(snapshots, monitor.WorkArea, options, monitor.Scale, sizes);
                    skipped += result.SkippedTiles;
                }
                else
                {
                    result = NoteArrangement.ArrangeExpanded(snapshots, monitor.WorkArea, options, options.ExpandedCorner,
                        monitor.Scale, sizes, orders.GetValueOrDefault(monitor.Id));
                    stacking.AddRange(result.Bounds.Keys.Select(id => (Window)windows[id]));
                }
                foreach (var item in group)
                    moves[item.Id] = result.Bounds.TryGetValue(item.Id, out var bounds) ? bounds : new Rect(item.Bounds.Location, sizes[item.Id]);
            }
            // Keep a fresh immutable settings snapshot before asynchronous persistence; UI edits can still replace it.
            layoutSettings = layoutSettings with { Monitors = updated, ArrangementOrder = orders };
            var originals = candidates.ToDictionary(p => p.Id, p => p.Bounds);
            var changed = moves.Where(p => windows[p.Key].IsCollapsed != collapsed || !NearlyEqual(originals[p.Key], p.Value)).ToArray();
            double calculatedMs = clock.Elapsed.TotalMilliseconds;
            var prepared = new List<NoteWindow>();
            try
            {
                foreach (var pair in changed)
                {
                    prepared.Add(windows[pair.Key]);
                    windows[pair.Key].PrepareState(collapsed, pair.Value);
                }
                WindowPlacement.MoveTogether(changed.Select(p => ((Window)windows[p.Key], p.Value)).ToArray());
            }
            finally { foreach (var window in prepared) window.EndArrangement(); }
            if (!collapsed) WindowPlacement.StackInOrder(stacking);
            double movedMs = clock.Elapsed.TotalMilliseconds;
            bool saved = await NoteViewModel.FlushManyAsync(changed.Select(p => notes[p.Key]).ToArray(), store.SaveManyAsync);
            await PersistLayoutAsync();
            double storedMs = clock.Elapsed.TotalMilliseconds;
            LastArrangementTiming = new ArrangementTiming(calculatedMs, movedMs - calculatedMs, storedMs - movedMs, changed.Length);
            if (!saved) throw new IOException("화면은 변경했지만 저장하지 못했습니다. 트레이의 미저장 메모 다시 저장을 눌러 주세요.");
            ArrangementNotice(skipped > 0 ? $"일부 타일을 배치하지 못했습니다 ({skipped}개). 시작점·간격·열 수를 조정해 주세요."
                : collapsed ? "접어서 정돈했습니다." : "모두 펼쳤습니다.", skipped > 0);
        }
        catch (Exception ex) { ArrangementNotice(ex.Message, true); }
        finally { arranging = false; RefreshList(); }
    }

    private Task PersistLayoutAsync() => Task.Run(() =>
    {
        // The lock also serializes ordinary settings saves. Read the latest snapshot inside it so an
        // intervening settings edit cannot be overwritten by an older queued arrangement snapshot.
        lock (layoutFileGate) layoutSettings.Save(Path.Combine(dataDirectory, "layout.json"));
    });
    internal sealed record ArrangementTiming(double CalculationMs, double MoveMs, double SaveMs, int MovedCount);
    internal ArrangementTiming? LastArrangementTiming { get; private set; }
    private static bool NearlyEqual(Rect a, Rect b) => Math.Abs(a.X - b.X) < .5 && Math.Abs(a.Y - b.Y) < .5
        && Math.Abs(a.Width - b.Width) < .5 && Math.Abs(a.Height - b.Height) < .5;

    private void ArrangementNotice(string message, bool warning = false)
    {
        if (warning) layoutPanel?.ShowError(message); else layoutPanel?.ShowSuccess(message);
        overlayPanel?.ShowStatus(message, warning);
        if (warning) tray?.ShowBalloonTip(5000, "OmniMemo", message, System.Windows.Forms.ToolTipIcon.Warning);
    }

    private void ShowArrangementUpgradeNotice()
    {
        if (!layoutSettings.NeedsManualArrangementNotice) return;
        ArrangementNotice("자동 정렬이 정돈 명령으로 바뀌었습니다. 접기·펼치기나 이동 후에는 위치를 유지하며, 필요할 때 미니 패널이나 트레이에서 정돈하세요.");
        try { SaveLayoutSettings(layoutSettings with { NeedsManualArrangementNotice = false }); }
        catch (Exception ex) { ArrangementNotice("변경 안내 상태를 저장하지 못했습니다: " + ex.Message, true); }
    }

    private IReadOnlyList<Memoit.Models.Note> GetPreviewNotes(string monitorId)
    {
        var monitors = MonitorCatalog.All();
        return windows.Where(p => p.Value.IsVisible && notes[p.Key].Snapshot.DeletedAt is null)
            .Select(p => (p.Key, Window: p.Value, Bounds: WindowPlacement.GetBounds(p.Value)))
            .Where(p => MonitorCatalog.Nearest(p.Bounds, monitors).Id == monitorId)
            .Select(p => notes[p.Key].Snapshot with { Width = p.Window.IsCollapsed ? notes[p.Key].Snapshot.Width : p.Bounds.Width / monitors.First(m => m.Id == monitorId).Scale,
                Height = p.Window.IsCollapsed ? notes[p.Key].Snapshot.Height : p.Bounds.Height / monitors.First(m => m.Id == monitorId).Scale }).ToArray();
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
            layoutPanel = new LayoutOptionsWindow(layoutSettings, MonitorCatalog.All(), dataDirectory, StartupRegistration.IsEnabled(), GetPreviewNotes);
            layoutPanel.ApplyRequested += ApplyPanelSettings;
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

    private void ApplyPanelSettings(LayoutSettings candidate)
    {
        try
        {
            candidate = candidate with
            {
                OverlayLeft = layoutSettings.OverlayLeft, OverlayTop = layoutSettings.OverlayTop,
                OverlayTopmost = layoutSettings.OverlayTopmost,
                OverlayColor = layoutSettings.OverlayColor, OverlayVisible = layoutSettings.OverlayVisible,
                ArrangementOrder = layoutSettings.ArrangementOrder, NeedsManualArrangementNotice = layoutSettings.NeedsManualArrangementNotice
            };
            SaveLayoutSettings(candidate);
            overlayPanel?.RefreshSettings(layoutSettings);
            layoutPanel?.ShowSuccess();
        }
        catch (Exception ex) { layoutPanel?.ShowError(ex.Message); }
    }
}
