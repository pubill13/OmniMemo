using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using Memoit.ViewModels;
using Memoit.Services;

namespace Memoit.Views;

public partial class NoteWindow : Window
{
    private readonly NoteViewModel vm;
    private bool applyingLayout;
    private (Point TilePosition, Rect ExpandedBounds, string MonitorId)? edgeReturn;
    internal void ClearEdgeReturn() => edgeReturn = null;
    private Point? dragPress;
    private Point dragOrigin;
    private bool dragStarted;
    public bool IsDragging => dragPress.HasValue;
    public event Action? InteractionFinished;
    public event Action? CollapsedStateChanged;
    public bool AllowClose { get; set; }
    public bool IsCollapsed => vm.IsCollapsed;
    public string CollapseGesture { get; set; } = "Ctrl+Shift+Space";
    public event Action? LayoutOptionsRequested;
    public event Action? NewNoteRequested;
    public event Action? SearchRequested;
    public event Action? DeleteRequested;
    public event EventHandler? HideRequested;
    public event Action<Point>? MoveRequested;
    public event Action<bool>? ArrangeTilesRequested;
    public event Action? TileDragCompleted;

    public bool EditorHasFocus => Editor.IsKeyboardFocusWithin;

    public NoteWindow(NoteViewModel vm)
    {
        this.vm = vm;
        InitializeComponent();
        DataContext = vm;
        Editor.Document = vm.Document;
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.Options.ConvertTabsToSpaces = false;
        Editor.TextArea.IndentationStrategy = null;
        InitializeFontSizes();
        InitializeFontFamilies();
        Left = vm.Snapshot.Left; Top = vm.Snapshot.Top;
        ApplyLayout(); UpdateTileTitle();
        vm.PropertyChanged += OnViewModelChanged;
        Closing += OnClosing;
        Closed += (_, _) => vm.PropertyChanged -= OnViewModelChanged;
        LocationChanged += (_, _) => SaveBounds();
        SizeChanged += (_, _) => SaveBounds();
        Loaded += (_, _) => { if (!IsCollapsed && ShowActivated) Editor.Focus(); };
        Editor.LostKeyboardFocus += (_, _) =>
            _ = Dispatcher.BeginInvoke(() => InteractionFinished?.Invoke());
    }
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NoteViewModel.IsCollapsed) && !applyingLayout) ApplyLayout();
        if (e.PropertyName == nameof(NoteViewModel.Title)) UpdateTileTitle();
    }
    private void UpdateTileTitle()
    {
        var enumerator = StringInfo.GetTextElementEnumerator(vm.Title);
        string abbreviation = "";
        for (int i = 0; i < 2 && enumerator.MoveNext(); i++) abbreviation += enumerator.GetTextElement();
        TileTitle.Text = abbreviation;
    }
    private void ApplyLayout()
    {
        applyingLayout = true;
        try
        {
            MinWidth = IsCollapsed ? 36 : 200; MinHeight = IsCollapsed ? 36 : 150;
            Width = IsCollapsed ? 36 : Math.Max(200, vm.Snapshot.Width);
            Height = IsCollapsed ? 36 : Math.Max(150, vm.Snapshot.Height);
            Left = vm.Snapshot.Left; Top = vm.Snapshot.Top;
            ResizeMode = IsCollapsed ? ResizeMode.NoResize : ResizeMode.CanResize;
            WindowChrome.GetWindowChrome(this).ResizeBorderThickness = new Thickness(IsCollapsed ? 0 : 5);
            Root.Visibility = IsCollapsed ? Visibility.Collapsed : Visibility.Visible;
            CollapsedTile.Visibility = IsCollapsed ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { applyingLayout = false; }
    }
    private void SaveBounds()
    {
        if (!IsLoaded || applyingLayout || WindowState != WindowState.Normal) return;
        if (edgeReturn is { } saved && WindowPlacement.GetBounds(this) != saved.ExpandedBounds)
            ClearEdgeReturn();
        if (IsCollapsed) vm.UpdatePosition(Left, Top);
        else vm.UpdateBounds(Left, Top, Width, Height);
    }
    public void BeginArrangement(Rect bounds)
    {
        ClearEdgeReturn();
        applyingLayout = true;
        // Lower constraints before native resizing; raising them here would resize a tile twice.
        MinWidth = 36; MinHeight = 36;
    }
    public void PrepareState(bool collapsed, Rect physicalFinalBounds)
    {
        SaveBounds();
        BeginArrangement(physicalFinalBounds);
        vm.IsCollapsed = collapsed;
        ResizeMode = collapsed ? ResizeMode.NoResize : ResizeMode.CanResize;
        WindowChrome.GetWindowChrome(this).ResizeBorderThickness = new Thickness(collapsed ? 0 : 5);
        Root.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        CollapsedTile.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
    }
    public void EndArrangement()
    {
        try
        {
            var bounds = WindowPlacement.GetBounds(this);
            var dpi = VisualTreeHelper.GetDpi(this);
            Width = bounds.Width / dpi.DpiScaleX;
            Height = bounds.Height / dpi.DpiScaleY;
            MinWidth = IsCollapsed ? 36 : Math.Min(200, Width);
            MinHeight = IsCollapsed ? 36 : Math.Min(150, Height);
        }
        finally { applyingLayout = false; }
        SaveBounds();
    }
    public void ToggleCollapsed(bool focusEditor = true)
    {
        SaveBounds();
        if (!IsLoaded) { vm.IsCollapsed = !vm.IsCollapsed; return; }
        bool collapse = !IsCollapsed;
        var dpi = VisualTreeHelper.GetDpi(this);
        var current = WindowPlacement.GetBounds(this);
        var work = WindowPlacement.GetWorkArea(this);
        var note = vm.Snapshot;
        var target = collapse
            ? new Rect(current.Left, current.Top, 36 * dpi.DpiScaleX, 36 * dpi.DpiScaleY)
            : NotePlacement.ExpandAtTile(current.Location, new Size(note.Width * dpi.DpiScaleX, note.Height * dpi.DpiScaleY), work);
        if (collapse && edgeReturn is { } saved && MonitorCatalog.ForWindow(this).Id == saved.MonitorId)
            target.Location = saved.TilePosition;
        string? sourceMonitor = !collapse && target.Location != current.Location ? MonitorCatalog.ForWindow(this).Id : null;
        PrepareState(collapse, target);
        try { WindowPlacement.MoveTogether([(this, target)]); }
        finally { EndArrangement(); }
        if (sourceMonitor is not null)
            edgeReturn = (current.Location, WindowPlacement.GetBounds(this), sourceMonitor);
        if (collapse) { WindowPlacement.KeepVisible(this); SaveBounds(); }
        CollapsedStateChanged?.Invoke();
        if (!collapse && focusEditor) Editor.Focus();
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (AllowClose) return;
        e.Cancel = true; HideRequested?.Invoke(this, EventArgs.Empty);
    }
    private void OnDragDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var grip = (UIElement)sender;
        // Header buttons handle their own gestures, even when their template is hit.
        for (var source = e.OriginalSource as DependencyObject; source is not null && source != grip;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source))
            if (source is ButtonBase) return;
        if (grip == CollapsedTile) grip.Focus();
        if (!grip.CaptureMouse()) return;
        dragPress = WindowMagnet.GetCursorPosition();
        dragOrigin = WindowPlacement.GetPosition(this);
        dragStarted = false;
        e.Handled = true;
    }
    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (dragPress is not Point start || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = WindowMagnet.GetCursorPosition() - start;
        var dpi = VisualTreeHelper.GetDpi(this);
        if (!dragStarted && Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance * dpi.DpiScaleX
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance * dpi.DpiScaleY) return;
        dragStarted = true;
        MoveRequested?.Invoke(dragOrigin + delta);
        e.Handled = true;
    }
    private void OnDragUp(object sender, MouseButtonEventArgs e)
    {
        if (dragPress is null) return;
        bool expandTile = sender == CollapsedTile && !dragStarted;
        FinishDrag((UIElement)sender);
        if (expandTile) ToggleCollapsed();
        e.Handled = true;
    }
    private void OnDragLostCapture(object sender, MouseEventArgs e)
    {
        if (e.OriginalSource == sender && dragPress.HasValue) FinishDrag((UIElement)sender);
    }
    private void FinishDrag(UIElement grip)
    {
        bool moved = dragStarted;
        dragPress = null;
        dragStarted = false;
        if (grip.IsMouseCaptured) grip.ReleaseMouseCapture();
        if (moved)
        {
            SaveBounds();
            TileDragCompleted?.Invoke();
        }
        InteractionFinished?.Invoke();
    }
    private void OnTileClick(object sender, RoutedEventArgs e)
    {
        if (!dragStarted) ToggleCollapsed();
        e.Handled = true;
    }
    private void OnNew(object sender, RoutedEventArgs e) => NewNoteRequested?.Invoke();
    private void OnSearch(object sender, RoutedEventArgs e) => SearchRequested?.Invoke();
    private void OnDelete(object sender, RoutedEventArgs e) => DeleteRequested?.Invoke();
    private void OnHide(object sender, RoutedEventArgs e) => Close();
    private void OnCollapse(object sender, RoutedEventArgs e) => ToggleCollapsed();
    private void OnLayoutOptions(object sender, RoutedEventArgs e) => LayoutOptionsRequested?.Invoke();
    private void OnArrangeByCreated(object sender, RoutedEventArgs e) => ArrangeTilesRequested?.Invoke(false);
    private void OnArrangeByColor(object sender, RoutedEventArgs e) => ArrangeTilesRequested?.Invoke(true);
    private void OnMenu(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender; button.ContextMenu!.PlacementTarget = button; button.ContextMenu.IsOpen = true;
    }
    private void OnColor(object sender, RoutedEventArgs e) => vm.Color = (string)((MenuItem)sender).Tag;
    private void InitializeFontSizes()
    {
        var smaller = new MenuItem { Header = "1 작게", Tag = -1 };
        var larger = new MenuItem { Header = "1 크게", Tag = 1 };
        smaller.Click += OnFontStep;
        larger.Click += OnFontStep;
        FontSizeMenu.Items.Add(smaller);
        FontSizeMenu.Items.Add(larger);
        FontSizeMenu.Items.Add(new Separator());
        foreach (int size in Enumerable.Range(10, 23).Concat([36, 40, 48, 56, 64, 72]))
        {
            var item = new MenuItem { Header = size.ToString(CultureInfo.InvariantCulture), Tag = size, IsCheckable = true };
            item.Click += OnFont;
            FontSizeMenu.Items.Add(item);
        }
        UpdateFontSizeMenu();
    }

    private void OnFontMenuOpened(object sender, RoutedEventArgs e) => UpdateFontSizeMenu();

    private void UpdateFontSizeMenu()
    {
        ((MenuItem)FontSizeMenu.Items[0]).IsEnabled = vm.FontSize > 10;
        ((MenuItem)FontSizeMenu.Items[1]).IsEnabled = vm.FontSize < 72;
        foreach (var item in FontSizeMenu.Items.OfType<MenuItem>().Where(item => item.IsCheckable))
            item.IsChecked = (int)item.Tag == vm.FontSize;
        FontSizeMenu.Header = $"글자 크기 · {vm.FontSize.ToString(CultureInfo.InvariantCulture)}";
    }

    private void OnFontStep(object sender, RoutedEventArgs e)
    {
        vm.FontSize = Math.Clamp(vm.FontSize + (int)((MenuItem)sender).Tag, 10, 72);
        UpdateFontSizeMenu();
    }

    private void OnFont(object sender, RoutedEventArgs e)
    {
        vm.FontSize = (int)((MenuItem)sender).Tag;
        UpdateFontSizeMenu();
    }
    private void InitializeFontFamilies()
    {
        foreach (var font in NoteFonts.Bundled)
            AddFontFamily(font.Name, font.Label, "앱에 포함된 글꼴 · 별도 설치 없이 사용");
        FontFamilyMenu.Items.Add(new Separator());
        foreach (var font in NoteFonts.Familiar) AddFontFamily(font.Name, font.Label, null);
        FontFamilyMenu.Items.Add(new Separator());
        var more = new MenuItem { Header = "설치된 글꼴 더 보기…" };
        more.Click += (_, _) =>
        {
            var picker = new FontPickerWindow(vm.FontFamily) { Owner = this };
            if (picker.ShowDialog() == true && picker.SelectedFont is { } font) vm.FontFamily = font;
        };
        FontFamilyMenu.Items.Add(more);
    }
    private void AddFontFamily(string name, string label, string? tooltip)
    {
        var item = new MenuItem { Header = label, Tag = name, IsCheckable = true, ToolTip = tooltip };
        item.Click += (_, _) => vm.FontFamily = name;
        FontFamilyMenu.Items.Add(item);
    }
    private void OnFontFamilyMenuOpened(object sender, RoutedEventArgs e)
    {
        foreach (var item in FontFamilyMenu.Items.OfType<MenuItem>().Where(i => i.IsCheckable))
            item.IsChecked = string.Equals((string)item.Tag, vm.FontFamily, StringComparison.OrdinalIgnoreCase);
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (HotkeyService.Matches(CollapseGesture, e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers))
        { if (!e.IsRepeat) ToggleCollapsed(); e.Handled = true; return; }
        if (IsCollapsed && Keyboard.Modifiers == ModifierKeys.None && e.Key is Key.Enter or Key.Space) { ToggleCollapsed(); e.Handled = true; return; }
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.N) { NewNoteRequested?.Invoke(); e.Handled = true; }
        if (e.Key == Key.F) { SearchRequested?.Invoke(); e.Handled = true; }
    }
}
