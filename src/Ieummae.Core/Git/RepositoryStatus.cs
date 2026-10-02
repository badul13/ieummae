using System.Text;

namespace Ieummae.Core.Git;

public enum ChangeKind { Modified, Added, Deleted, Renamed, Copied, TypeChanged, Untracked, Conflict }

// 작업 트리 파일 하나 - status v2 의 XY(인덱스·작업 트리) 상태
public sealed record WorkingFile(string Path, string? OrigPath, char Index, char Work, ChangeKind Kind)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string Dir => System.IO.Path.GetDirectoryName(Path)?.Replace('\\', '/') ?? "";
    public bool IsUntracked => Kind == ChangeKind.Untracked;
    public bool IsConflict => Kind == ChangeKind.Conflict;
    // HEAD 에 없던 파일 (새로 스테이징)
    public bool IsNewInIndex => Index == 'A';
}

public sealed record StatusResult(string? Branch, string? Upstream, int Ahead, int Behind, List<WorkingFile> Files);

public sealed partial class Repository
{
    // 작업 트리 상태 - 추적 안 하는 파일은 하나씩 (-uall), path 지정 시 그 아래만
    public async Task<StatusResult> StatusAsync(string? path = null, CancellationToken ct = default)
    {
        // 이름 바꾸기 - 스테이징된 것만 git 이 짝지어 줌, 작업 트리 쪽은 삭제+새 파일로
        var args = new List<string> { "status", "--porcelain=v2", "-z", "--branch", "-uall" };
        if (!string.IsNullOrEmpty(path)) { args.Add("--"); args.Add(path); }
        var r = await RunAsync(args, ct).ConfigureAwait(false);
        if (!r.Ok) throw new GitException(r.ExitCode, r.Error);
        return ParseStatus(r.Output);
    }

    public static StatusResult ParseStatus(string output)
    {
        string? branch = null, upstream = null;
        int ahead = 0, behind = 0;
        var files = new List<WorkingFile>();
        var t = output.Split('\0');
        for (int i = 0; i < t.Length; i++)
        {
            var e = t[i];
            if (e.Length == 0) continue;
            switch (e[0])
            {
                case '#':
                    if (e.StartsWith("# branch.head ")) { branch = e[14..]; if (branch == "(detached)") branch = null; }
                    else if (e.StartsWith("# branch.upstream ")) upstream = e[18..];
                    else if (e.StartsWith("# branch.ab "))
                    {
                        var p = e[12..].Split(' ');
                        ahead = int.Parse(p[0].TrimStart('+'));
                        behind = int.Parse(p[1].TrimStart('-'));
                    }
                    break;
                case '1':
                {
                    // 1 XY sub mH mI mW hH hI path
                    var p = e.Split(' ', 9);
                    files.Add(new WorkingFile(p[8], null, p[1][0], p[1][1], KindOf(p[1])));
                    break;
                }
                case '2':
                {
                    // 2 XY sub mH mI mW hH hI Xscore path \0 origPath
                    var p = e.Split(' ', 10);
                    var orig = i + 1 < t.Length ? t[++i] : null;
                    files.Add(new WorkingFile(p[9], orig, p[1][0], p[1][1], p[8][0] == 'C' ? ChangeKind.Copied : ChangeKind.Renamed));
                    break;
                }
                case 'u':
                {
                    // u XY sub m1 m2 m3 mW h1 h2 h3 path
                    var p = e.Split(' ', 11);
                    files.Add(new WorkingFile(p[10], null, p[1][0], p[1][1], ChangeKind.Conflict));
                    break;
                }
                case '?':
                    files.Add(new WorkingFile(e[2..], null, '?', '?', ChangeKind.Untracked));
                    break;
            }
        }
        return new StatusResult(branch, upstream, ahead, behind, files);
    }

    static ChangeKind KindOf(string xy) =>
        xy.Contains('D') ? ChangeKind.Deleted
        : xy[0] == 'A' ? ChangeKind.Added
        : xy.Contains('T') ? ChangeKind.TypeChanged
        : ChangeKind.Modified;

    // 작업 트리 줄 수 - HEAD 대비 (추적 중인 파일만)
    public async Task<Dictionary<string, (int Added, int Deleted)>> WorkingNumstatAsync(CancellationToken ct = default)
    {
        var map = new Dictionary<string, (int, int)>();
        var from = await HasHeadAsync(ct).ConfigureAwait(false) ? "HEAD" : EmptyTree;
        var r = await RunAsync(["diff", "--no-color", "--no-ext-diff", "--no-renames", "-z", "--numstat", from], ct).ConfigureAwait(false);
        foreach (var e in r.Output.Split('\0'))
        {
            var p = e.Split('\t');
            if (p.Length < 3) continue;
            map[p[2]] = p[0] == "-" ? (0, 0) : (int.Parse(p[0]), int.Parse(p[1]));
        }
        return map;
    }

