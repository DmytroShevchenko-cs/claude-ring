namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class TaskKeyTests
{
    private static readonly TaskKey Default = new(Settings.DefaultTaskKeyPattern);

    [Theory]
    [InlineData("PROJ-1173 эмодзи из п…", "PROJ-1173")]
    [InlineData("feature/TEAM-1210", "TEAM-1210")]
    [InlineData("Потерянный клик в меню…", null)]
    public void Finds_first_key(string text, string? expected) => Assert.Equal(expected, Default.Find(text));

    [Fact]
    public void Checks_sources_in_order() => Assert.Equal("ABC-2", Default.Find(null, "no key", "ABC-2 then ABC-3"));

    [Theory]
    [InlineData("feature/PROJ-1173_fix", "PROJ-1173")]
    [InlineData("PROJ-1173эмодзи", "PROJ-1173")]
    [InlineData("abcDEF-12", null)]
    public void Respects_boundaries(string text, string? expected) => Assert.Equal(expected, Default.Find(text));

    [Fact]
    public void Null_array_gives_null() => Assert.Null(Default.Find(null));

    [Fact]
    public void Capture_group_gives_the_key() => Assert.Equal("123", new TaskKey(@"#(\d+)").Find("Fix #123 now"));

    [Fact]
    public void Branch_number_pattern() => Assert.Equal("123", new TaskKey(@"(?:^|/)(\d+)-").Find(null, "feature/123-fix"));

    [Fact]
    public void Whole_match_without_a_group() => Assert.Equal("ab-12", new TaskKey(@"[a-z]+-\d+").Find("x ab-12"));

    [Fact]
    public void Non_capturing_group_is_not_the_key_group() =>
        Assert.Equal("PROJ-7", new TaskKey(@"(?:PROJ|OPS)-\d+").Find("PROJ-7"));

    [Fact]
    public void Empty_group_moves_to_the_next_source() => Assert.Equal("5", new TaskKey(@"#(\d*)").Find("# none", "#5"));
}
