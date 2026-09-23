using System.Windows;
using Memoit.Services;
using Memoit.Views;

namespace Memoit;

public partial class App
{
    private void ExecuteCommand(string command)
    {
        if (busy || shuttingDown) return;
        if (command == "TogglePanel") { ToggleLayoutPanel(); return; }
        RunOperation(async () =>
        {
            switch (command)
            {
                case "ToggleVisibility": await SetAllVisibleAsync(!windows.Values.Any(w => w.IsVisible)); break;
                case "HideAll": await SetAllVisibleAsync(false); break;
                case "ShowAll": await SetAllVisibleAsync(true); break;
                case "ToggleCollapsed": await SetAllCollapsedAsync(windows.Values.Any(w => w.IsVisible && !w.IsCollapsed)); break;
                case "CollapseAll": await SetAllCollapsedAsync(true); break;
                case "ExpandAll": await SetAllCollapsedAsync(false); break;
                case "Arrange": await ArrangeTilesAsync(); break;
                case "SortCreated": await ChangeLayoutAsync(sort: LayoutSort.Created); break;
                case "SortColor": await ChangeLayoutAsync(sort: LayoutSort.Color); break;
                case "SortTitle": await ChangeLayoutAsync(sort: LayoutSort.Title); break;
                case "ShapeGrid": await ChangeLayoutAsync(shape: LayoutShape.Grid); break;
                case "ShapeHorizontal": await ChangeLayoutAsync(shape: LayoutShape.Horizontal); break;
                case "ShapeVertical": await ChangeLayoutAsync(shape: LayoutShape.Vertical); break;
                case "ToggleAuto": await SetAutoArrangeAsync(!layoutSettings.AutoArrange); break;
            }
        }, command is "ToggleVisibility" or "HideAll" or "ShowAll" or "ToggleCollapsed" or "CollapseAll" or "ExpandAll");
    }

    private async Task SetAllCollapsedAsync(bool collapsed)
    {
        var visible = windows.Where(p => p.Value.IsVisible && notes[p.Key].Snapshot.DeletedAt is null).ToArray();
        foreach (var pair in visible)
            if (pair.Value.IsCollapsed != collapsed) pair.Value.ToggleCollapsed();
        bool saved = true;
        foreach (var pair in visible) saved &= await notes[pair.Key].FlushAsync();
        if (!saved) throw new IOException("일부 메모를 저장하지 못했습니다. 변경 내용은 유지되며 다음 편집 또는 종료 때 다시 저장합니다.");
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
        if (autoArrangeMenu is not null) autoArrangeMenu.Checked = value.AutoArrange;
        foreach (var window in windows.Values)
        {
            window.SetAutoArrange(value.AutoArrange);
            window.CollapseGesture = value.Hotkeys.GetValueOrDefault("ToggleCurrent", "");
        }
    }

    private Dictionary<Guid, Point> CalculateTileMoves(LayoutSettings settings)
    {
        var result = new Dictionary<Guid, Point>();
        var candidates = windows.Where(p => p.Value.IsVisible && p.Value.IsCollapsed
            && notes[p.Key].Snapshot.DeletedAt is null).Select(p => (p.Key, Window: p.Value, Monitor: MonitorCatalog.ForWindow(p.Value))).ToArray();
        foreach (var group in candidates.GroupBy(p => p.Monitor.Id))
        {
            var monitor = group.First().Monitor;
            foreach (var move in TileLayout.Arrange(group.Select(p => notes[p.Key].Snapshot), monitor.WorkArea,
                settings.GetMonitor(group.Key), group.Max(p => p.Monitor.Scale))) result.Add(move.Key, move.Value);
        }
        return result;
    }

    private async Task ApplyTileMovesAsync(Dictionary<Guid, Point> moves)
    {
        foreach (var move in moves) WindowPlacement.Move(windows[move.Key], move.Value);
        bool saved = true;
        foreach (var id in moves.Keys) saved &= await notes[id].FlushAsync();
        if (!saved) throw new IOException("정렬한 위치를 일부 저장하지 못했습니다. 창의 변경 내용은 유지됩니다.");
    }

