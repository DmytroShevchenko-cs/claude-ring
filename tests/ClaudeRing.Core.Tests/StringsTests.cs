namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class StringsTests
{
    [Fact]
    public void English_labels()
    {
        var s = Strings.En;
        Assert.Equal("VS Code", s.Chat);
        Assert.Equal("Repos", s.Repos);
        Assert.Equal("All", s.All);
        Assert.Equal("All in browser", s.AllInBrowser);
        Assert.Equal("Folder in Finder", s.FolderInFinder);
        Assert.Equal("more ›", s.More);
        Assert.Equal("← Back", s.Back);
        Assert.Equal("All MRs", s.AllReviews("MR"));
        Assert.Equal("All PRs", s.AllReviews("PR"));
        Assert.Equal("→ main", s.Target("main"));
    }

    [Fact]
    public void Russian_labels()
    {
        var s = Strings.Ru;
        Assert.Equal("VS Code", s.Chat);
        Assert.Equal("Репо", s.Repos);
        Assert.Equal("Все", s.All);
        Assert.Equal("Все в браузере", s.AllInBrowser);
        Assert.Equal("Папка в Finder", s.FolderInFinder);
        Assert.Equal("ещё ›", s.More);
        Assert.Equal("← назад", s.Back);
        Assert.Equal("Все MR", s.AllReviews("MR"));
        Assert.Equal("Все PR", s.AllReviews("PR"));
        Assert.Equal("→ Dev", s.Target("Dev"));
    }

    [Theory]
    [InlineData(UiLanguage.En, "ru-RU", "en")]
    [InlineData(UiLanguage.Ru, "en-US", "ru")]
    [InlineData(UiLanguage.Auto, "ru-RU", "ru")]
    [InlineData(UiLanguage.Auto, "uk-UA", "en")]
    [InlineData(UiLanguage.Auto, null, "en")]
    public void Picks_the_language(UiLanguage language, string? system, string expected) =>
        Assert.Same(expected == "ru" ? Strings.Ru : Strings.En, Strings.For(language, () => system));

    [Fact]
    public void Explicit_language_does_not_read_the_system() =>
        Assert.Same(Strings.En, Strings.For(UiLanguage.En, () => throw new InvalidOperationException("must not be called")));

    [Fact]
    public void Reads_the_first_quoted_system_language()
    {
        var runner = new FakeProcessRunner();
        string[]? call = null;
        runner.OnRun = (file, args) =>
        {
            call = [file, .. args];
            return "(\n    \"ru-RU\",\n    \"en-US\"\n)\n";
        };
        Assert.Equal("ru-RU", SystemLanguage.Read(runner));
        Assert.Equal(["/usr/bin/defaults", "read", "-g", "AppleLanguages"], call!);
    }

    [Fact]
    public void Reads_an_unquoted_system_language()
    {
        var runner = new FakeProcessRunner { OnRun = (_, _) => "(\n    en,\n    ru\n)\n" };
        Assert.Equal("en", SystemLanguage.Read(runner));
    }

    [Fact]
    public void System_language_is_null_when_defaults_fails()
    {
        var runner = new FakeProcessRunner { OnRun = (_, _) => throw new InvalidOperationException("exited with 1") };
        Assert.Null(SystemLanguage.Read(runner));
    }
}
