namespace Memoit.Services;

public static class HotkeyDefaults
{
    public static IReadOnlyDictionary<string, string> Labels { get; } = new Dictionary<string, string>
    {
        ["NewNote"] = "새 메모", ["ShowList"] = "메모 목록", ["Search"] = "메모 검색",
        ["ToggleOverlay"] = "미니 패널 열기 / 닫기",
        ["ToggleVisibility"] = "전체 메모 숨기기 / 보이기", ["HideAll"] = "전체 숨기기", ["ShowAll"] = "전체 보이기",
        ["ToggleCollapsed"] = "전체 접기 / 펼치기", ["CollapseAll"] = "전체 접기", ["ExpandAll"] = "전체 펼치기",
        ["TogglePanel"] = "설정 열기 / 닫기", ["Arrange"] = "모두 정돈", ["SortCreated"] = "생성순 정돈",
        ["SortColor"] = "색상순 정렬", ["SortTitle"] = "제목순 정렬", ["ShapeGrid"] = "격자 배치",
        ["ShapeHorizontal"] = "가로 배치", ["ShapeVertical"] = "세로 배치",
        ["ArrangeCollapsed"] = "접힌 메모 정돈", ["ArrangeExpanded"] = "펼친 메모 정돈", ["UndoArrange"] = "이전 배치로",
        ["ToggleCurrent"] = "현재 메모 접기 / 펼치기"
    };

    public static Dictionary<string, string> Create()
    {
        var values = Labels.Keys.ToDictionary(key => key, _ => "", StringComparer.Ordinal);
        foreach (var pair in new Dictionary<string, string> { ["ToggleVisibility"] = "H", ["ToggleCollapsed"] = "C",
            ["TogglePanel"] = "O", ["Arrange"] = "R", ["SortCreated"] = "1", ["SortColor"] = "2", ["SortTitle"] = "3",
            ["NewNote"] = "N", ["ShowList"] = "L", ["Search"] = "F", ["ToggleOverlay"] = "P" })
            values[pair.Key] = "Ctrl+Alt+Shift+" + pair.Value;
        values["ToggleCurrent"] = "Ctrl+Shift+Space";
        return values;
    }
}