    private Task ArrangeTilesAsync() => ApplyTileMovesAsync(CalculateTileMoves(layoutSettings));

    private async Task ChangeLayoutAsync(LayoutSort? sort = null, LayoutShape? shape = null)
    {
        var updated = new Dictionary<string, MonitorLayout>(layoutSettings.Monitors);
        foreach (var monitor in MonitorCatalog.All())
        {
            var prior = layoutSettings.GetMonitor(monitor.Id);
            updated[monitor.Id] = prior with { Sort = sort ?? prior.Sort, Shape = shape ?? prior.Shape };
        }
        var candidate = layoutSettings with { Monitors = updated };
        var moves = CalculateTileMoves(candidate);
        SaveLayoutSettings(candidate);
        await ApplyTileMovesAsync(moves);
        RefreshLayoutPanel();
    }

    private async Task SetAutoArrangeAsync(bool enabled)
    {
        try
        {
            var candidate = layoutSettings with { AutoArrange = enabled };
            var moves = enabled ? CalculateTileMoves(candidate) : [];
            SaveLayoutSettings(candidate);
            if (enabled) await ApplyTileMovesAsync(moves);
            RefreshLayoutPanel();
        }
        finally { foreach (var window in windows.Values) window.SetAutoArrange(layoutSettings.AutoArrange); }
    }

    private void RefreshLayoutPanel()
    {
        if (layoutPanel?.IsVisible == true) layoutPanel.RefreshSettings(layoutSettings, MonitorCatalog.All());
    }

    private void ToggleLayoutPanel()
    {
        if (layoutPanel?.IsVisible == true) { layoutPanel.Close(); return; }
        if (layoutPanel is null)
        {
            layoutPanel = new LayoutOptionsWindow(layoutSettings, MonitorCatalog.All());
            layoutPanel.ApplyRequested += candidate => ApplyPanelSettings(candidate, false);
            layoutPanel.ArrangeRequested += candidate => ApplyPanelSettings(candidate, true);
            layoutPanel.CaptureChanged += capture =>
            {
                try { if (capture) hotkeys?.BeginCapture(); else hotkeys?.EndCapture(); }
                catch (Exception ex) { layoutPanel.ShowError("단축키 등록을 복구하지 못했습니다: " + ex.Message); }
            };
            layoutPanel.Hidden += () =>
            {
                try
                {
                    SaveLayoutSettings(layoutSettings with { PanelLeft = layoutPanel.Left, PanelTop = layoutPanel.Top,
                        SelectedMonitor = layoutPanel.SelectedMonitorId });
                }
                catch (Exception ex) { Error("정렬 옵션창 위치를 저장하지 못했습니다.", ex); }
            };
            if (layoutSettings.PanelLeft is double left && layoutSettings.PanelTop is double top)
            { layoutPanel.Left = left; layoutPanel.Top = top; }
        }
        else layoutPanel.RefreshSettings(layoutSettings, MonitorCatalog.All());
        layoutPanel.Show(); WindowPlacement.KeepOnScreen(layoutPanel); layoutPanel.Activate();
    }

    private void ApplyPanelSettings(LayoutSettings candidate, bool arrange)
    {
        RunOperation(async () =>
        {
            try
            {
                var moves = arrange || candidate.AutoArrange ? CalculateTileMoves(candidate) : [];
                SaveLayoutSettings(candidate);
                if (arrange || candidate.AutoArrange) await ApplyTileMovesAsync(moves);
                layoutPanel?.ShowSuccess(arrange ? "설정을 저장하고 정렬했습니다." : "설정을 저장했습니다.");
            }
            catch (Exception ex) { layoutPanel?.ShowError(ex.Message); }
        }, false);
    }
}
