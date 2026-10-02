namespace Ieummae.Core.Git;

// 줄 하나의 마지막 수정 커밋
public sealed record BlameLine(int Number, string Hash, string Author, DateTimeOffset Time, string Summary, string Text)
{
    public string Short => Hash[..Math.Min(8, Hash.Length)];
    // 아직 커밋 안 된 줄 - git 은 0000… 해시로 표시
    public bool Uncommitted => Hash.All(c => c == '0');
}

public sealed partial class Repository
{
    public async Task<List<BlameLine>> BlameAsync(string path, string? rev = null, CancellationToken ct = default)
    {
        var args = new List<string> { "blame", "--porcelain", "-w" };
        if (rev is not null) args.Add(rev);
        args.Add("--");
        args.Add(path);
        var r = await RunAsync(args, ct).ConfigureAwait(false);
        if (!r.Ok) throw new GitException(r.ExitCode, r.Error);
        return ParseBlame(r.Output);
    }

    // --porcelain - 커밋마다 첫 등장에만 작성자 등 정보, 이후엔 해시 줄만. 내용 줄은 탭으로 시작
    public static List<BlameLine> ParseBlame(string output)
    {
        var info = new Dictionary<string, (string Author, long Time, string Summary)>();
        var lines = new List<BlameLine>();
        string hash = "";
        int final = 0;
        string author = "", summary = "";
        long time = 0;
        foreach (var raw in output.Split('\n'))
        {
            var l = raw.TrimEnd('\r');
            if (l.StartsWith('\t'))
            {
                if (!info.ContainsKey(hash)) info[hash] = (author, time, summary);
                var (a, t, s) = info[hash];
                lines.Add(new BlameLine(final, hash, a, DateTimeOffset.FromUnixTimeSeconds(t), s, l[1..]));
                continue;
            }
            var sp = l.IndexOf(' ');
            if (sp == 40 && l.Length >= 40 && l[..40].All(Uri.IsHexDigit))
            {
                hash = l[..40];
                var p = l.Split(' ');
                final = p.Length > 2 && int.TryParse(p[2], out var f) ? f : final + 1;
                if (info.TryGetValue(hash, out var known)) (author, time, summary) = known;
                continue;
            }
            if (l.StartsWith("author ")) author = l[7..];
            else if (l.StartsWith("author-time ")) time = long.TryParse(l[12..], out var t) ? t : 0;
            else if (l.StartsWith("summary ")) summary = l[8..];
        }
        return lines;
    }
}

// 저장소 없이 하는 일 - 복제·새로 만들기·설정
public static class GitTools
{
    public static Task<GitResult> CloneAsync(string parentDir, string url, string folder, Action<string>? onLine, CancellationToken ct = default) =>
        Git.RunAsync(parentDir, ["clone", "--progress", url, folder], new GitRunOptions { OnErrorLine = onLine }, ct);

    public static Task<GitResult> InitAsync(string dir) => Git.RunAsync(dir, "init");

    // 주소에서 폴더 이름 - https://x/y/repo.git → repo
    public static string FolderFromUrl(string url)
    {
        var s = url.Trim().TrimEnd('/', '\\');
        if (s.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        var i = s.LastIndexOfAny(['/', '\\', ':']);
        return i >= 0 ? s[(i + 1)..] : s;
    }

    // 설정 - scope: "--global" 또는 "--local" (local 은 dir 이 저장소여야)
    public static async Task<string?> GetConfigAsync(string dir, string scope, string key)
    {
        var r = await Git.RunAsync(dir, "config", scope, "--get", key).ConfigureAwait(false);
        return r.Ok ? r.Output.Trim() : null;
    }

    public static Task<GitResult> SetConfigAsync(string dir, string scope, string key, string value) =>
        value.Length == 0
            ? Git.RunAsync(dir, "config", scope, "--unset", key)
            : Git.RunAsync(dir, "config", scope, key, value);
}
