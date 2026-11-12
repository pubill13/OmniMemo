using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Memoit.Services;
using Memoit.Models;

namespace Memoit.Views;

public enum SettingsTab { Layout, Hotkeys, General, Backup, Help, About }

public sealed class LayoutOptionsWindow : Window
{
    private LayoutSettings draft = new();
    private IReadOnlyList<MonitorDescriptor> monitors = [];
    private readonly ComboBox monitorBox = new() { DisplayMemberPath = "Name", Margin = new Thickness(0, 4, 0, 10) };
    private readonly ComboBox shape = new() { ItemsSource = new[] { "여러 줄로 배치", "가로 한 줄", "세로 한 줄" } };
    private readonly ComboBox sort = new() { ItemsSource = new[] { "생성순", "색상순", "제목순" } };
    private readonly ComboBox corner = new() { ItemsSource = Enum.GetValues<ExpandedCorner>() };
    private Window? previewWindow;
    private readonly ComboBox columnMode = new() { ItemsSource = new[] { "자동", "직접 지정" }, SelectedIndex = 0 };
    private readonly TextBlock originSummary = new(), shapeHint = new(), colorHint = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Dictionary<ExpandedCorner, RadioButton> cornerButtons = [];
    private readonly TabControl tabs = new();
    private readonly CheckBox autoStart = new() { Content = "Windows 로그인 시 OmniMemo 실행" };
    private readonly TextBox x = new(), y = new(), gap = new(), columns = new();
    private readonly TextBox newNoteX = new(), newNoteY = new();
    private readonly TextBox newNoteWidth = new(), newNoteHeight = new();
    private readonly TextBlock newNoteSummary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) };
    private readonly StackPanel hotkeyRows = new();
    private readonly Dictionary<string, TextBox> recorders = [];
    private readonly StackPanel columnFields = new();
    private readonly ComboBox previewMode = new() { ItemsSource = new[] { "접힌 모습", "펼친 모습" }, SelectedIndex = 0 };
    private readonly Canvas preview = new() { Width = 250, Height = 140, Background = Brushes.White, ClipToBounds = true };
    private readonly TextBlock previewStatus = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray };
    private readonly StackPanel previewPager = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock previewPageLabel = new() { VerticalAlignment = VerticalAlignment.Center };
    private Button? previewPrevious, previewNext;
    private int previewPage;
    private readonly Func<string, IReadOnlyList<Note>>? previewNotes;
    private string? selectedId;
    private bool refreshing;
    private bool capturing;
    private string? captureId;
    private string captureOriginal = "";
    public bool AllowClose { get; set; }
    public string? SelectedMonitorId => selectedId;
    public event Action<LayoutSettings, bool>? ApplyRequested;
    public event Action<bool>? CaptureChanged;
    public event Action? Hidden;
    public event Action? BackupRequested;
    public event Action? RestoreRequested;

    public LayoutOptionsWindow(LayoutSettings settings, IReadOnlyList<MonitorDescriptor> monitors, string dataPath = "", bool autoStart = false,
        Func<string, IReadOnlyList<Note>>? previewNotes = null)
    {
        this.previewNotes = previewNotes;
        Title = "OmniMemo · 설정"; Width = 640; Height = 740; MinWidth = 300; MinHeight = 360;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/OmniMemo;component/Assets/OmniMemo.ico"));
        foreach (var (control, name) in new (DependencyObject, string)[] { (x, "시작 X"), (y, "시작 Y"), (gap, "간격"), (columns, "한 줄에 놓을 메모 수"), (shape, "배치 형태"), (sort, "정렬 기준"), (monitorBox, "모니터"), (corner, "펼친 메모 시작 방향") })
            System.Windows.Automation.AutomationProperties.SetName(control, name);
        System.Windows.Automation.AutomationProperties.SetName(newNoteX, "새 메모 시작 X");
        System.Windows.Automation.AutomationProperties.SetName(newNoteY, "새 메모 시작 Y");
        System.Windows.Automation.AutomationProperties.SetName(newNoteWidth, "새 메모 기본 가로");
        System.Windows.Automation.AutomationProperties.SetName(newNoteHeight, "새 메모 기본 세로");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (settings.PanelLeft is double left && settings.PanelTop is double top)
        { WindowStartupLocation = WindowStartupLocation.Manual; Left = left; Top = top; }
        ShowInTaskbar = false; FontFamily = new FontFamily("Malgun Gothic"); FontSize = 13; Background = Brush("#F3F6FA"); Foreground = Brush("#253247");
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/OmniMemo;component/Views/SettingsStyles.xaml", UriKind.Relative) });
        Resources.Add(typeof(Button), CreateButtonStyle());
        Resources.Add(typeof(TabItem), CreateTabStyle());
        foreach (var type in new[] { typeof(TextBox), typeof(ComboBox) })
        { var style = new Style(type); style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 7, 9, 7))); style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White)); style.Setters.Add(new Setter(Control.BorderBrushProperty, Brush("#D5DDE7"))); style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1))); Resources.Add(type, style); }
        foreach (var combo in new[] { monitorBox, shape, sort, corner, columnMode, previewMode }) combo.Style = (Style)Resources["SettingsCombo"];
        var root = new DockPanel { Margin = new Thickness(22, 18, 22, 16) };
        var footer = new Border { Background = Brushes.White, BorderBrush = Brush("#DEE5EE"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 9, 10, 9), Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(Button("적용", Dispatch, true));
        var footerGrid = new Grid(); footerGrid.ColumnDefinitions.Add(new ColumnDefinition()); footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.VerticalAlignment = VerticalAlignment.Center; status.Margin = new Thickness(0, 0, 12, 0);
        footerGrid.Children.Add(status); Grid.SetColumn(actions, 1); footerGrid.Children.Add(actions); footer.Child = footerGrid; root.Children.Add(footer);
        tabs.Background = Brushes.Transparent; tabs.BorderThickness = new Thickness(0); tabs.Padding = new Thickness(0, 12, 0, 0);
        tabs.SelectionChanged += (_, _) => footer.Visibility = tabs.SelectedIndex >= (int)SettingsTab.Help ? Visibility.Collapsed : Visibility.Visible;
        var header = new StackPanel { Margin = new Thickness(2, 0, 2, 12) };
        header.Children.Add(new TextBlock { Text = "설정", FontSize = 25, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A3A") });
        header.Children.Add(new TextBlock { Text = "OmniMemo의 배치, 단축키, 시작과 백업을 관리합니다.", Foreground = Brush("#6B7788"), Margin = new Thickness(0, 4, 0, 0) });
        SizeChanged += (_, _) => header.Visibility = ActualHeight < 420 ? Visibility.Collapsed : Visibility.Visible;
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        root.Children.Add(tabs);
        var layout = new StackPanel { Margin = new Thickness(0, 4, 0, 8), Background = Brushes.Transparent };
        var heading = new DockPanel(); var previewButton = Button("미리보기", OpenPreview); DockPanel.SetDock(previewButton, Dock.Right); heading.Children.Add(previewButton); heading.Children.Add(Label("배치할 화면")); layout.Children.Add(heading); layout.Children.Add(monitorBox);
        layout.Children.Add(Section("새 메모 시작 위치"));
        layout.Children.Add(newNoteSummary);
        var newNoteActions = new WrapPanel();
        var pickNewNote = Button("화면에서 지정", () => PickAnchor(true));
        System.Windows.Automation.AutomationProperties.SetName(pickNewNote, "새 메모 위치를 화면에서 지정");
        newNoteActions.Children.Add(pickNewNote);
        newNoteActions.Children.Add(Button("정돈 시작점 사용", ResetNewNoteAnchor));
        layout.Children.Add(newNoteActions);
        var newNoteCoords = new UniformGridShim();
        var newNoteXField = new StackPanel(); newNoteXField.Children.Add(Label("새 메모 X (DIP)")); newNoteXField.Children.Add(newNoteX);
        var newNoteYField = new StackPanel(); newNoteYField.Children.Add(Label("새 메모 Y (DIP)")); newNoteYField.Children.Add(newNoteY);
        newNoteCoords.Children.Add(newNoteXField); newNoteCoords.Children.Add(newNoteYField);
        layout.Children.Add(new Expander { Header = "세부 조정", Content = newNoteCoords, Margin = new Thickness(0, 6, 0, 4) });
        layout.Children.Add(new TextBlock { Text = "적용 후 새로 만드는 메모부터 사용합니다. X·Y를 모두 비우면 정돈 시작점을 사용합니다.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray });
        layout.Children.Add(Section("접어서 정돈"));
        layout.Children.Add(Label("배치 형태")); layout.Children.Add(shape); layout.Children.Add(shapeHint);
        var originRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) }; var pickButton = Button("화면에서 지정", PickAnchor); DockPanel.SetDock(pickButton, Dock.Right); originRow.Children.Add(pickButton); originSummary.VerticalAlignment = VerticalAlignment.Center; originRow.Children.Add(originSummary); layout.Children.Add(originRow);
        var coords = new UniformGridShim(); var cx = new StackPanel(); cx.Children.Add(Label("시작 X (DIP)")); cx.Children.Add(x); var cy = new StackPanel(); cy.Children.Add(Label("시작 Y (DIP)")); cy.Children.Add(y); coords.Children.Add(cx); coords.Children.Add(cy);
        layout.Children.Add(new Expander { Header = "세부 조정", Content = coords, Margin = new Thickness(0, 6, 0, 4) });
        columnFields.Children.Add(Label("한 줄에 놓을 메모 수")); var columnRow = new UniformGridShim(); columnMode.Margin = new Thickness(0, 0, 8, 0); columnRow.Children.Add(columnMode); columnRow.Children.Add(columns); columnFields.Children.Add(columnRow); layout.Children.Add(columnFields);
        layout.Children.Add(Section("펼쳐서 정돈"));
        layout.Children.Add(Label("이 모서리에서 화면 안쪽으로 배치합니다"));
        var directions = new UniformGridShim();
        foreach (var direction in new[] { ExpandedCorner.TopLeft, ExpandedCorner.TopRight, ExpandedCorner.BottomLeft, ExpandedCorner.BottomRight })
        {
            var text = (string)new CornerLabelConverter().Convert(direction, typeof(string), null!, CultureInfo.InvariantCulture);
            var button = new RadioButton { Content = text, Style = (Style)Resources["SettingsCorner"], GroupName = "Corner", Margin = new Thickness(5), Padding = new Thickness(8) };
            System.Windows.Automation.AutomationProperties.SetName(button, "시작 모서리 " + text);
            button.Checked += (_, _) => corner.SelectedItem = direction; cornerButtons[direction] = button; directions.Children.Add(button);
        }
        layout.Children.Add(directions);
        layout.Children.Add(Section("공통"));
        var commonFields = new Grid(); commonFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) }); commonFields.ColumnDefinitions.Add(new ColumnDefinition());
        var sortField = new StackPanel { Margin = new Thickness(0, 0, 12, 0) }; sortField.Children.Add(Label("정렬 기준")); sortField.Children.Add(sort); commonFields.Children.Add(sortField);
        var gapField = new StackPanel(); gapField.Children.Add(Label("간격 (0–24 DIP)")); gapField.Children.Add(gap); Grid.SetColumn(gapField, 1); commonFields.Children.Add(gapField); layout.Children.Add(commonFields);
        colorHint.Text = "색이 바뀌면 새 줄에 배치하며, 같은 색 안에서는 먼저 만든 메모부터 놓습니다. 줄 구분은 여러 줄 배치에만 적용됩니다."; colorHint.Foreground = Brushes.DimGray; layout.Children.Add(colorHint);
        corner.ItemTemplate = CornerTemplate();
        layout.Children.Add(new TextBlock { Text = "정돈 배치 규칙은 접어서 정돈·펼쳐서 정돈에 적용됩니다. 모두 접기·모두 펼치기는 현재 위치를 유지합니다.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Foreground = Brushes.DimGray });
        GroupSections(layout);
        tabs.Items.Add(new TabItem { Header = "배치", Content = new ScrollViewer { Content = layout, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        var hotkeys = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var reset = Button("모든 단축키 기본값", ResetAll); DockPanel.SetDock(reset, Dock.Bottom); hotkeys.Children.Add(reset);
        var hotkeyContent = new StackPanel(); hotkeyContent.Children.Add(new TextBlock { Text = "자주 쓰는 동작만 등록해 두면 메모를 빠르게 제어할 수 있습니다.", Foreground = Brush("#6B7788"), Margin = new Thickness(2, 0, 2, 12) }); hotkeyContent.Children.Add(hotkeyRows);
        hotkeys.Children.Add(new ScrollViewer { Content = hotkeyContent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        tabs.Items.Add(new TabItem { Header = "단축키", Content = hotkeys });
        tabs.Items.Add(new TabItem { Header = "일반", Content = BuildGeneral() });
        tabs.Items.Add(new TabItem { Header = "백업", Content = BuildBackup(dataPath) });
        tabs.Items.Add(new TabItem { Header = "도움말", Content = BuildHelp(dataPath) });
        tabs.Items.Add(new TabItem { Header = "버전", Content = BuildAbout() }); Content = root;
        SetAutoStart(autoStart);
        Loaded += (_, _) => FitToWorkArea();
        monitorBox.SelectionChanged += (_, _) => SwitchMonitor();
        foreach (var field in new[] { x, y, gap, columns, newNoteX, newNoteY }) field.TextChanged += (_, _) => RefreshPreview();
        foreach (var field in new[] { shape, sort, corner, columnMode, previewMode }) field.SelectionChanged += (_, _) => RefreshPreview();
        IsVisibleChanged += (_, _) => { if (IsVisible) RefreshPreview(); else ClosePreview(); };
        Closing += (_, e) => { ClosePreview(); if (!AllowClose) { e.Cancel = true; HidePanel(); } };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; if (capturing) CancelCapture(); else HidePanel(); } };
        RefreshSettings(settings, monitors);
    }

    public void SelectTab(SettingsTab tab) => tabs.SelectedIndex = (int)tab;
    public void SetAutoStart(bool value)
    {
        bool previous = refreshing; refreshing = true; autoStart.IsChecked = value; refreshing = previous;
    }
    private UIElement BuildGeneral()
    {
        var content = new StackPanel { Margin = new Thickness(0, 4, 0, 8), Background = Brushes.Transparent };
        content.Children.Add(Section("시작")); content.Children.Add(autoStart);
        content.Children.Add(Section("새 메모 기본 크기"));
        var sizes = new UniformGridShim();
        var widthLabel = Label("가로 (DIP, 최소 200)"); widthLabel.TextWrapping = TextWrapping.Wrap;
        var heightLabel = Label("세로 (DIP, 최소 150)"); heightLabel.TextWrapping = TextWrapping.Wrap;
        var width = new StackPanel(); width.Children.Add(widthLabel); width.Children.Add(newNoteWidth);
        var height = new StackPanel(); height.Children.Add(heightLabel); height.Children.Add(newNoteHeight);
        sizes.Children.Add(width); sizes.Children.Add(height); content.Children.Add(sizes);
        content.Children.Add(Button("기본 크기로 복원", ResetNewNoteSize));
        content.Children.Add(new TextBlock { Text = "적용 후 새로 만드는 메모부터 사용합니다. 기존 메모의 크기는 유지합니다. 화면 배율 100%에서 1 DIP는 1픽셀입니다.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray });
        GroupSections(content);
        return new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    private UIElement BuildBackup(string dataPath)
    {
        var content = new StackPanel { Margin = new Thickness(0, 4, 0, 8), Background = Brushes.Transparent };
        content.Children.Add(Section("백업과 복원"));
        content.Children.Add(new TextBlock { Text = "매일 첫 저장 후 자동 백업하며 최근 7개를 보관합니다. 휴지통 메모도 포함됩니다.", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(Button("백업 파일 저장…", () => BackupRequested?.Invoke()));
        content.Children.Add(Button("백업에서 복원…", () => RestoreRequested?.Invoke()));
        content.Children.Add(new TextBlock { Text = "복원하면 전체 메모가 백업 내용으로 바뀝니다. 현재 데이터는 교체 전에 안전 백업합니다.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 8, 0, 8) });
        content.Children.Add(Section("데이터 저장 위치"));
        var path = new TextBox { Text = dataPath, IsReadOnly = true, TextWrapping = TextWrapping.Wrap };
        System.Windows.Automation.AutomationProperties.SetName(path, "데이터 저장 위치");
        content.Children.Add(path);
        GroupSections(content);
        return new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    private UIElement BuildHelp(string dataPath)
    {
        var content = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
        void Row(string name, string result)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.5, GridUnitType.Star) });
            var label = HelpText(name);
            label.FontWeight = FontWeights.Bold;
            label.Margin = new Thickness(0, 0, 10, 0);
            var description = HelpText(result);
            description.Margin = new Thickness(0);
            Grid.SetColumn(description, 1);
            grid.Children.Add(label);
            grid.Children.Add(description);
            content.Children.Add(grid);
        }
        content.Children.Add(Section("시작하기"));
        Row("새 메모", "패널이나 트레이에서 새 메모를 누르면 바로 입력할 수 있습니다.");
        Row("접기와 펼치기", "타일을 클릭하거나 Enter 또는 Space를 누르면 펼쳐집니다. 화면 가장자리에서 안쪽으로 보정된 메모는 다시 접으면 원래 타일 자리로 돌아갑니다. 직접 옮기거나 크기를 조절한 뒤에는 현재 위치에 접힙니다.");
        Row("옮기기", "메모 상단의 빈 곳이나 타일을 드래그하세요. 가장자리에 가까워지면 크기를 유지한 채 붙습니다. 크기를 바꾸려면 펼친 창의 테두리를 끌어 조절하세요.");
        Row("새 메모 기본 크기", "설정 → 일반에서 가로·세로를 지정하고 적용하세요. 이후 만드는 메모부터 반영하며 기존 메모 크기는 유지합니다. 기본값은 320×300 DIP입니다.");
        Row("숨기기", "메모의 ×를 누르면 창을 숨깁니다. 목록에서 다시 열 수 있습니다.");

        content.Children.Add(Section("새 메모 위치·색상"));
        Row("시작 위치", "설정 → 배치 → 새 메모 시작 위치에서 모니터별로 지정하세요. 별도로 지정하기 전이나 ‘정돈 시작점 사용’을 선택하면 접어서 정돈 시작점을 따릅니다. 화면 가장자리에서는 펼친 창이 보이도록 보정합니다.");
        Row("연속 생성", "같은 위치에 펼친 메모가 보이면 24 DIP 간격으로 대각선으로 겹칩니다. 경계에 닿으면 시작 위치부터 다시 사용합니다.");
        Row("모니터", "메모의 ＋는 그 메모의 화면에 엽니다. 다른 곳에서 만들면 마지막 활성 메모의 화면을, 기록이 없으면 마우스가 있는 화면을 사용합니다.");
        Row("색상", "메모의 ＋와 Ctrl+N은 원본 색을, 패널은 선택한 색을 사용합니다. 패널의 ‘전체’와 목록·트레이·전역 단축키는 마지막 활성 메모 색을 따르며 기본색은 노랑입니다.");

        content.Children.Add(Section("편집"));
        Row("제목", "본문에서 비어 있지 않은 첫 줄이 제목입니다.");
        Row("글꼴과 크기", "⋯ 메뉴에서 포함 글꼴 또는 설치된 글꼴과 글자 크기를 선택하세요.");
        Row("색상 변경", "⋯ 메뉴에서 현재 메모의 색을 바꿀 수 있습니다.");
        Row("편집 실행 취소", "Ctrl+Z로 입력을 되돌리고 Ctrl+Y로 다시 실행합니다.");
        Row("항상 위", "메모의 압정을 누르면 다른 일반 창 위에 표시됩니다.");

        content.Children.Add(Section("전체 조작과 정돈"));
        Row("모두 접기", "각 메모의 현재 위치에서 타일로 접고 페이지를 해제합니다.");
        Row("모두 펼치기", "각 메모의 현재 위치에서 펼치고 페이지를 해제합니다. 화면 가장자리에서만 위치를 보정하며 겹침을 허용합니다.");
        Row("모두 숨기기", "메모 창을 숨깁니다. 위치·접힘·압정과 현재 페이지를 유지합니다.");
        Row("모두 보이기", "현재 페이지 구성을 유지하며 메모 창을 다시 표시합니다.");
        Row("접어서 정돈", "보이는 메모를 타일로 접어 모니터별 시작점과 간격에 맞춰 배치합니다.");
        Row("펼쳐서 정돈", "보이는 메모를 선택한 모서리부터 펼쳐 배치합니다. 공간이 부족하면 페이지로 나눕니다.");
        Row("앞으로 표시", "모두 숨기기를 제외한 위 명령은 대상 메모를 일반 창 앞으로 가져옵니다.");
        Row("설정 적용", "배치 탭의 적용은 설정을 저장합니다. 기존 메모를 옮기려면 정돈을 실행하세요.");

        content.Children.Add(Section("패널·미니패널·트레이"));
        Row("패널 열기", "트레이 아이콘을 더블클릭하면 마지막 모드의 패널이 앞으로 나옵니다.");
        Row("미니패널 전환", "패널 헤더의 축소 아이콘으로 줄입니다. 미니패널 둘째 줄의 복귀 아이콘으로 일반 크기를 복원합니다.");
        Row("패널 이동과 고정", "상단 손잡이를 드래그해 옮기고 압정으로 항상 위 표시를 전환합니다.");
        Row("색상별 조작", "일반 패널에서 원하는 색을 선택하면 해당 색의 보이는 메모만 접거나 펼칠 수 있습니다.");
        Row("페이지 이동", "일반 패널에서 이전·다음을 누르세요. 미니패널이거나 패널이 숨겨져 있으면 별도 페이지 창을 사용합니다.");

        content.Children.Add(Section("목록·휴지통"));
        Row("목록·검색", "목록에서 메모를 찾아 열면 해당 페이지로 이동합니다. 체크박스와 우클릭으로 여러 메모를 관리할 수 있습니다.");
        Row("삭제 실행 취소", "휴지통으로 옮긴 뒤 10초 동안 가장 최근 삭제를 되돌릴 수 있습니다. 목록·일반 패널 또는 별도 안내 창의 실행 취소를 누르세요.");
        Row("복원", "휴지통에서 선택한 메모를 복원할 수 있습니다.");
        Row("영구 삭제 주의", "휴지통 비우기는 검색 결과와 관계없이 휴지통 전체를 영구 삭제합니다.");

        content.Children.Add(Section("단축키"));
        Row("기본 키", "아래는 전역 단축키의 기본값입니다. 사용자 지정 키는 설정의 단축키 탭에서 확인하세요.");
        Row("메모 안에서", "Ctrl+N은 새 메모를 만들고 Ctrl+F는 목록·검색을 엽니다.");
        foreach (var pair in HotkeyDefaults.Create())
            Row(HotkeyDefaults.Labels[pair.Key], string.IsNullOrEmpty(pair.Value) ? "미지정" : pair.Value);

        content.Children.Add(Section("저장·백업·복구"));
        Row("자동 저장", "입력이 멈추면 약 0.5초 후 저장합니다. 계속 입력할 때도 최대 2초 간격으로 저장합니다.");
        Row("저장 실패", "메모의 상태 표시에서 오류를 확인하고 트레이의 ‘저장 다시 시도’를 누르세요.");
        Row("백업", "하루 첫 저장 후 자동 백업하며 최근 7개를 보관합니다. 수동 백업은 설정의 백업 탭에서 실행합니다.");
        Row("복원 전 주의", "복원하면 현재 메모 데이터 전체가 백업 내용으로 교체됩니다. 앱은 교체 전에 현재 데이터의 안전 백업을 만듭니다.");
        Row("데이터 폴더", "아래 경로를 선택해 Ctrl+C로 복사할 수 있습니다.");
        var path = new TextBox { Text = dataPath, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontSize = 14 };
        System.Windows.Automation.AutomationProperties.SetName(path, "데이터 폴더 경로");
        content.Children.Add(path);
        GroupSections(content);
        return new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
    private UIElement BuildAbout()
    {
        var version = typeof(LayoutOptionsWindow).Assembly.GetName().Version?.ToString(3) ?? "알 수 없음";
        var content = new StackPanel { Margin = new Thickness(0, 4, 0, 8), Background = Brushes.Transparent };
        content.Children.Add(new TextBlock { Text = "OmniMemo", FontSize = 26, FontWeight = FontWeights.Bold });
        content.Children.Add(new TextBlock { Text = "개인용 로컬 스티커 메모", Foreground = Brushes.DimGray, Margin = new Thickness(0, 4, 0, 16) });
        content.Children.Add(Section("현재 버전"));
        content.Children.Add(new TextBlock { Text = version, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        content.Children.Add(Section("최근 변경"));
        content.Children.Add(HelpText("1.13.4\n일반 설정에서 새 메모 기본 크기를 지정하고 기본값으로 복원할 수 있습니다. 기존 메모 크기는 유지합니다. 자동 저장·첫 백업을 포함한 5분 편집 응답성을 검증했으며 사용자 환경의 멈춤 원인은 아직 미확정입니다."));
        content.Children.Add(HelpText("1.13.3\n새 메모 시작 위치를 모니터별로 따로 지정할 수 있습니다. 삭제 실행 취소 안내창의 여백과 크기 계산을 개선했습니다."));
        content.Children.Add(HelpText("1.13.2\n화면 가장자리에서 개별 타일을 펼치고 다시 접으면 원래 자리로 돌아갑니다. 헤더를 모서리로 드래그해도 메모 크기를 유지합니다."));
        content.Children.Add(HelpText("1.13.1\n새 메모는 접어서 정돈 시작점 근처에 열립니다. 도움말을 기능별로 나누고 데이터 경로를 복사할 수 있게 했습니다."));
        content.Children.Add(HelpText("1.13.0\n패널에 240×88 크기의 2줄 아이콘 미니 모드를 추가했습니다. 마지막 모드와 압정을 저장하며 트레이 더블클릭은 패널을 표시합니다."));
        content.Children.Add(HelpText("1.12.2\n전체 표시·상태 변경·정돈 시 메모를 앞으로 가져옵니다. 자동 저장 때 숨겨진 목록과 변하지 않은 패널을 반복해서 만드는 작업을 줄이고, 긴 본문의 제목 처리와 저장 상태 알림을 최적화했습니다."));
        content.Children.Add(HelpText("1.12.1\n설정의 탭·카드·입력란과 단축키 구획을 정리하고, 드롭다운·모서리 선택·적용 버튼의 스타일을 통일했습니다."));
        content.Children.Add(HelpText("1.12.0\n전체 조작과 정돈을 분리했습니다. 모두 접기·펼치기는 현재 위치를 유지하고, 정돈은 설정된 위치에 배치합니다. 압정의 클릭 영역과 색상 표시를 개선했습니다."));
        content.Children.Add(HelpText("1.11.4\n설정에 도움말과 버전 탭을 추가했습니다. 현재 버전은 실행 중인 프로그램에서 자동으로 읽습니다."));
        content.Children.Add(HelpText("1.11.3\n직접 접기 시 현재 메모 위치에 타일을 남깁니다. 이전 정돈 위치는 명시적인 ‘접어서 정돈’에서만 사용합니다."));
        content.Children.Add(HelpText("1.11.2\n휴지통 화면의 제목·날짜·버튼 정렬을 개선하고 휴지통 비우기를 추가했습니다."));
        content.Children.Add(HelpText("1.11.1\n펼친 메모를 겹치지 않는 페이지로 나누고 목록에서 해당 페이지를 열 수 있게 했습니다."));
        content.Children.Add(new TextBlock { Text = ".NET 10 · WPF · SQLite · Windows x64", Foreground = Brushes.DimGray, Margin = new Thickness(0, 16, 0, 0) });
        GroupSections(content);
        return new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    private static TextBlock HelpText(string text) => new() { Text = text, FontSize = 14, LineHeight = 22, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10) };
    private void FitToWorkArea()
    {
        var screen = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle).WorkingArea;
        var scale = VisualTreeHelper.GetDpi(this);
        MinWidth = Math.Min(MinWidth, screen.Width / scale.DpiScaleX); MinHeight = Math.Min(MinHeight, screen.Height / scale.DpiScaleY);
        Width = Math.Min(Width, screen.Width / scale.DpiScaleX); Height = Math.Min(Height, screen.Height / scale.DpiScaleY);
        Left = Math.Clamp(Left, screen.Left / scale.DpiScaleX, screen.Right / scale.DpiScaleX - Width);
        Top = Math.Clamp(Top, screen.Top / scale.DpiScaleY, screen.Bottom / scale.DpiScaleY - Height);
    }
    private void OpenPreview()
    {
        if (previewWindow is not null) { previewWindow.Activate(); return; }
        var content = new StackPanel { Margin = new Thickness(18) }; content.Children.Add(previewMode);
        var frame = new Border { Child = preview, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Margin = new Thickness(0, 12, 0, 12), HorizontalAlignment = HorizontalAlignment.Center }; content.Children.Add(frame);
        previewPager.Children.Clear();
        previewPrevious = Button("이전 페이지", () => { previewPage--; RefreshPreview(); });
        previewNext = Button("다음 페이지", () => { previewPage++; RefreshPreview(); });
        previewPager.Children.Add(previewPrevious); previewPager.Children.Add(previewPageLabel); previewPager.Children.Add(previewNext);
        content.Children.Add(previewPager); content.Children.Add(previewStatus);
        var window = new Window { Title = "OmniMemo · 배치 미리보기", Width = 440, Height = 360, MinWidth = 300, MinHeight = 250, Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, ShowInTaskbar = false, FontFamily = FontFamily, Background = Brush("#F3F6FA"), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        if (IsVisible) window.Owner = this;
        window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; window.Close(); } };
        window.Closed += (_, _) => { content.Children.Clear(); frame.Child = null; previewWindow = null; };
        previewWindow = window; RefreshPreview(); window.Show();
    }
    private void ClosePreview() => previewWindow?.Close();

    private static DataTemplate CornerTemplate()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding { Converter = new CornerLabelConverter() });
        return new DataTemplate { VisualTree = text };
    }
    private sealed class CornerLabelConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
        {
            ExpandedCorner.TopLeft => "왼쪽 위", ExpandedCorner.TopRight => "오른쪽 위",
            ExpandedCorner.BottomLeft => "왼쪽 아래", ExpandedCorner.BottomRight => "오른쪽 아래", _ => "오른쪽 위"
        };
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    private static void GroupSections(StackPanel parent)
    {
        var children = parent.Children.Cast<UIElement>().ToArray(); parent.Children.Clear(); StackPanel? group = null;
        foreach (var child in children)
        {
            if (child is TextBlock { Tag: "Section" })
            {
                group = new StackPanel();
                parent.Children.Add(new Border { Child = group, Background = Brushes.White, CornerRadius = new CornerRadius(8), BorderBrush = Brush("#E1E6ED"), BorderThickness = new Thickness(1), Padding = new Thickness(18, 12, 18, 16), Margin = new Thickness(0, 0, 0, 12) });
            }
            if (group is null) parent.Children.Add(child); else group.Children.Add(child);
        }
    }
    private static SolidColorBrush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;

    private static Style CreateTabStyle()
    {
        var style = new Style(typeof(TabItem));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(13, 9, 13, 9)));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brush("#687587")));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        var frame = new FrameworkElementFactory(typeof(Border)); frame.Name = "TabFrame";
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        frame.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        foreach (var property in new[] { Border.BackgroundProperty, Border.BorderBrushProperty, Border.PaddingProperty })
            frame.SetBinding(property, new System.Windows.Data.Binding(property.Name) { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header"); frame.AppendChild(presenter);
        var template = new ControlTemplate(typeof(TabItem)) { VisualTree = frame };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, Brush("#E9EEF5"))); template.Triggers.Add(hover);
        var selected = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White));
        selected.Setters.Add(new Setter(Control.BorderBrushProperty, Brush("#D5DDE7")));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, Brush("#253247")));
        selected.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold)); template.Triggers.Add(selected);
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Control.BorderBrushProperty, Brush("#3265AA"))); template.Triggers.Add(focus);
        style.Setters.Add(new Setter(Control.TemplateProperty, template)); return style;
    }

    private static Style CreateButtonStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(235, 239, 244))));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(38, 47, 60))));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(206, 215, 225))));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        var border = new FrameworkElementFactory(typeof(Border)); border.Name = "Frame";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetBinding(FrameworkElement.MarginProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) }); border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        foreach (var (property, color) in new[] { (UIElement.IsMouseOverProperty, Color.FromRgb(221, 230, 240)), (System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Color.FromRgb(202, 216, 232)) })
        { var trigger = new Trigger { Property = property, Value = true }; trigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(color), "Frame")); template.Triggers.Add(trigger); }
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true }; focus.Setters.Add(new Setter(Border.BorderBrushProperty, Brushes.SteelBlue, "Frame")); template.Triggers.Add(focus);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .5, "Frame")); template.Triggers.Add(disabled);
        style.Setters.Add(new Setter(Control.TemplateProperty, template)); return style;
    }

    private static TextBlock Label(string text) => new() { Text = text, Foreground = Brush("#596779"), Margin = new Thickness(0, 8, 0, 6) };
    private static TextBlock Section(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 15, Foreground = Brush("#253247"), Margin = new Thickness(0, 2, 0, 10), Tag = "Section" };
    private static Button Button(string text, Action action, bool primary = false)
    {
        var button = new Button { Content = text, Padding = new Thickness(13, 8, 13, 8), Margin = new Thickness(2, 4, 2, 4), MinHeight = 34, Cursor = Cursors.Hand };
        if (primary) { button.Background = Brush("#E9EEDE"); button.BorderBrush = Brush("#BDCDA8"); button.FontWeight = FontWeights.SemiBold; button.MinWidth = 86; }
        button.Click += (_, _) => action(); return button;
    }
    public void RefreshSettings(LayoutSettings settings, IReadOnlyList<MonitorDescriptor> available)
    {
        EndCapture();
        refreshing = true;
        draft = settings with { Monitors = new(settings.Monitors), Hotkeys = new(settings.Hotkeys) };
        newNoteWidth.Text = settings.NewNoteWidth.ToString(CultureInfo.InvariantCulture);
        newNoteHeight.Text = settings.NewNoteHeight.ToString(CultureInfo.InvariantCulture);
        monitors = available; selectedId = null;
        monitorBox.ItemsSource = monitors;
        monitorBox.SelectedItem = monitors.FirstOrDefault(m => m.Id == settings.SelectedMonitor) ?? monitors.FirstOrDefault();
        refreshing = false; LoadMonitor(); BuildRecorders(); status.Foreground = Brushes.DimGray; status.Text = "변경한 설정은 적용 후 저장됩니다.";
    }
    private void SwitchMonitor()
    {
        if (refreshing) return;
        if (!StoreMonitor()) { refreshing = true; monitorBox.SelectedItem = monitors.FirstOrDefault(m => m.Id == selectedId); refreshing = false; return; }
        LoadMonitor();
    }
    private void LoadMonitor()
    {
        refreshing = true;
        selectedId = (monitorBox.SelectedItem as MonitorDescriptor)?.Id;
        var value = selectedId is not null ? draft.GetMonitor(selectedId) : new MonitorLayout();
        x.Text = value.X.ToString(CultureInfo.InvariantCulture); y.Text = value.Y.ToString(CultureInfo.InvariantCulture);
        newNoteX.Text = value.NewNoteX?.ToString(CultureInfo.InvariantCulture) ?? "";
        newNoteY.Text = value.NewNoteY?.ToString(CultureInfo.InvariantCulture) ?? "";
        gap.Text = value.Gap.ToString(CultureInfo.InvariantCulture); columns.Text = value.Columns.ToString(CultureInfo.InvariantCulture);
        shape.SelectedIndex = (int)value.Shape; sort.SelectedIndex = (int)value.Sort;
        corner.SelectedItem = value.ExpandedCorner; cornerButtons[value.ExpandedCorner].IsChecked = true;
        columnMode.SelectedIndex = value.Columns == 0 ? 0 : 1; if (value.Columns == 0) columns.Text = "4";
        refreshing = false;
        RefreshPreview();
    }
    private bool StoreMonitor()
    {
        if (selectedId is null) return true;
        if (!TryReadMonitor(out var value)) { ShowError("새 메모 X·Y는 모두 지정하거나 모두 비워 두세요. 위치는 0 이상, 간격은 0–24, 메모 수는 1–30입니다."); return false; }
        draft.Monitors[selectedId] = value;
        return true;
    }
    private bool TryReadMonitor(out MonitorLayout value)
    {
        value = new MonitorLayout();
        if (!double.TryParse(x.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var px) || !double.TryParse(y.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var py)
            || !double.TryParse(gap.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var spacing)
            || !double.IsFinite(px) || !double.IsFinite(py) || !double.IsFinite(spacing) || px < 0 || py < 0 || spacing < 0 || spacing > 24)
            return false;
        int count = 0;
        if (shape.SelectedIndex == (int)LayoutShape.Grid && columnMode.SelectedIndex == 1 && (!int.TryParse(columns.Text, out count) || count < 1 || count > 30)) return false;
        if (shape.SelectedIndex != (int)LayoutShape.Grid) count = selectedId is string id ? draft.GetMonitor(id).Columns : 0;
        if (shape.SelectedIndex < 0 || sort.SelectedIndex < 0) return false;
        double? nx = null, ny = null;
        if (!string.IsNullOrWhiteSpace(newNoteX.Text) || !string.IsNullOrWhiteSpace(newNoteY.Text))
        {
            if (!double.TryParse(newNoteX.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedX)
                || !double.TryParse(newNoteY.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedY)
                || !double.IsFinite(parsedX) || !double.IsFinite(parsedY) || parsedX < 0 || parsedY < 0) return false;
            nx = parsedX; ny = parsedY;
        }
        var previous = selectedId is string monitorId ? draft.GetMonitor(monitorId) : new MonitorLayout();
        value = previous with { X = px, Y = py, NewNoteX = nx, NewNoteY = ny, Gap = spacing, Columns = count, Shape = (LayoutShape)shape.SelectedIndex, Sort = (LayoutSort)sort.SelectedIndex, ExpandedCorner = corner.SelectedItem is ExpandedCorner direction ? direction : ExpandedCorner.TopRight };
        return true;
    }
    public void RefreshPreview()
    {
        if (refreshing) return;
        columnFields.Visibility = shape.SelectedIndex == (int)LayoutShape.Grid ? Visibility.Visible : Visibility.Collapsed;
        columns.Visibility = columnMode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        originSummary.Text = $"시작 위치  X {x.Text} · Y {y.Text}";
        newNoteSummary.Text = string.IsNullOrWhiteSpace(newNoteX.Text) && string.IsNullOrWhiteSpace(newNoteY.Text)
            ? "정돈 시작점 사용" : $"별도 시작 위치  X {newNoteX.Text} · Y {newNoteY.Text}";
        shapeHint.Foreground = Brush("#778397"); shapeHint.FontSize = 11; shapeHint.Margin = new Thickness(0, 6, 0, 2);
        shapeHint.Text = shape.SelectedIndex switch { 0 => "▦  오른쪽으로 채운 뒤 다음 줄로 내려갑니다", 1 => "▪ ▪ ▪ →  한 줄로 오른쪽에 놓습니다", _ => "▤ ↓  한 줄로 아래에 놓습니다" };
        colorHint.Visibility = sort.SelectedIndex == (int)LayoutSort.Color ? Visibility.Visible : Visibility.Collapsed;
        preview.Children.Clear();
        previewPager.Visibility = Visibility.Collapsed;
        if (!TryReadMonitor(out var options)) { previewStatus.Text = "위치·간격·메모 수를 확인하세요. 간격 0–24, 직접 지정 1–30."; return; }
        if (monitorBox.SelectedItem is not MonitorDescriptor monitor) { previewStatus.Text = "모니터를 선택하세요."; return; }
        if (!double.IsFinite(options.X * monitor.Scale) || !double.IsFinite(options.Y * monitor.Scale))
        { previewStatus.Text = "시작 위치가 너무 큽니다. 화면 안의 위치를 입력하세요."; return; }
        var source = previewNotes?.Invoke(monitor.Id) ?? [];
        bool sample = !source.Any(n => n.IsVisible && n.DeletedAt is null);
        if (sample) source = Enumerable.Range(0, 6).Select(i => new Note { Color = i < 3 ? "#FFF2B2" : "#FFDDE7", Body = $"예시 {i + 1}", CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(i) }).ToArray();
        bool collapsed = previewMode.SelectedIndex == 0;
        var notes = source.Where(n => n.IsVisible && n.DeletedAt is null).Select(n => n with { IsCollapsed = collapsed }).ToArray();
        ArrangementResult result;
        if (collapsed) result = NoteArrangement.ArrangeCollapsed(notes, monitor.WorkArea, options, monitor.Scale);
        else
        {
            var pages = NoteArrangement.ArrangeExpandedPages(notes, monitor.WorkArea, options, options.ExpandedCorner, monitor.Scale,
                orderedIds: draft.ArrangementOrder.GetValueOrDefault(monitor.Id));
            if (pages.Count > 1 && monitor.WorkArea.Height > 220 * monitor.Scale)
            {
                var area = monitor.WorkArea;
                area.Height -= 120 * monitor.Scale;
                pages = NoteArrangement.ArrangeExpandedPages(notes, area, options, options.ExpandedCorner, monitor.Scale,
                    orderedIds: draft.ArrangementOrder.GetValueOrDefault(monitor.Id));
            }
            previewPage = Math.Clamp(previewPage, 0, Math.Max(0, pages.Count - 1));
            result = new(pages.Count == 0 ? new Dictionary<Guid, Rect>() : pages[previewPage]);
            previewPager.Visibility = pages.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            previewPageLabel.Text = $" {previewPage + 1} / {pages.Count} ";
            if (previewPrevious is not null) previewPrevious.IsEnabled = previewPage > 0;
            if (previewNext is not null) previewNext.IsEnabled = previewPage < pages.Count - 1;
        }
        double ratio = Math.Min(360 / monitor.WorkArea.Width, 220 / monitor.WorkArea.Height);
        preview.Width = monitor.WorkArea.Width * ratio; preview.Height = monitor.WorkArea.Height * ratio;
        var lookup = notes.ToDictionary(n => n.Id);
        foreach (var (id, bounds) in result.Bounds.Reverse())
        {
            var note = lookup[id];
            var item = new Border { Width = bounds.Width * ratio, Height = bounds.Height * ratio,
                Background = (Brush)new BrushConverter().ConvertFromString(note.Color)!, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(.5), ToolTip = note.Title };
            Canvas.SetLeft(item, (bounds.Left - monitor.WorkArea.Left) * ratio); Canvas.SetTop(item, (bounds.Top - monitor.WorkArea.Top) * ratio);
            preview.Children.Add(item);
        }
        previewStatus.Text = (sample ? "예시 메모 · " : $"보이는 메모 {notes.Length}개 · ")
            + (result.SkippedTiles > 0 ? $"일부 타일을 배치하지 못했습니다 ({result.SkippedTiles}개)." : "실제 메모는 이동하지 않습니다.");
    }
    private void BuildRecorders()
    {
        hotkeyRows.Children.Clear(); recorders.Clear();
        foreach (var (section, ids) in new[] { ("메모", new[] { "NewNote", "Search", "ToggleCurrent" }), ("전체 조작", new[] { "ToggleVisibility", "ToggleCollapsed" }), ("정돈", new[] { "Arrange", "ArrangeExpanded" }), ("패널", new[] { "ToggleOverlay" }) })
        {
          hotkeyRows.Children.Add(Section(section));
          foreach (var id in ids)
          {
            var label = HotkeyDefaults.Labels[id];
            var row = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
            var title = new TextBlock { Text = label, Width = 190, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(title, Dock.Left); row.Children.Add(title);
            row.SizeChanged += (_, _) => title.Width = Math.Clamp(row.ActualWidth * .4, 85, 190);
            var box = new TextBox { IsReadOnly = true, VerticalContentAlignment = VerticalAlignment.Center, Text = draft.Hotkeys.GetValueOrDefault(id, ""), Padding = new Thickness(3), ToolTip = "선택한 뒤 키 조합을 누르세요. Esc는 입력 취소" };
            System.Windows.Automation.AutomationProperties.SetName(box, label + " 단축키");
            var clear = Button("해제", () => SetGesture(id, "")); DockPanel.SetDock(clear, Dock.Right); row.Children.Add(clear);
            row.Children.Add(box); recorders[id] = box; hotkeyRows.Children.Add(row);
            box.GotKeyboardFocus += (_, _) => { captureId = id; captureOriginal = draft.Hotkeys.GetValueOrDefault(id, ""); capturing = true; CaptureChanged?.Invoke(true); };
            box.LostKeyboardFocus += (_, _) => EndCapture(false);
            box.PreviewKeyDown += (_, e) =>
            {
                e.Handled = true; var key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key == Key.Escape) { CancelCapture(); return; }
                if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
                var modifiers = Keyboard.Modifiers;
                var value = (modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl+" : "") + (modifiers.HasFlag(ModifierKeys.Alt) ? "Alt+" : "") + (modifiers.HasFlag(ModifierKeys.Shift) ? "Shift+" : "") + (modifiers.HasFlag(ModifierKeys.Windows) ? "Win+" : "") + key;
                try { SetGesture(id, HotkeyService.Normalize(value)); } catch (Exception ex) { ShowError(ex.Message); }
            };
          }
        }
        GroupSections(hotkeyRows);
    }
    private void SetGesture(string id, string value) { draft.Hotkeys[id] = value; recorders[id].Text = value; }
    private void ResetAll() { EndCapture(); draft = draft with { Hotkeys = HotkeyDefaults.Create() }; BuildRecorders(); }
    private void EndCapture(bool clearFocus = true)
    {
        if (!capturing) return; capturing = false;
        captureId = null;
        if (clearFocus) Keyboard.ClearFocus(); CaptureChanged?.Invoke(false);
    }
    private void CancelCapture()
    {
        if (captureId is string id) SetGesture(id, captureOriginal);
        EndCapture();
    }
    private void Dispatch()
    {
        EndCapture(); if (!StoreMonitor()) return;
        try
        {
            HotkeyService.Validate(draft.Hotkeys);
            if (!double.TryParse(newNoteWidth.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
                || !double.TryParse(newNoteHeight.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var height))
                throw new InvalidDataException("새 메모 가로·세로에 숫자를 입력해 주세요.");
            var candidate = draft with { NewNoteWidth = width, NewNoteHeight = height, AutoArrange = false, SelectedMonitor = selectedId, PanelLeft = Left, PanelTop = Top, Monitors = new(draft.Monitors), Hotkeys = new(draft.Hotkeys) };
            candidate.Validate();
            ApplyRequested?.Invoke(candidate, autoStart.IsChecked == true);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    private void ResetNewNoteAnchor() { newNoteX.Text = ""; newNoteY.Text = ""; }
    private void ResetNewNoteSize() { newNoteWidth.Text = "320"; newNoteHeight.Text = "300"; }
    private void PickAnchor() => PickAnchor(false);
    private void PickAnchor(bool forNewNote)
    {
        if (monitorBox.SelectedItem is not MonitorDescriptor monitor) return;
        EndCapture(); Hide(); CaptureChanged?.Invoke(true);
        try
        {
            var picker = new AnchorPickerWindow(monitor);
            if (picker.ShowDialog() == true && picker.SelectedPoint is Point point)
            {
                (forNewNote ? newNoteX : x).Text = point.X.ToString("0.##", CultureInfo.InvariantCulture);
                (forNewNote ? newNoteY : y).Text = point.Y.ToString("0.##", CultureInfo.InvariantCulture);
            }
        }
        finally { CaptureChanged?.Invoke(false); Show(); Activate(); }
    }
    private void HidePanel() { EndCapture(); Hide(); Hidden?.Invoke(); }
    public void ShowError(string message) { status.Foreground = Brushes.Firebrick; status.Text = message; }
    public void ShowSuccess(string message = "설정을 저장했습니다.") { status.Foreground = Brushes.DarkGreen; status.Text = message; }
    private sealed class UniformGridShim : System.Windows.Controls.Primitives.UniformGrid { public UniformGridShim() { Columns = 2; } }
}
