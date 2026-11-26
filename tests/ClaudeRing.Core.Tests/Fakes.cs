namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public sealed class FakeProcessRunner : IProcessRunner
{
    public List<string[]> Started { get; } = [];
    public Func<string, string[], string> OnRun { get; set; } = (_, _) => "";

    public string Run(string file, params string[] args) => OnRun(file, args);
    public void Start(string file, params string[] args) => Started.Add([file, .. args]);
}

public sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("claude-ring-").FullName;

    public string Write(string relative, string content)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
