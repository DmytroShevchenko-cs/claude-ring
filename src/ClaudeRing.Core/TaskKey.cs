namespace ClaudeRing.Core;

using System.Text.RegularExpressions;

// Finds the task key with the pattern from settings.
// With a capture group, the key is the first group; otherwise the whole match.
public sealed class TaskKey
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(200);

    private readonly Regex _pattern;
    private readonly bool _useGroup;

    // Throws ArgumentException for an invalid pattern; SettingsLoader rejects those first.
    public TaskKey(string pattern)
    {
        _pattern = new Regex(pattern, RegexOptions.None, MatchTimeout);
        _useGroup = _pattern.GetGroupNumbers().Length > 1;
    }

    // First key in the first source that has one. Never throws.
    public string? Find(params string?[]? sources)
    {
        foreach (var source in sources ?? [])
        {
            if (source == null) continue;
            Match match;
            try
            {
                match = _pattern.Match(source);
            }
            catch (RegexMatchTimeoutException)
            {
                continue;
            }
            if (!match.Success) continue;
            var key = _useGroup ? match.Groups[1].Value : match.Value;
            if (key.Length > 0) return key;
        }
        return null;
    }
}
