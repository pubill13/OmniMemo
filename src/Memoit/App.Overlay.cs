using System.Windows;
using Memoit.Services;
using Memoit.Views;
using Memoit.ViewModels;

namespace Memoit;

public partial class App
{
    private OverlayPanelWindow? overlayPanel;

    private void ToggleOverlayPanel()
    {
        if (overlayPanel?.IsVisible == true) { overlayPanel.Close(); return; }
        ShowOverlayPanel(true);
    }

    private void RestoreOverlayPanel()
    {
        if (layoutSettings.OverlayVisible) ShowOverlayPanel();
    }

    private void ShowOverlayPanel(bool activate = false)
    {
        if (overlayPanel is null)
        {
            overlayPanel = new OverlayPanelWindow(layoutSettings);
            overlayPanel.CommandRequested += ExecuteCommand;
            overlayPanel.CollapseRequested += (collapsed, color) => RunFilteredCollapsed(collapsed, color);
            overlayPanel.PreferencesChanged += SaveOverlayPreferences;
            overlayPanel.Hidden += SaveOverlayPreferences;
            overlayPanel.Left = layoutSettings.OverlayLeft ?? SystemParameters.WorkArea.Right - overlayPanel.Width - 16;
            overlayPanel.Top = layoutSettings.OverlayTop ?? SystemParameters.WorkArea.Top + 16;
        }
        overlayPanel.FlushPreferences();
        overlayPanel.RefreshSettings(layoutSettings);
        overlayPanel.SetNotes(notes.Values.Select(n => n.Snapshot));
        overlayPanel.SetHasVisibleNotes(windows.Values.Any(w => w.IsVisible));
        overlayPanel.ShowActivated = activate;
        overlayPanel.Show();
        WindowPlacement.KeepOnScreen(overlayPanel);
        if (activate)
        {
            if (overlayPanel.WindowState == WindowState.Minimized) overlayPanel.WindowState = WindowState.Normal;
            overlayPanel.Activate();
        }
        SaveOverlayPreferences();
    }

    private void SaveOverlayPreferences()
    {
        if (overlayPanel is null) return;
        var candidate = layoutSettings with
            {
                OverlayLeft = overlayPanel.Left, OverlayTop = overlayPanel.Top,
                OverlayTopmost = overlayPanel.SelectedTopmost,
                OverlayColor = overlayPanel.SelectedColor, OverlayVisible = overlayPanel.IsVisible,
                OverlayOpacity = overlayPanel.SelectedOpacity
            };
        try
        {
            SaveLayoutSettings(candidate);
        }
        catch (Exception ex)
        {
            // Retain the visible preferences so a retry writes the value the user sees.
            layoutSettings = candidate;
            layoutSaveFailed = true;
            overlayPanel.ShowStatus("패널 설정 저장 실패 · 트레이에서 저장 다시 시도: " + ex.Message, true);
        }
    }

    private void CloseOverlayPanel()
    {
        if (overlayPanel is null) return;
        SaveOverlayPreferences();
        overlayPanel.AllowClose = true; overlayPanel.Close();
    }

    private async void RunFilteredCollapsed(bool collapsed, string? color)
    {
        if (arranging || busy || shuttingDown) return;
        arranging = true;
        try { await SetFilteredCollapsedAsync(collapsed, color); ArrangementNotice("선택 색상 메모를 변경했습니다."); }
        catch (Exception ex) { ArrangementNotice(ex.Message, true); }
        finally { arranging = false; RefreshList(); }
    }
    private async Task SetFilteredCollapsedAsync(bool collapsed, string? color)
    {
        var selected = windows.Where(p => p.Value.IsVisible && notes[p.Key].Snapshot.DeletedAt is null
            && NoteColors.Matches(notes[p.Key].Snapshot.Color, color)).ToArray();
        foreach (var pair in selected)
            if (pair.Value.IsCollapsed != collapsed) pair.Value.ToggleCollapsed(false);
        bool saved = await NoteViewModel.FlushManyAsync(selected.Select(p => notes[p.Key]), store.SaveManyAsync);
        if (!saved) throw new IOException("일부 메모를 저장하지 못했습니다. 변경 내용은 유지되며 다음 편집 또는 종료 때 다시 저장합니다.");
    }
}
