namespace Ieummae.Core.Git;

public enum Operation { None, Merge, Rebase, CherryPick, Revert }

// 충돌 종류 - 양쪽 수정 / 한쪽이 지움 / 양쪽 새로 만듦
public enum ConflictKind { BothModified, DeletedByUs, DeletedByThem, BothAdded, BothDeleted, Other }

public sealed partial class Repository
{
    string? _gitDir;

    // .git 폴더 실제 위치 - worktree 면 다른 곳
    public async Task<string> GitDirAsync(CancellationToken ct = default)
    {
        if (_gitDir is not null) return _gitDir;
        var r = await RunAsync(["rev-parse", "--absolute-git-dir"], ct).ConfigureAwait(false);
        return _gitDir = r.Ok ? r.Output.Trim().Replace('/', Path.DirectorySeparatorChar) : Path.Combine(Root, ".git");
    }

    // 진행 중인 작업 - git 이 남기는 표시 파일로 판단
    public async Task<Operation> OperationAsync(CancellationToken ct = default)
    {
        var dir = await GitDirAsync(ct).ConfigureAwait(false);
        if (Directory.Exists(Path.Combine(dir, "rebase-merge")) || Directory.Exists(Path.Combine(dir, "rebase-apply"))) return Operation.Rebase;
        if (File.Exists(Path.Combine(dir, "MERGE_HEAD"))) return Operation.Merge;
        if (File.Exists(Path.Combine(dir, "CHERRY_PICK_HEAD"))) return Operation.CherryPick;
        if (File.Exists(Path.Combine(dir, "REVERT_HEAD"))) return Operation.Revert;
        return Operation.None;
    }

    // 리베이스 진행 단계 - (현재, 전체), 모르면 null
    public async Task<(int, int)?> RebaseStepAsync(CancellationToken ct = default)
    {
        var dir = Path.Combine(await GitDirAsync(ct).ConfigureAwait(false), "rebase-merge");
        if (!Directory.Exists(dir)) return null;
        string Read(string f) => File.Exists(Path.Combine(dir, f)) ? File.ReadAllText(Path.Combine(dir, f)).Trim() : "";
        return int.TryParse(Read("msgnum"), out var n) && int.TryParse(Read("end"), out var e) ? (n, e) : null;
    }

    static string Verb(Operation op) => op switch
    {
        Operation.Merge => "merge",
        Operation.Rebase => "rebase",
        Operation.CherryPick => "cherry-pick",
        Operation.Revert => "revert",
        _ => throw new InvalidOperationException("진행 중인 작업 없음"),
    };

    // 계속하기 - 커밋 메시지 편집기 띄우지 않음
    public Task<GitResult> ContinueAsync(Operation op) => Git.RunAsync(Root, [Verb(op), "--continue"], NoEditor, CancellationToken.None);
    public Task<GitResult> AbortAsync(Operation op) => RunAsync(Verb(op), "--abort");
    public Task<GitResult> SkipAsync() => Git.RunAsync(Root, ["rebase", "--skip"], NoEditor, CancellationToken.None);

    public static ConflictKind KindOf(WorkingFile f) => (f.Index, f.Work) switch
    {
        ('U', 'U') => ConflictKind.BothModified,
        ('D', 'U') => ConflictKind.DeletedByUs,
        ('U', 'D') => ConflictKind.DeletedByThem,
        ('A', 'A') => ConflictKind.BothAdded,
        ('D', 'D') => ConflictKind.BothDeleted,
        _ => ConflictKind.Other,
    };

    // 해결 표시 - 파일이 있으면 add, 없으면 rm
    public Task<GitResult> MarkResolvedAsync(string path) =>
        File.Exists(Path.Combine(Root, path)) ? RunAsync("add", "--", path) : RunAsync("rm", "-q", "--", path);

    // 파일 통째로 한쪽 고르기 - 그쪽이 지운 파일이면 지움
    public async Task<GitResult> TakeWholeAsync(WorkingFile f, bool ours)
    {
        var kind = KindOf(f);
        bool deleted = ours ? kind == ConflictKind.DeletedByUs : kind == ConflictKind.DeletedByThem;
        if (deleted || kind == ConflictKind.BothDeleted) return await RunAsync("rm", "-q", "--", f.Path).ConfigureAwait(false);
        var r = await RunAsync("checkout", ours ? "--ours" : "--theirs", "--", f.Path).ConfigureAwait(false);
        return r.Ok ? await RunAsync("add", "--", f.Path).ConfigureAwait(false) : r;
    }
}
