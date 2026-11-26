namespace ClaudeRing.Core;

using System.Text.Json;

public sealed class SessionInfoReader(string projectsDir)
{
    private sealed class Entry
    {
        public string Path = "";
        public long Offset;
        public SessionInfo Info = new(null, null, null, null);
    }

    private readonly Dictionary<string, Entry> _cache = [];
    private const int ChunkSize = 1024 * 1024;

    private readonly object _lock = new();

    public SessionInfo Read(string sessionId)
    {
        lock (_lock)
        {
            if (!_cache.TryGetValue(sessionId, out var entry) || !File.Exists(entry.Path))
            {
                _cache.Remove(sessionId);
                var file = FindFile(sessionId);
                if (file == null) return new(null, null, null, null);
                entry = new Entry { Path = file };
                _cache[sessionId] = entry;
            }

            try { ReadNew(entry); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return entry.Info;
        }
    }

    private string? FindFile(string sessionId)
    {
        try
        {
            if (!Directory.Exists(projectsDir)) return null;
            var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };
            return Directory.EnumerateFiles(projectsDir, sessionId + ".jsonl", options).FirstOrDefault();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void ReadNew(Entry entry)
    {
        // Buffer size 1 turns off FileStream buffering: we read big chunks ourselves.
        using var stream = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1);
        var info = entry.Info;
        var offset = entry.Offset;
        if (stream.Length < offset)
        {
            // File was replaced or truncated: start again.
            offset = 0;
            info = new(null, null, null, null);
            entry.Offset = 0;
            entry.Info = info;
        }
        if (stream.Length == offset) return;

        stream.Seek(offset, SeekOrigin.Begin);
        var (custom, ai, branch, cwd) = info;
        var buffer = new byte[ChunkSize];
        // Bytes of a line that continues in the next chunk.
        var carry = new LineBuffer();
        // gitBranch and cwd are in almost every line, so only the last such line is parsed, after the loop.
        var branchLine = new LineBuffer();
        var cwdLine = new LineBuffer();
        long consumed = 0;
        int count;
        while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            var start = 0;
            int nl;
            while ((nl = Array.IndexOf(buffer, (byte)'\n', start, count - start)) >= 0)
            {
                if (carry.Length > 0)
                {
                    carry.Append(buffer, start, nl - start);
                    ApplyLine(carry.Bytes, 0, carry.Length, ref custom, ref ai, branchLine, cwdLine);
                    consumed += carry.Length + 1;
                    carry.Clear();
                }
                else
                {
                    ApplyLine(buffer, start, nl - start, ref custom, ref ai, branchLine, cwdLine);
                    consumed += (long)(nl - start) + 1;
                }
                start = nl + 1;
            }
            // Bytes after the last "\n" belong to a line that is not finished yet.
            carry.Append(buffer, start, count - start);
        }

        branch = ParseString(branchLine, "gitBranch") ?? branch;
        cwd = ParseString(cwdLine, "cwd") ?? cwd;
        entry.Info = new(custom, ai, branch, cwd);
        entry.Offset = offset + consumed;
    }

    private static void ApplyLine(byte[] bytes, int start, int length, ref string? custom, ref string? ai, LineBuffer branchLine, LineBuffer cwdLine)
    {
        if (length > 0 && bytes[start + length - 1] == (byte)'\r') length--;
        if (length == 0 || bytes[start] != (byte)'{') return;
        var line = new ReadOnlySpan<byte>(bytes, start, length);

        if (line.IndexOf("\"customTitle\""u8) >= 0 || line.IndexOf("\"aiTitle\""u8) >= 0)
        {
            try
            {
                using var doc = JsonDocument.Parse(new ReadOnlyMemory<byte>(bytes, start, length));
                custom = Str(doc.RootElement, "customTitle") ?? custom;
                ai = Str(doc.RootElement, "aiTitle") ?? ai;
            }
            catch (JsonException) { }
        }
        if (line.IndexOf("\"gitBranch\""u8) >= 0) branchLine.Set(line);
        if (line.IndexOf("\"cwd\""u8) >= 0) cwdLine.Set(line);
    }

    private static string? ParseString(LineBuffer line, string name)
    {
        if (line.Length == 0) return null;
        try
        {
            using var doc = JsonDocument.Parse(new ReadOnlyMemory<byte>(line.Bytes, 0, line.Length));
            return Str(doc.RootElement, name);
        }
        catch (JsonException) { return null; }
    }

    // Reusable byte storage, so remembering a line does not allocate each time.
    private sealed class LineBuffer
    {
        public byte[] Bytes = [];
        public int Length;

        public void Clear() => Length = 0;

        public void Set(ReadOnlySpan<byte> data)
        {
            Length = 0;
            Append(data);
        }

        public void Append(byte[] data, int start, int count) => Append(new ReadOnlySpan<byte>(data, start, count));

        private void Append(ReadOnlySpan<byte> data)
        {
            if (Length + data.Length > Bytes.Length)
                Array.Resize(ref Bytes, Math.Max(Length + data.Length, Bytes.Length * 2));
            data.CopyTo(Bytes.AsSpan(Length));
            Length += data.Length;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
}
