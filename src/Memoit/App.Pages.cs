using System.Windows;
using Memoit.Services;
using Memoit.Views;

namespace Memoit;

public partial class App
{
    private sealed class ExpandedSession(string label, List<Dictionary<Guid, Rect>> pages)
    {
        public string Label { get; } = label;
        public List<Dictionary<Guid, Rect>> Pages { get; } = pages;
        public int Index { get; set; }
    }
    private readonly Dictionary<string, ExpandedSession> pageSessions = [];
    private PagerWindow? pageWindow;
    private bool updatingPages;
    private bool IsPageMember(Guid id) => pageSessions.Values.Any(s => s.Pages.Any(p => p.ContainsKey(id)));
    private bool IsLogicallyVisible(Guid id) => notes.TryGetValue(id, out var vm) && vm.Snapshot.IsVisible && vm.Snapshot.DeletedAt is null;

    private void ApplyPageVisibility()
    {
        foreach (var session in pageSessions.Values)
            for (int i = 0; i < session.Pages.Count; i++)
                foreach (var id in session.Pages[i].Keys)
                    if (windows.TryGetValue(id, out var window))
                    {
                        if (i == session.Index && IsLogicallyVisible(id))
                        { if (!window.IsVisible) { window.ShowActivated = false; window.Show(); } }
                        else if (window.IsVisible) window.Hide();
                    }
    }

    private void ChangeExpandedPage(string monitorId, int delta)
    {
        if (arranging || busy || shuttingDown || !pageSessions.TryGetValue(monitorId, out var session)) return;
        session.Index = Math.Clamp(session.Index + delta, 0, session.Pages.Count - 1);
        ApplyPageVisibility(); RefreshList();
    }

    private void RevealPage(Guid id)
    {
        foreach (var session in pageSessions.Values)
        {
            int index = session.Pages.FindIndex(p => p.ContainsKey(id));
            if (index >= 0) { session.Index = index; ApplyPageVisibility(); break; }
        }
    }

    private void RefreshPageControls()
    {
        if (updatingPages || shuttingDown) return;
        updatingPages = true;
        try
        {
            // Visibility changes retain page membership and the current page.
            foreach (var pair in pageSessions.ToArray())
            {
                var session = pair.Value;
                var current = session.Pages.ElementAtOrDefault(session.Index);
                foreach (var page in session.Pages)
                    foreach (var id in page.Keys.Where(id => !notes.TryGetValue(id, out var vm) || vm.Snapshot.DeletedAt is not null || !windows.ContainsKey(id)).ToArray()) page.Remove(id);
                session.Pages.RemoveAll(p => p.Count == 0);
                if (session.Pages.Count == 0) { pageSessions.Remove(pair.Key); continue; }
                int retained = current is null ? -1 : session.Pages.IndexOf(current);
                session.Index = retained >= 0 ? retained : Math.Min(session.Index, session.Pages.Count - 1);
            }
            ApplyPageVisibility();
            var displays = pageSessions.Where(p => p.Value.Pages.Any(page => page.Keys.Any(IsLogicallyVisible))).Select(p => new PageDisplay(p.Key, p.Value.Label, p.Value.Index,
                p.Value.Pages.Count, p.Value.Pages[p.Value.Index].Count, p.Value.Pages.Sum(page => page.Count))).ToArray();
            overlayPanel?.SetPages(displays);
            bool standalone = overlayPanel?.IsVisible != true && displays.Any(p => p.Total > 1);
            if (standalone)
            {
                if (pageWindow is null)
                {
                    pageWindow = new PagerWindow();
                    pageWindow.PageRequested += ChangeExpandedPage;
                }
                pageWindow.SetPages(displays);
                if (!pageWindow.IsVisible)
                {
                    var monitor = MonitorCatalog.All().FirstOrDefault(m => m.Id == displays.First(p => p.Total > 1).MonitorId);
                    if (monitor is not null) { pageWindow.Left = monitor.WorkArea.Left / monitor.Scale + 8; pageWindow.Top = monitor.WorkArea.Bottom / monitor.Scale - 112; }
                    pageWindow.Show();
                    WindowPlacement.KeepOnScreen(pageWindow);
                }
            }
            else pageWindow?.Hide();
        }
        finally { updatingPages = false; }
    }

    private void ClearPageSession()
    {
        pageSessions.Clear();
        pageWindow?.Hide();
        overlayPanel?.SetPages([]);
    }

    private async void DetachFromPages(Guid id)
    {
        bool removed = false;
        foreach (var session in pageSessions.Values)
            foreach (var page in session.Pages)
                removed |= page.Remove(id);
        if (!removed) return;
        RefreshPageControls();
        layoutSettings = layoutSettings with { PagedExpansionActive = pageSessions.Values.Any(s => s.Pages.Count > 1) };
        try { await PersistLayoutAsync(); }
        catch (Exception ex) { ArrangementNotice("페이지 상태를 저장하지 못했습니다: " + ex.Message, true); }
    }
}
