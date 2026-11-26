namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class ConfigTests
{
    [Fact]
    public void Missing_file_gives_defaults()
    {
        var config = RingConfig.Load("/nonexistent/config.json");
        Assert.Equal(8, config.SlotCount);
        Assert.Null(config.GitLabGroup);
        Assert.Equal("https://gitlab.com", config.GitLabBaseUrl);
    }

    [Fact]
    public void File_overrides_values()
    {
        using var dir = new TempDir();
        var path = dir.Write("config.json", """{ "slotCount": 6, "jiraBaseUrl": "https://example.atlassian.net" }""");
        var config = RingConfig.Load(path);
        Assert.Equal(6, config.SlotCount);
        Assert.Equal("https://example.atlassian.net", config.JiraBaseUrl);
    }
}
