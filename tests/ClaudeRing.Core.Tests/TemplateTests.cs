namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class TemplateTests
{
    private static readonly Dictionary<string, string?> Values = new()
    {
        ["key"] = "A-1",
        ["branch"] = "feature/A-1 fix",
        ["title"] = "Fix & ship",
        ["repo.webUrl"] = "https://g/T/api",
        ["repo.path"] = "/p/A-1/api",
        ["sessionId"] = null,
    };

    [Fact]
    public void Lists_placeholders_once_in_order() =>
        Assert.Equal(["key", "branch"], Template.Placeholders("{key}/{branch}/{key}").ToArray());

    [Fact]
    public void Finds_unknown_placeholders() =>
        Assert.Equal(["ticket"], Template.Unknown("{key}-{ticket}").ToArray());

    [Fact]
    public void Json_braces_are_not_placeholders() =>
        Assert.Empty(Template.Placeholders("""{"a": 1}"""));

    [Fact]
    public void Text_without_placeholders_stays() =>
        Assert.Equal("https://x/?a=1", Template.Render("https://x/?a=1", Values, forUrl: true));

    [Fact]
    public void Url_escapes_values_and_keeps_slashes_in_branch() =>
        Assert.Equal("https://x/?q=Fix%20%26%20ship&b=feature/A-1%20fix",
            Template.Render("https://x/?q={title}&b={branch}", Values, forUrl: true));

    [Fact]
    public void Url_keeps_web_url_as_is() =>
        Assert.Equal("https://g/T/api/pipelines", Template.Render("{repo.webUrl}/pipelines", Values, forUrl: true));

    [Fact]
    public void Plain_does_not_escape() =>
        Assert.Equal("Fix & ship /p/A-1/api", Template.Render("{title} {repo.path}", Values, forUrl: false));

    [Fact]
    public void Missing_or_null_value_gives_null()
    {
        Assert.Null(Template.Render("{repo.name}", Values, forUrl: true));
        Assert.Null(Template.Render("{sessionId}", Values, forUrl: false));
    }

    [Fact]
    public void Empty_value_gives_null() =>
        Assert.Null(Template.Render("{key}", new Dictionary<string, string?> { ["key"] = "" }, forUrl: false));

    [Fact]
    public void Escape_path_keeps_slashes() =>
        Assert.Equal("feature/A-1%23x", Template.EscapePath("feature/A-1#x"));
}
