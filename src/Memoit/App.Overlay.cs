using System.Windows;
using Memoit.Services;
using Memoit.Views;

namespace Memoit;

public partial class App
{
    private OverlayPanelWindow? overlayPanel;

    private void ToggleOverlayPanel()
    {
        if (overlayPanel?.IsVisible == true) { overlayPanel.Close(); return; }
        ShowOverlayPanel();
    }

    private void RestoreOverlayPanel()
    {
        if (layoutSettings.OverlayVisible) ShowOverlayPanel();
    }

    private void ShowOverlayPanel()
    {
        if (overlayPanel is null)
        {
            overlayPanel = new OverlayPanelWindow(layoutSettings);
            overlayPanel.CommandRequested += ExecuteCommand;
            overlayPanel.CollapseRequested += (collapsed, color) => RunOperation(() => SetFilteredCollapsedAsync(collapsed, color));
            overlayPanel.PreferencesChanged += SaveOverlayPreferences;
            overlayPanel.Hidden += SaveOverlayPreferences;
            overlayPanel.Left = layoutSettings.OverlayLeft ?? SystemParameters.WorkArea.Right - overlayPanel.Width - 16;
            overlayPanel.Top = layoutSettings.OverlayTop ?? SystemParameters.WorkArea.Top + 16;
        }
        overlayPanel.RefreshSettings(layoutSettings);
        overlayPanel.Show();
        WindowPlacement.KeepOnScreen(overlayPanel);
        SaveOverlayPreferences();
    }

    private void SaveOverlayPreferences()
    {
        if (overlayPanel is null) return;
        try
        {
            SaveLayoutSettings(layoutSettings with
            {
                OverlayLeft = overlayPanel.Left, OverlayTop = overlayPanel.Top,
                OverlayOpacity = overlayPanel.SelectedOpacity, OverlayTopmost = overlayPanel.SelectedTopmost,
                OverlayColor = overlayPanel.SelectedColor, OverlayVisible = overlayPanel.IsVisible
            });
        }
        catch (Exception ex) { Error("데스크톱 패널 설정을 저장하지 못했습니다.", ex); }
    }

    private void CloseOverlayPanel()
    {
        if (overlayPanel is null) return;
        SaveOverlayPreferences();
        overlayPanel.AllowClose = true; overlayPanel.Close();
    }

    private async Task SetFilteredCollapsedAsync(bool collapsed, string? color)
    {
        var selected = windows.Where(p => p.Value.IsVisible && notes[p.Key].Snapshot.DeletedAt is null
            && NoteColors.Matches(notes[p.Key].Snapshot.Color, color)).ToArray();
        foreach (var pair in selected)
            if (pair.Value.IsCollapsed != collapsed) pair.Value.ToggleCollapsed();
        bool saved = true;
        foreach (var pair in selected) saved &= await notes[pair.Key].FlushAsync();
        if (!saved) throw new IOException("일부 메모를 저장하지 못했습니다. 변경 내용은 유지되며 다음 편집 또는 종료 때 다시 저장합니다.");
    }
}
