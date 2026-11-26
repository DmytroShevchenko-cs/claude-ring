namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class ExampleConfigTests
{
    // The repo root is the first parent folder with the solution file.
    private static string RepoFile(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ClaudeRing.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, name);
    }

    [Fact]
    public void Example_config_loads_without_errors_or_warnings()
    {
        var r = SettingsLoader.Load(RepoFile("config.example.json"));
        Assert.Empty(r.Errors);
        Assert.Empty(r.Warnings);
        Assert.Equal(7, r.Settings!.ChatMenu.Count);
    }
}
