using Ieummae.Core.Diff;

namespace Ieummae.Core.Git;

// 비교 대상
public abstract record DiffSpec
{
    // 커밋 하나 - 첫 부모(없으면 빈 트리) 대비
    public sealed record Commit(string Hash, string? Parent) : DiffSpec;
    // 두 커밋
    public sealed record Range(string From, string To) : DiffSpec;
    // 작업 트리 - HEAD 대비 (스테이징 여부 무관)
    public sealed record WorkingTree : DiffSpec;
    // 추적 안 하는 새 파일 - 빈 파일 대비
    public sealed record Untracked : DiffSpec;
}

public sealed record DiffOptions(int Context = 3, bool IgnoreWhitespace = false);

public sealed partial class Repository
{
    // 빈 트리 - 첫 커밋·커밋 없는 저장소 비교용 (git 고정 해시)
    public const string EmptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

    public async Task<List<FileDiff>> DiffAsync(DiffSpec spec, IReadOnlyList<string> paths, DiffOptions? opt = null, CancellationToken ct = default)
    {
        opt ??= new DiffOptions();
        var args = new List<string>
        {
            "diff", "--no-color", "--no-ext-diff", "--src-prefix=a/", "--dst-prefix=b/", "-M", $"-U{opt.Context}",
        };
        if (opt.IgnoreWhitespace) args.Add("-w");
        switch (spec)
        {
            case DiffSpec.Commit c: args.Add(c.Parent ?? EmptyTree); args.Add(c.Hash); break;
            case DiffSpec.Range r: args.Add(r.From); args.Add(r.To); break;
            case DiffSpec.WorkingTree: args.Add(await HasHeadAsync(ct).ConfigureAwait(false) ? "HEAD" : EmptyTree); break;
            case DiffSpec.Untracked: args.Add("--no-index"); break;
        }
        args.Add("--");
        if (spec is DiffSpec.Untracked) args.Add("/dev/null");
        args.AddRange(paths);

        var r2 = await RunAsync(args, ct).ConfigureAwait(false);
        // --no-index 는 차이가 있으면 종료 코드 1
        if (!r2.Ok && !(spec is DiffSpec.Untracked && r2.ExitCode == 1)) throw new GitException(r2.ExitCode, r2.Error);
        var files = DiffParser.Parse(r2.Output);
        foreach (var f in files) WordDiff.Apply(f);
        // --no-index 경로 - 저장소 기준 상대 경로로 정리, 새 파일로 표시
        if (spec is DiffSpec.Untracked)
            foreach (var f in files) { f.Status = FileStatus.Added; f.NewPath = f.NewPath.TrimStart('/'); }
        return files;
    }

    // 탐색기에서 받은 절대 경로 → 저장소 기준 / 구분 상대 경로 (최상위면 "")
    public string Relative(string fullPath)
    {
        var rel = Path.GetRelativePath(Root, Path.GetFullPath(fullPath)).Replace('\\', '/');
        return rel == "." ? "" : rel;
    }

    public async Task<bool> IsTrackedAsync(string relPath, CancellationToken ct = default) =>
        (await RunAsync(["ls-files", "--error-unmatch", "--", relPath], ct).ConfigureAwait(false)).Ok;

    public async Task<bool> HasHeadAsync(CancellationToken ct = default) =>
        (await RunAsync(["rev-parse", "--verify", "-q", "HEAD"], ct).ConfigureAwait(false)).Ok;
}
