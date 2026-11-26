namespace ClaudeRing.Core;

using System.Globalization;

// Slot labels. Product names (VS Code, Rider, MR, Jira) are the same in every language.
public sealed record Strings(
    string Chat,
    string Repos,
    string All,
    string AllInBrowser,
    string FolderInFinder,
    string More,
    string Back,
    string AllReviewsFormat)
{
    public static readonly Strings En = new("VS Code", "Repos", "All", "All in browser", "Folder in Finder", "more ›", "← Back", "All {0}s");

    public static readonly Strings Ru = new("VS Code", "Репо", "Все", "Все в браузере", "Папка в Finder", "ещё ›", "← назад", "Все {0}");

    // "All MRs" / "Все MR".
    public string AllReviews(string reviewsLabel) => string.Format(CultureInfo.InvariantCulture, AllReviewsFormat, reviewsLabel);

    // Reviews into one target branch.
    public string Target(string branch) => "→ " + branch;

    // Auto uses the first macOS UI language: Russian for "ru", English for anything else.
    public static Strings For(UiLanguage language, Func<string?> systemLanguage) => language switch
    {
        UiLanguage.Ru => Ru,
        UiLanguage.En => En,
        _ => systemLanguage()?.StartsWith("ru", StringComparison.OrdinalIgnoreCase) == true ? Ru : En,
    };
}
