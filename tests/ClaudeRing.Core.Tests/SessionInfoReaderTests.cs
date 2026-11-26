namespace ClaudeRing.Core.Tests;

using ClaudeRing.Core;

public class SessionInfoReaderTests
{
    [Fact]
    public void Takes_last_titles_and_branch()
    {
        using var dir = new TempDir();
        dir.Write("-Users-me/abc.jsonl", string.Join('\n',
            """{"type":"user","cwd":"/Users/me","gitBranch":"master","message":{"content":"hi"}}""",
            """{"type":"ai-title","aiTitle":"Старый заголовок"}""",
            """{"type":"ai-title","aiTitle":"Фикс эмодзи"}""",
            """{"type":"custom-title","customTitle":"PROJ-1173"}""",
            """{"type":"assistant","cwd":"/Users/me/x","gitBranch":"feature/PROJ-1173"}""",
            "not json"));

        var info = new SessionInfoReader(dir.Path).Read("abc");

        Assert.Equal("PROJ-1173", info.CustomTitle);
        Assert.Equal("Фикс эмодзи", info.AiTitle);
        Assert.Equal("feature/PROJ-1173", info.GitBranch);
        Assert.Equal("/Users/me/x", info.Cwd);
    }

    [Fact]
    public void Missing_session_gives_empty_info() =>
        Assert.Equal(new SessionInfo(null, null, null, null), new SessionInfoReader("/nonexistent").Read("x"));

    [Fact]
    public void Reads_only_new_lines_after_append()
    {
        using var dir = new TempDir();
        var path = dir.Write("p/s1.jsonl", """{"type":"ai-title","aiTitle":"A"}""" + "\n");
        var reader = new SessionInfoReader(dir.Path);
        Assert.Equal("A", reader.Read("s1").AiTitle);

        File.AppendAllText(path, """{"type":"ai-title","aiTitle":"B"}""" + "\n" + """{"type":"x","gitBranch":"feature/X-1"}""" + "\n");
        var info = reader.Read("s1");

        Assert.Equal("B", info.AiTitle);
        Assert.Equal("feature/X-1", info.GitBranch);
    }

    [Fact]
    public void Partial_last_line_is_read_later()
    {
        using var dir = new TempDir();
        var path = dir.Write("p/s2.jsonl", """{"type":"ai-title","aiTitle":"A"}""" + "\n" + """{"type":"custom-title","custom""");
        var reader = new SessionInfoReader(dir.Path);
        Assert.Null(reader.Read("s2").CustomTitle);

        File.AppendAllText(path, """Title":"KEY-1"}""" + "\n");

        Assert.Equal("KEY-1", reader.Read("s2").CustomTitle);
    }

    [Fact]
    public void Truncated_file_is_read_again()
    {
        using var dir = new TempDir();
        var path = dir.Write("p/s3.jsonl", """{"type":"ai-title","aiTitle":"A long old title"}""" + "\n" + """{"type":"x"}""" + "\n");
        var reader = new SessionInfoReader(dir.Path);
        Assert.Equal("A long old title", reader.Read("s3").AiTitle);

        File.WriteAllText(path, """{"aiTitle":"N"}""" + "\n");

        Assert.Equal("N", reader.Read("s3").AiTitle);
    }

    [Fact]
    public void Cyrillic_then_append_keeps_byte_offsets()
    {
        using var dir = new TempDir();
        var path = dir.Write("p/c1.jsonl", """{"type":"ai-title","aiTitle":"Фикс эмодзи"}""" + "\n");
        var reader = new SessionInfoReader(dir.Path);
        Assert.Equal("Фикс эмодзи", reader.Read("c1").AiTitle);

        File.AppendAllText(path, """{"type":"custom-title","customTitle":"PROJ-1173 эмодзи"}""" + "\n");
        var info = reader.Read("c1");

        Assert.Equal("Фикс эмодзи", info.AiTitle);
        Assert.Equal("PROJ-1173 эмодзи", info.CustomTitle);
    }

    [Fact]
    public void Line_longer_than_chunk_is_read()
    {
        using var dir = new TempDir();
        var pad = new string('x', 2_500_000);
        dir.Write("p/big.jsonl", $$"""{"type":"ai-title","pad":"{{pad}}","aiTitle":"Big"}""" + "\n");

        Assert.Equal("Big", new SessionInfoReader(dir.Path).Read("big").AiTitle);
    }

    [Fact]
    public void Crlf_lines_are_read()
    {
        using var dir = new TempDir();
        dir.Write("p/crlf.jsonl", """{"type":"ai-title","aiTitle":"A"}""" + "\r\n" + """{"type":"x","gitBranch":"feature/X-1"}""" + "\r\n");

        var info = new SessionInfoReader(dir.Path).Read("crlf");

        Assert.Equal("A", info.AiTitle);
        Assert.Equal("feature/X-1", info.GitBranch);
    }

    [Fact]
    public void Second_read_after_long_line_reads_nothing_new()
    {
        using var dir = new TempDir();
        var pad = new string('x', 2_500_000);
        var lineB = """{"type":"ai-title","aiTitle":"B"}""" + "\n";
        var path = dir.Write("p/long.jsonl", $$"""{"type":"ai-title","pad":"{{pad}}","aiTitle":"A"}""" + "\n" + lineB);
        var reader = new SessionInfoReader(dir.Path);
        Assert.Equal("B", reader.Read("long").AiTitle);

        // Same-length "Z" record over the B record: with an exact offset these bytes are never read again.
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
        {
            fs.Seek(-lineB.Length, SeekOrigin.End);
            fs.Write(System.Text.Encoding.UTF8.GetBytes(lineB.Replace("\"B\"", "\"Z\"")));
        }
        Assert.Equal("B", reader.Read("long").AiTitle);

        File.AppendAllText(path, """{"type":"x","gitBranch":"feature/X-2"}""" + "\n");
        var info = reader.Read("long");
        Assert.Equal("feature/X-2", info.GitBranch);
        Assert.Equal("B", info.AiTitle);

        File.AppendAllText(path, """{"type":"ai-title","aiTitle":"C"}""" + "\n");
        Assert.Equal("C", reader.Read("long").AiTitle);
    }
}
