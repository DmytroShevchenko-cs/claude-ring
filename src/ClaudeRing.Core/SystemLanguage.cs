namespace ClaudeRing.Core;

using System.Text.RegularExpressions;

public static partial class SystemLanguage
{
    // One entry of the "defaults" list output: "ru-RU", or en without quotes.
    [GeneratedRegex(@"^\s*""?([A-Za-z]{2,3}(?:[-_][A-Za-z0-9]+)*)""?,?\s*$", RegexOptions.Multiline)]
    private static partial Regex Entry();

    // First macOS UI language, e.g. "ru-RU". Null when it cannot be read.
    public static string? Read(IProcessRunner runner)
    {
        try
        {
            var match = Entry().Match(runner.Run("/usr/bin/defaults", "read", "-g", "AppleLanguages"));
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (Exception e) when (e is InvalidOperationException or TimeoutException or IOException
                                      or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
