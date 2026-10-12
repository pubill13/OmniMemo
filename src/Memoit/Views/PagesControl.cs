using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;

namespace Memoit.Views;

public sealed record PageDisplay(string MonitorId, string Label, int Index, int Total, int Count, int TotalNotes);

public sealed class PagesControl : StackPanel
{
    private readonly ComboBox monitors = new() { DisplayMemberPath = nameof(PageDisplay.Label), Margin = new Thickness(2, 4, 2, 4) };
    private readonly TextBlock summary = new() { TextAlignment = TextAlignment.Center, Margin = new Thickness(2, 4, 2, 4), FontSize = 11 };
    private readonly Button previous = new() { Content = "〈 이전", ToolTip = "이전 페이지", MinHeight = 30, Margin = new Thickness(2) };
    private readonly Button next = new() { Content = "다음 〉", ToolTip = "다음 페이지", MinHeight = 30, Margin = new Thickness(2) };
    public event Action<string, int>? PageRequested;

    public PagesControl()
    {
        Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(monitors, "페이지 모니터");
        AutomationProperties.SetName(previous, "이전 페이지");
        AutomationProperties.SetName(next, "다음 페이지");
        AutomationProperties.SetLiveSetting(summary, AutomationLiveSetting.Polite);
        Children.Add(monitors); Children.Add(summary);
        var row = new Grid(); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new());
        Grid.SetColumn(next, 1); row.Children.Add(previous); row.Children.Add(next); Children.Add(row);
        monitors.SelectionChanged += (_, _) => Update();
        previous.Click += (_, _) => Request(-1); next.Click += (_, _) => Request(1);
    }

    public void SetPages(IReadOnlyList<PageDisplay> displays)
    {
        string? selected = (monitors.SelectedItem as PageDisplay)?.MonitorId;
        var pages = displays.Where(p => p.Total > 1).ToArray();
        monitors.ItemsSource = pages;
        monitors.SelectedItem = pages.FirstOrDefault(p => p.MonitorId == selected) ?? pages.FirstOrDefault();
        monitors.Visibility = pages.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
        Visibility = pages.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Update();
    }

    private void Update()
    {
        var page = monitors.SelectedItem as PageDisplay;
        previous.IsEnabled = page is not null && page.Index > 0;
        next.IsEnabled = page is not null && page.Index + 1 < page.Total;
        summary.Text = page is null ? "" : $"{page.Index + 1} / {page.Total} · 현재 {page.Count}개 / 전체 {page.TotalNotes}개";
    }
    private void Request(int delta)
    {
        if (monitors.SelectedItem is PageDisplay page && page.Index + delta >= 0 && page.Index + delta < page.Total)
            PageRequested?.Invoke(page.MonitorId, delta);
    }
}

public sealed class PagerWindow : Window
{
    public bool AllowClose { get; set; }
    private readonly PagesControl pages = new();
    public event Action<string, int>? PageRequested;
    public PagerWindow()
    {
        Title = "OmniMemo · 메모 페이지"; Width = 260; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        ShowActivated = false; Topmost = true; FontSize = 12; FontFamily = new FontFamily("Malgun Gothic");
        Content = new Border { Padding = new Thickness(8), Background = Brushes.WhiteSmoke,
            BorderBrush = Brushes.LightSlateGray, BorderThickness = new Thickness(1), Child = pages };
        pages.PageRequested += (monitor, delta) => PageRequested?.Invoke(monitor, delta);
        Closing += (_, e) => { if (!AllowClose) { e.Cancel = true; Hide(); } };
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(NoActivate);
    }
    private static IntPtr NoActivate(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0021) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE keeps typing focus in the memo.
        return IntPtr.Zero;
    }
    public void SetPages(IReadOnlyList<PageDisplay> displays) => pages.SetPages(displays);
}

