namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class IdeLauncherTests
{
    [Theory]
    [InlineData(IdeType.Rider, "/Applications/Rider.app/Contents/MacOS/rider")]
    [InlineData(IdeType.Idea, "/Applications/IntelliJ IDEA.app/Contents/MacOS/idea")]
    [InlineData(IdeType.WebStorm, "/Applications/WebStorm.app/Contents/MacOS/webstorm")]
    [InlineData(IdeType.PyCharm, "/Applications/PyCharm.app/Contents/MacOS/pycharm")]
    [InlineData(IdeType.GoLand, "/Applications/GoLand.app/Contents/MacOS/goland")]
    [InlineData(IdeType.CLion, "/Applications/CLion.app/Contents/MacOS/clion")]
    [InlineData(IdeType.RubyMine, "/Applications/RubyMine.app/Contents/MacOS/rubymine")]
    [InlineData(IdeType.PhpStorm, "/Applications/PhpStorm.app/Contents/MacOS/phpstorm")]
    public void Jetbrains_opens_all_paths_in_one_call(IdeType type, string binary)
    {
        var command = Assert.Single(IdeLauncher.Commands(new IdeSettings(type), ["/p/a", "/p/b"]));
        Assert.Equal(binary, command.File);
        Assert.Equal(["/p/a", "/p/b"], command.Args);
    }

    [Fact]
    public void App_setting_replaces_the_default_app()
    {
        var command = Assert.Single(IdeLauncher.Commands(new IdeSettings(IdeType.Rider, "/Users/me/Applications/Rider.app/"), ["/p/a"]));
        Assert.Equal("/Users/me/Applications/Rider.app/Contents/MacOS/rider", command.File);
    }

    [Theory]
    [InlineData(IdeType.VsCode, "/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code")]
    [InlineData(IdeType.Cursor, "/Applications/Cursor.app/Contents/Resources/app/bin/cursor")]
    [InlineData(IdeType.Windsurf, "/Applications/Windsurf.app/Contents/Resources/app/bin/windsurf")]
    public void Vs_code_family_opens_one_window_per_path(IdeType type, string cli)
    {
        var commands = IdeLauncher.Commands(new IdeSettings(type), ["/p/a", "/p/b"]);
        Assert.Equal(2, commands.Count);
        Assert.All(commands, c => Assert.Equal(cli, c.File));
        Assert.Equal(["-n", "/p/a"], commands[0].Args);
        Assert.Equal(["-n", "/p/b"], commands[1].Args);
    }

    [Fact]
    public void Custom_ide_uses_open_once_per_path()
    {
        var commands = IdeLauncher.Commands(new IdeSettings(IdeType.Custom, "/Applications/Zed.app"), ["/p/a", "/p/b"]);
        Assert.Equal(2, commands.Count);
        Assert.Equal("open", commands[0].File);
        Assert.Equal(["-a", "/Applications/Zed.app", "/p/a"], commands[0].Args);
        Assert.Equal(["-a", "/Applications/Zed.app", "/p/b"], commands[1].Args);
    }

    [Fact]
    public void No_ide_or_no_paths_gives_no_commands()
    {
        Assert.Empty(IdeLauncher.Commands(new IdeSettings(IdeType.None), ["/p/a"]));
        Assert.Empty(IdeLauncher.Commands(new IdeSettings(IdeType.Rider), []));
    }

    [Theory]
    [InlineData(IdeType.Rider, "Rider")]
    [InlineData(IdeType.Idea, "IDEA")]
    [InlineData(IdeType.VsCode, "VS Code")]
    [InlineData(IdeType.Cursor, "Cursor")]
    public void Display_name(IdeType type, string name) =>
        Assert.Equal(name, IdeLauncher.DisplayName(new IdeSettings(type)));

    [Fact]
    public void Custom_display_name_is_the_app_name() =>
        Assert.Equal("Zed", IdeLauncher.DisplayName(new IdeSettings(IdeType.Custom, "/Applications/Zed.app")));
}
