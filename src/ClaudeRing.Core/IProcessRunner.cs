namespace ClaudeRing.Core;

using System.Diagnostics;

public interface IProcessRunner
{
    // Runs a process and returns its stdout. Throws on non-zero exit code.
    string Run(string file, params string[] args);

    // Starts a process and does not wait for it.
    void Start(string file, params string[] args);
}

public sealed class ProcessRunner(TimeSpan? timeout = null) : IProcessRunner
{
    private static readonly TimeSpan DefaultRunTimeout = TimeSpan.FromSeconds(5);

    private readonly TimeSpan _runTimeout = timeout ?? DefaultRunTimeout;

    public string Run(string file, params string[] args)
    {
        using var p = Process.Start(Info(file, args))
            ?? throw new InvalidOperationException($"Could not start {file}");
        // Read both pipes at the same time so a full pipe cannot block the child.
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(_runTimeout))
        {
            p.Kill(entireProcessTree: true);
            throw new TimeoutException($"{file} did not exit within {_runTimeout.TotalSeconds:0.#} s");
        }
        // The parameterless overload waits until the pipes are fully read.
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"{file} exited with {p.ExitCode}: {stderr.Result}");
        return stdout.Result;
    }

    public void Start(string file, params string[] args) => Process.Start(Info(file, args))?.Dispose();

    private static ProcessStartInfo Info(string file, string[] args)
    {
        var info = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) info.ArgumentList.Add(a);
        return info;
    }
}
