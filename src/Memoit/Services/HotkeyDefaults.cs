namespace Memoit.Services;

public static class HotkeyDefaults
{
    public static IReadOnlyDictionary<string, string> Labels { get; } = new Dictionary<string, string>
    {
        ["ToggleVisibility"] = "전체 메모 숨기기 / 보이기", ["HideAll"] = "전체 숨기기", ["ShowAll"] = "전체 보이기",
        ["ToggleCollapsed"] = "전체 접기 / 펼치기", ["CollapseAll"] = "전체 접기", ["ExpandAll"] = "전체 펼치기",
        ["TogglePanel"] = "메모 패널 열기 / 닫기", ["Arrange"] = "접힌 메모 정렬", ["SortCreated"] = "생성순 정렬",
        ["SortColor"] = "색상순 정렬", ["SortTitle"] = "제목순 정렬", ["ShapeGrid"] = "격자 배치",
        ["ShapeHorizontal"] = "가로 배치", ["ShapeVertical"] = "세로 배치", ["ToggleAuto"] = "자동 정렬 켜기 / 끄기",
        ["ToggleCurrent"] = "현재 메모 접기 / 펼치기"
    };

    public static Dictionary<string, string> Create()
    {
        var values = Labels.Keys.ToDictionary(key => key, _ => "", StringComparer.Ordinal);
        foreach (var pair in new Dictionary<string, string> { ["ToggleVisibility"] = "H", ["ToggleCollapsed"] = "C",
            ["TogglePanel"] = "O", ["Arrange"] = "R", ["SortCreated"] = "1", ["SortColor"] = "2", ["SortTitle"] = "3", ["ToggleAuto"] = "A" })
            values[pair.Key] = "Ctrl+Alt+Shift+" + pair.Value;
        values["ToggleCurrent"] = "Ctrl+Shift+Space";
        return values;
    }
}
