namespace ClaudeRing.Core;

using System.Text.RegularExpressions;

public sealed class RepoFinder(IEnumerable<string> roots)
{
    public IReadOnlyList<RepoRef> Find(string key)
    {
        var branchMatch = new Regex($@"(^|[/_-]){Regex.Escape(key)}($|[/_-])", RegexOptions.IgnoreCase);
        var result = new List<RepoRef>();
        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var dir in Candidates(root))
            {
                string? branch;
                string? webUrl;
                try
                {
                    branch = ReadBranch(dir);
                    webUrl = branch != null && branchMatch.IsMatch(branch) ? ReadWebUrl(dir) : null;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
                if (branch != null && branchMatch.IsMatch(branch)) result.Add(new RepoRef(Path.GetFileName(dir), dir, branch, webUrl));
            }
        }
        return result.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static readonly EnumerationOptions Flat = new() { IgnoreInaccessible = true, RecurseSubdirectories = false };

    // Repos at depth 1 (<repo>-<KEY>) and depth 2 (<KEY>/<repo>).
    private static IEnumerable<string> Candidates(string root)
    {
        foreach (var d1 in Directory.EnumerateDirectories(root, "*", Flat))
        {
            if (Path.GetFileName(d1).StartsWith('_') || Path.GetFileName(d1).StartsWith('.')) continue;
            if (IsRepo(d1)) { yield return d1; continue; }
            foreach (var d2 in Directory.EnumerateDirectories(d1, "*", Flat).Where(IsRepo)) yield return d2;
        }
    }

    private static bool IsRepo(string dir) => Path.Exists(Path.Combine(dir, ".git"));

    private static string? ReadBranch(string repo)
    {
        var git = Path.Combine(repo, ".git");
        var gitDir = Directory.Exists(git) ? git : ParseGitDir(git, repo);
        var head = gitDir == null ? null : Path.Combine(gitDir, "HEAD");
        if (head == null || !File.Exists(head)) return null;
        var text = File.ReadAllText(head).Trim();
        return text.StartsWith("ref: refs/heads/", StringComparison.Ordinal) ? text["ref: refs/heads/".Length..] : null;
    }

    private static string? ParseGitDir(string gitFile, string repo)
    {
        var line = File.ReadAllText(gitFile).Trim();
        if (!line.StartsWith("gitdir: ", StringComparison.Ordinal)) return null;
        var path = line["gitdir: ".Length..];
        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(repo, path));
    }

    // Web URL of the "origin" remote, or null when it cannot be read.
    private static string? ReadWebUrl(string repo)
    {
        try
        {
            var git = Path.Combine(repo, ".git");
            string configDir;
            if (Directory.Exists(git)) configDir = git;
            else
            {
                var gitDir = ParseGitDir(git, repo);
                if (gitDir == null) return null;
                var commonFile = Path.Combine(gitDir, "commondir");
                configDir = File.Exists(commonFile)
                    ? Path.GetFullPath(Path.Combine(gitDir, File.ReadAllText(commonFile).Trim()))
                    : gitDir;
            }
            var config = Path.Combine(configDir, "config");
            return File.Exists(config) ? ToWebUrl(OriginUrl(File.ReadAllLines(config))) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? OriginUrl(IEnumerable<string> lines)
    {
        var inOrigin = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inOrigin = Regex.IsMatch(line, @"^\[remote\s+""origin""\]$");
                continue;
            }
            if (!inOrigin) continue;
            var eq = line.IndexOf('=');
            if (eq > 0 && line[..eq].Trim().Equals("url", StringComparison.OrdinalIgnoreCase)) return line[(eq + 1)..].Trim();
        }
        return null;
    }

    private static string? ToWebUrl(string? remote)
    {
        if (string.IsNullOrWhiteSpace(remote)) return null;
        string host, path;
        var scp = Regex.Match(remote, @"^(?:[^@/]+@)?([^:/]+):(?!//)(.+)$");
        if (remote.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(remote, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host)) return null;
            host = uri.Host;
            path = uri.AbsolutePath;
        }
        else if (scp.Success)
        {
            host = scp.Groups[1].Value;
            path = scp.Groups[2].Value;
        }
        else return null;

        path = path.Trim('/');
        if (path.EndsWith(".git", StringComparison.Ordinal)) path = path[..^4];
        path = path.TrimEnd('/');
        return path.Length == 0 ? null : $"https://{host}/{path}";
    }
}
