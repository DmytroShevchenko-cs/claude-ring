namespace ClaudeRing.Core;

using System.Text.RegularExpressions;

public static class SlotText
{
    private static readonly Regex TaskKey = new(@"^([A-Z][A-Z0-9]+)-(\d+)$");

    // A task key is shown in two parts: project on top, number big below.
    public static (string? Top, string Main) Format(string label)
    {
        var m = TaskKey.Match(label);
        return m.Success ? (m.Groups[1].Value, m.Groups[2].Value) : (null, label);
    }
}
