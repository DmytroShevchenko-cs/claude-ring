namespace ClaudeRing.Core;

using System.Text.RegularExpressions;

public static partial class TaskKey
{
    [GeneratedRegex(@"(?<![A-Za-z0-9])([A-Z][A-Z0-9]+-\d+)(?!\d)")]
    private static partial Regex Pattern();

    public static string? Find(params string?[] sources) =>
        (sources ?? []).Where(s => s != null).Select(s => Pattern().Match(s!)).FirstOrDefault(m => m.Success)?.Groups[1].Value;
}
