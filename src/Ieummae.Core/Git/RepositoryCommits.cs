using Ieummae.Core.Diff;

namespace Ieummae.Core.Git;

// 커밋의 변경 파일 한 줄
public sealed record ChangedFile(FileStatus Status, string Path, string? OldPath, int Added, int Deleted, bool Binary)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string Dir => System.IO.Path.GetDirectoryName(Path)?.Replace('\\', '/') ?? "";
}

// 커밋 상세 - 본문 포함
public sealed record CommitInfo(string Hash, string Author, string Email, DateTimeOffset AuthorTime, string Committer, DateTimeOffset CommitTime, string Message)
{
    public string Subject => Message.Split('\n')[0];
    public string Body => Message.Contains('\n') ? Message[(Message.IndexOf('\n') + 1)..].Trim() : "";
}

public enum ResetMode { Soft, Mixed, Hard }

public sealed partial class Repository
{
    // 변경 파일 - 첫 부모(없으면 빈 트리) 대비. 이름 상태·줄 수 두 호출을 동시에
    public async Task<List<ChangedFile>> ChangedFilesAsync(string hash, string? parent, CancellationToken ct = default)
    {
        string from = parent ?? EmptyTree;
        var numTask = RunAsync(["diff", "--no-color", "--no-ext-diff", "-M", "-z", "--numstat", from, hash], ct);
        var st = await RunAsync(["diff", "--no-color", "--no-ext-diff", "-M", "-z", "--name-status", from, hash], ct).ConfigureAwait(false);
        var num = await numTask.ConfigureAwait(false);
        if (!st.Ok) throw new GitException(st.ExitCode, st.Error);
        return ParseChanges(st.Output, num.Output);
    }

    // -z 출력 해석 - 이름 상태: "R100\0옛\0새\0", "M\0경로\0" / 줄 수: "추가\t삭제\t경로\0", 이름 바꾸기는 "추가\t삭제\t\0옛\0새\0"
    public static List<ChangedFile> ParseChanges(string nameStatus, string numstat)
    {
        var counts = new Dictionary<string, (int, int, bool)>();
        var nt = numstat.Split('\0');
        for (int i = 0; i < nt.Length; i++)
        {
            var p = nt[i].Split('\t');
            if (p.Length < 3) continue;
            string path = p[2];
            if (path.Length == 0 && i + 2 < nt.Length) { path = nt[i + 2]; i += 2; }
            bool bin = p[0] == "-";
            counts[path] = (bin ? 0 : int.Parse(p[0]), bin ? 0 : int.Parse(p[1]), bin);
        }

        var list = new List<ChangedFile>();
        var t = nameStatus.Split('\0');
        for (int i = 0; i + 1 < t.Length; i++)
        {
            var code = t[i];
            if (code.Length == 0) continue;
            string? old = null;
            string path = t[++i];
            if (code[0] is 'R' or 'C' && i + 1 < t.Length) { old = path; path = t[++i]; }
            var status = code[0] switch
            {
                'A' => FileStatus.Added,
                'D' => FileStatus.Deleted,
                'R' => FileStatus.Renamed,
                'C' => FileStatus.Copied,
                'T' => FileStatus.TypeChanged,
                _ => FileStatus.Modified,
            };
            var (a, d, b) = counts.GetValueOrDefault(path);
            list.Add(new ChangedFile(status, path, old, a, d, b));
        }
        return list;
    }

    public async Task<CommitInfo> ShowAsync(string hash, CancellationToken ct = default)
    {
        var r = await RunAsync(["show", "-s", "--no-color", "--format=%H%x00%an%x00%ae%x00%at%x00%cn%x00%ct%x00%B", hash], ct).ConfigureAwait(false);
        if (!r.Ok) throw new GitException(r.ExitCode, r.Error);
        var f = r.Output.Split('\0');
        static DateTimeOffset T(string s) => DateTimeOffset.FromUnixTimeSeconds(long.Parse(s));
        return new CommitInfo(f[0], f[1], f[2], T(f[3]), f[4], T(f[5]), f[6].TrimEnd());
    }

    // 동작 - 결과는 화면이 그대로 보여줌 (실패 메시지 포함)
    public Task<GitResult> CheckoutAsync(string target) => RunAsync("checkout", target);

    public Task<GitResult> CreateBranchAsync(string name, string at, bool checkout) =>
        checkout ? RunAsync("checkout", "-b", name, at) : RunAsync("branch", name, at);

    public Task<GitResult> CreateTagAsync(string name, string at, string? message) =>
        string.IsNullOrWhiteSpace(message) ? RunAsync("tag", name, at) : RunAsync("tag", "-a", name, "-m", message, at);

    public Task<GitResult> ResetAsync(ResetMode mode, string to) =>
        RunAsync("reset", "--" + mode.ToString().ToLowerInvariant(), to);

    // 병합 커밋은 첫 부모 기준 (-m 1)
    public Task<GitResult> RevertAsync(string hash, bool merge) =>
        merge ? RunAsync("revert", "--no-edit", "-m", "1", hash) : RunAsync("revert", "--no-edit", hash);

    public Task<GitResult> CherryPickAsync(string hash, bool merge) =>
        merge ? RunAsync("cherry-pick", "-m", "1", hash) : RunAsync("cherry-pick", hash);
}
