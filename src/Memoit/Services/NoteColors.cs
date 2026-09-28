namespace Memoit.Services;

public static class NoteColors
{
    public static readonly string[] Values = ["#FFF2B3", "#FFDDE7", "#DEF0D8", "#DCEBFA", "#EAE0F7", "#FAFAF5"];
    public static bool Matches(string actual, string? filter) => filter is null || string.Equals(Normalize(actual), Normalize(filter), StringComparison.OrdinalIgnoreCase);
    private static string Normalize(string value) => string.Equals(value, "#FFF2B2", StringComparison.OrdinalIgnoreCase) ? "#FFF2B3" : value;
}