    // 고른 파일만 커밋 - 다른 스테이징 내용은 그대로 둠 (--only 와 경로)
    // 경로 목록은 표준 입력으로 (명령줄 길이 제한 회피), 메시지는 임시 파일로
    public async Task<GitResult> CommitAsync(string message, IReadOnlyList<WorkingFile> files, bool amend, CancellationToken ct = default)
    {
        var untracked = files.Where(f => f.IsUntracked).Select(f => f.Path).ToList();
        if (untracked.Count > 0)
        {
            var add = await RunAsync(["add", "--pathspec-from-file=-", "--pathspec-file-nul"], new GitRunOptions { Input = Nul(untracked) }, ct).ConfigureAwait(false);
            if (!add.Ok) return add;
        }
        var msgFile = System.IO.Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(msgFile, message, new UTF8Encoding(false), ct).ConfigureAwait(false);
            var args = new List<string> { "commit", "-F", msgFile, "--cleanup=strip" };
            if (amend) args.Add("--amend");
            // 병합·체리픽 중 - 일부만 커밋(--only) 불가, 고른 파일을 스테이징하고 전체 커밋
            if (await OperationAsync(ct).ConfigureAwait(false) != Operation.None)
            {
                var all = files.SelectMany(f => f.OrigPath is { } o ? new[] { f.Path, o } : [f.Path]).Distinct().ToList();
                if (all.Count > 0)
                {
                    var add = await RunAsync(["add", "-A", "--pathspec-from-file=-", "--pathspec-file-nul"], new GitRunOptions { Input = Nul(all) }, ct).ConfigureAwait(false);
                    if (!add.Ok) return add;
                }
                return await RunAsync(args, ct).ConfigureAwait(false);
            }
            if (files.Count == 0)
                return await RunAsync([.. args, "--only"], ct).ConfigureAwait(false);
            // 이름 바꾸기는 옛 경로도 같이 (옛 경로 삭제가 커밋에 들어가게)
            var paths = files.SelectMany(f => f.OrigPath is { } o ? new[] { f.Path, o } : [f.Path]).Distinct().ToList();
            return await RunAsync([.. args, "--only", "--pathspec-from-file=-", "--pathspec-file-nul"], new GitRunOptions { Input = Nul(paths) }, ct).ConfigureAwait(false);
        }
        finally { File.Delete(msgFile); }
    }

    Task<GitResult> RunAsync(List<string> args, GitRunOptions opt, CancellationToken ct) => Git.RunAsync(Root, args, opt, ct);

    static string Nul(IEnumerable<string> paths) => string.Concat(paths.Select(p => p + "\0"));

    public async Task<string> HeadMessageAsync(CancellationToken ct = default)
    {
        var r = await RunAsync(["log", "-1", "--format=%B"], ct).ConfigureAwait(false);
        return r.Ok ? r.Output.TrimEnd() : "";
    }

    // 변경 되돌리기 - HEAD 에 있던 파일은 HEAD 내용으로, 새로 스테이징한 파일은 스테이징만 풀기(파일은 남음)
    // 추적 안 하는 파일 삭제는 화면 쪽에서 (휴지통)
    public async Task<GitResult> RevertFilesAsync(IReadOnlyList<WorkingFile> files, CancellationToken ct = default)
    {
        var fresh = files.Where(f => f.IsNewInIndex).Select(f => f.Path).ToList();
        var tracked = files.Where(f => !f.IsUntracked && !f.IsNewInIndex)
            .SelectMany(f => f.OrigPath is { } o ? new[] { f.Path, o } : [f.Path]).Distinct().ToList();
        if (fresh.Count > 0)
        {
            var r = await RunAsync(["rm", "--cached", "-q", "--pathspec-from-file=-", "--pathspec-file-nul"], new GitRunOptions { Input = Nul(fresh) }, ct).ConfigureAwait(false);
            if (!r.Ok) return r;
        }
        if (tracked.Count > 0)
            return await RunAsync(["restore", "--source=HEAD", "--staged", "--worktree", "--pathspec-from-file=-", "--pathspec-file-nul"], new GitRunOptions { Input = Nul(tracked) }, ct).ConfigureAwait(false);
        return new GitResult(0, "", "");
    }

    // .gitignore 에 한 줄 추가 - 마지막 줄바꿈 없으면 붙여서
    public void AddToIgnore(string pattern)
    {
        var path = System.IO.Path.Combine(Root, ".gitignore");
        var text = File.Exists(path) ? File.ReadAllText(path) : "";
        if (text.Replace("\r", "").Split('\n').Contains(pattern)) return;
        var nl = text.Contains("\r\n") ? "\r\n" : "\n";
        File.AppendAllText(path, (text.Length > 0 && !text.EndsWith('\n') ? nl : "") + pattern + nl);
    }
}
