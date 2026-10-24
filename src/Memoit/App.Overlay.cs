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
            overlayPanel.CommandRequested += command =>
            {
                if (command == "NewNote")
                { if (!busy && !shuttingDown) RunOperation(() => NewNoteAsync(null, overlayPanel.SelectedColor)); }
                else ExecuteCommand(command);
            };
            overlayPanel.CollapseRequested += (collapsed, color) => RunFilteredCollapsed(collapsed, color);
            overlayPanel.PageRequested += ChangeExpandedPage;
            overlayPanel.PreferencesChanged += SaveOverlayPreferences;
            overlayPanel.Hidden += () => { SaveOverlayPreferences(); RefreshPageControls(); };
            overlayPanel.Left = layoutSettings.OverlayLeft ?? SystemParameters.WorkArea.Right - overlayPanel.Width - 16;
            overlayPanel.Top = layoutSettings.OverlayTop ?? SystemParameters.WorkArea.Top + 16;
        }
        overlayPanel.FlushPreferences();
        overlayPanel.RefreshSettings(layoutSettings);
        overlayPanel.SetNotes(notes.Values.Select(n => n.Snapshot));


        overlayPanel.ShowActivated = activate;
        overlayPanel.Show();
        WindowPlacement.KeepOnScreen(overlayPanel);
        if (activate)
        {
            if (overlayPanel.WindowState == WindowState.Minimized) overlayPanel.WindowState = WindowState.Normal;
            overlayPanel.Activate();
        }
        SaveOverlayPreferences();
        RefreshPageControls();
    }

    private void SaveOverlayPreferences()
    {
        if (overlayPanel is null) return;
        var candidate = layoutSettings with
            {
                OverlayLeft = overlayPanel.Left, OverlayTop = overlayPanel.Top,
                OverlayTopmost = overlayPanel.SelectedTopmost,
                OverlayColor = overlayPanel.SelectedColor, OverlayVisible = overlayPanel.IsVisible

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
    private Task SetFilteredCollapsedAsync(bool collapsed, string? color) => ChangeCollapsedStateAsync(collapsed, color, true);
}
