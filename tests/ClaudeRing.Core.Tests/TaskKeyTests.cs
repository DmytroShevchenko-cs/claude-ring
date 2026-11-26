namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class TaskKeyTests
{
    [Theory]
    [InlineData("PROJ-1173 эмодзи из п…", "PROJ-1173")]
    [InlineData("feature/TEAM-1210", "TEAM-1210")]
    [InlineData("Потерянный клик в меню…", null)]
    public void Finds_first_key(string text, string? expected) => Assert.Equal(expected, TaskKey.Find(text));

    [Fact]
    public void Checks_sources_in_order() => Assert.Equal("ABC-2", TaskKey.Find(null, "no key", "ABC-2 then ABC-3"));

    [Theory]
    [InlineData("feature/PROJ-1173_fix", "PROJ-1173")]
    [InlineData("PROJ-1173эмодзи", "PROJ-1173")]
    [InlineData("abcDEF-12", null)]
    public void Respects_boundaries(string text, string? expected) => Assert.Equal(expected, TaskKey.Find(text));

    [Fact]
    public void Null_array_gives_null() => Assert.Null(TaskKey.Find(null));
}
