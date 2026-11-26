namespace ClaudeRing.Core;

using System.Text.RegularExpressions;

// Templates for custom menu items, e.g. "https://sentry.io/issues/?query={key}".
public static partial class Template
{
    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        "key", "branch", "title", "sessionId", "repos.parent", "repo.name", "repo.path", "repo.webUrl", "repo.branch",
    };

    // Values that are paths: "/" stays as is in URLs.
    private static readonly HashSet<string> PathValues = ["branch", "repo.branch", "repo.path", "repos.parent"];

    // Values that are URLs already.
    private static readonly HashSet<string> UrlValues = ["repo.webUrl"];

    [GeneratedRegex(@"\{([A-Za-z][A-Za-z.]*)\}")]
    private static partial Regex Placeholder();

    public static IReadOnlyList<string> Placeholders(string template) =>
        Placeholder().Matches(template).Select(m => m.Groups[1].Value).Distinct().ToList();

    public static IReadOnlyList<string> Unknown(string template) =>
        Placeholders(template).Where(p => !Known.Contains(p)).ToList();

    // Null when a placeholder has no value. With forUrl, values are escaped for a URL.
    public static string? Render(string template, IReadOnlyDictionary<string, string?> values, bool forUrl)
    {
        var missing = false;
        var result = Placeholder().Replace(template, m =>
        {
            var name = m.Groups[1].Value;
            if (!values.TryGetValue(name, out var value) || string.IsNullOrEmpty(value))
            {
                missing = true;
                return "";
            }
            return forUrl ? Escape(name, value) : value;
        });
        return missing ? null : result;
    }

    public static string EscapePath(string value) => string.Join('/', value.Split('/').Select(Uri.EscapeDataString));

    private static string Escape(string name, string value) =>
        UrlValues.Contains(name) ? value
        : PathValues.Contains(name) ? EscapePath(value)
        : Uri.EscapeDataString(value);
}
