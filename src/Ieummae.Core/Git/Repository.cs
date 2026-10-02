namespace Ieummae.Core.Git;

// 작업 트리 하나 - 최상위 경로 기준으로 git 실행
public sealed class Repository
{
    public string Root { get; }
    public string Name => Path.GetFileName(Root);

    Repository(string root) => Root = root;

    // 저장소 찾기 - 경로에서 위로 올라가며 .git (폴더 또는 worktree 의 파일) 탐색
    // git 프로세스 없이 파일 시스템만 봄 - 창 표시 전에 호출해도 빠름
    public static Repository? Discover(string path)
    {
        var dir = Path.GetFullPath(path);
        if (File.Exists(dir)) dir = Path.GetDirectoryName(dir)!;
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
        {
            var git = Path.Combine(d.FullName, ".git");
            if (Directory.Exists(git) || File.Exists(git)) return new Repository(d.FullName.TrimEnd(Path.DirectorySeparatorChar));
        }
        return null;
    }

    public Task<GitResult> RunAsync(params string[] args) => Git.RunAsync(Root, args);

    public Task<GitResult> RunAsync(IEnumerable<string> args, CancellationToken ct) => Git.RunAsync(Root, args, ct);

    // 현재 브랜치 - 분리된 HEAD 면 null
    public async Task<string?> BranchAsync(CancellationToken ct = default)
    {
        var r = await RunAsync(["symbolic-ref", "--short", "-q", "HEAD"], ct).ConfigureAwait(false);
        var name = r.Output.Trim();
        return r.Ok && name.Length > 0 ? name : null;
    }
}
