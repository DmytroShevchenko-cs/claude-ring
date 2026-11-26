namespace ClaudeRing.Core.Tests;

using System.Diagnostics;
using ClaudeRing.Core;
using Xunit;

public class ProcessRunnerTests
{
    [Fact]
    public void Run_returns_stdout()
    {
        Assert.Equal("hello\n", new ProcessRunner().Run("/bin/echo", "hello"));
    }

    [Fact]
    public void Run_throws_on_non_zero_exit()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ProcessRunner().Run("/bin/sh", "-c", "echo oops >&2; exit 3"));
        Assert.Contains("3", ex.Message);
        Assert.Contains("oops", ex.Message);
    }

    [Fact]
    public void Run_times_out_and_kills()
    {
        var runner = new ProcessRunner(TimeSpan.FromMilliseconds(300));
        var sw = Stopwatch.StartNew();
        Assert.Throws<TimeoutException>(() => runner.Run("/bin/sleep", "30"));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Run_handles_large_stderr()
    {
        var result = new ProcessRunner().Run("/bin/sh", "-c", "head -c 200000 /dev/zero | tr '\\0' x >&2; echo done");
        Assert.Equal("done\n", result);
    }
}
