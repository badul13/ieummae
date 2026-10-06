namespace Ieummae.Core.Git;

// Worktree - 다른 워크트리가 체크아웃 중이면 그 폴더, 아니면 null
public sealed record BranchInfo(string Name, bool IsRemote, bool IsCurrent, string? Upstream, string Hash, DateTimeOffset Date, string? Worktree = null)
{
    // 원격 브랜치의 로컬 이름 (origin/feat/x → feat/x)
    public string LocalName => IsRemote ? Name[(Name.IndexOf('/') + 1)..] : Name;
}

public sealed record StashInfo(string Ref, string Message, DateTimeOffset Date);
public sealed record TagInfo(string Name, string Hash, DateTimeOffset Date, string Subject);
public sealed record RemoteInfo(string Name, string Url);

public sealed partial class Repository
{
    public async Task<List<BranchInfo>> BranchesAsync(CancellationToken ct = default)
    {
        var r = await RunAsync(["for-each-ref", "--sort=-committerdate",
            "--format=%(refname)%00%(objectname:short)%00%(upstream:short)%00%(committerdate:unix)%00%(HEAD)%00%(worktreepath)",
            "refs/heads", "refs/remotes"], ct).ConfigureAwait(false);
        var list = new List<BranchInfo>();
        foreach (var line in r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split('\0');
            if (f.Length < 6 || f[0].EndsWith("/HEAD")) continue;
            bool remote = f[0].StartsWith("refs/remotes/");
            var name = remote ? f[0][13..] : f[0][11..];
            bool current = f[4] == "*";
            // 지금 워크트리는 HEAD 표시로 이미 구분 - 다른 워크트리만
            var worktree = !current && f[5].Length > 0 ? Path.GetFullPath(f[5]) : null;
            list.Add(new BranchInfo(name, remote, current, f[2].Length > 0 ? f[2] : null, f[1],
                DateTimeOffset.FromUnixTimeSeconds(long.TryParse(f[3], out var t) ? t : 0), worktree));
        }
        return list;
    }

    // 추적 브랜치 대비 앞섬·뒤처짐 - 추적 없으면 null
    public async Task<(int Ahead, int Behind)?> AheadBehindAsync(CancellationToken ct = default)
    {
        var r = await RunAsync(["rev-list", "--left-right", "--count", "HEAD...@{u}"], ct).ConfigureAwait(false);
        if (!r.Ok) return null;
        var p = r.Output.Split('\t', ' ');
        return (int.Parse(p[0]), int.Parse(p[1].Trim()));
    }

    // 브랜치 전환 - 원격 브랜치면 같은 이름 로컬 브랜치를 만들어 추적 (이미 있으면 그쪽으로)
    public async Task<GitResult> SwitchAsync(BranchInfo b, CancellationToken ct = default)
    {
        if (!b.IsRemote) return await RunAsync(["checkout", b.Name], ct).ConfigureAwait(false);
        var exists = await RunAsync(["rev-parse", "--verify", "-q", "refs/heads/" + b.LocalName], ct).ConfigureAwait(false);
        return exists.Ok
            ? await RunAsync(["checkout", b.LocalName], ct).ConfigureAwait(false)
            : await RunAsync(["checkout", "--track", "-b", b.LocalName, b.Name], ct).ConfigureAwait(false);
    }

    public Task<GitResult> DeleteBranchAsync(string name, bool force) => RunAsync("branch", force ? "-D" : "-d", name);

    public Task<GitResult> DeleteRemoteBranchAsync(BranchInfo b, Action<string>? onLine, CancellationToken ct = default) =>
        Git.RunAsync(Root, ["push", "--progress", b.Name[..b.Name.IndexOf('/')], "--delete", b.LocalName], Progress(onLine), ct);

    public Task<GitResult> MergeAsync(string name, bool noFastForward) =>
        noFastForward ? RunAsync("merge", "--no-ff", "--no-edit", name) : RunAsync("merge", "--no-edit", name);

    // 편집기를 띄우지 않게 - 계속하기 때 메시지 편집 생략
    static readonly GitRunOptions NoEditor = new() { Environment = new Dictionary<string, string> { ["GIT_EDITOR"] = "true" } };

    public Task<GitResult> RebaseAsync(string onto) => Git.RunAsync(Root, ["rebase", onto], NoEditor, CancellationToken.None);

    // 스태시
    public Task<GitResult> StashPushAsync(string? message, bool untracked)
    {
        var args = new List<string> { "stash", "push" };
        if (untracked) args.Add("-u");
        if (!string.IsNullOrWhiteSpace(message)) { args.Add("-m"); args.Add(message); }
        return RunAsync(args, CancellationToken.None);
    }

    public async Task<List<StashInfo>> StashesAsync(CancellationToken ct = default)
    {
        var r = await RunAsync(["stash", "list", "--format=%gd%x00%gs%x00%ct"], ct).ConfigureAwait(false);
        return r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split('\0'))
            .Where(f => f.Length >= 3)
            .Select(f => new StashInfo(f[0], f[1], DateTimeOffset.FromUnixTimeSeconds(long.TryParse(f[2], out var t) ? t : 0)))
            .ToList();
    }

    public Task<GitResult> StashApplyAsync(string stash) => RunAsync("stash", "apply", stash);
    public Task<GitResult> StashPopAsync(string stash) => RunAsync("stash", "pop", stash);
    public Task<GitResult> StashDropAsync(string stash) => RunAsync("stash", "drop", stash);

    // 태그
    public async Task<List<TagInfo>> TagsAsync(CancellationToken ct = default)
    {
        var r = await RunAsync(["for-each-ref", "--sort=-creatordate",
            "--format=%(refname:short)%00%(objectname:short)%00%(creatordate:unix)%00%(subject)", "refs/tags"], ct).ConfigureAwait(false);
        return r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split('\0'))
            .Where(f => f.Length >= 4)
            .Select(f => new TagInfo(f[0], f[1], DateTimeOffset.FromUnixTimeSeconds(long.TryParse(f[2], out var t) ? t : 0), f[3]))
            .ToList();
    }

    public Task<GitResult> DeleteTagAsync(string name) => RunAsync("tag", "-d", name);

    public Task<GitResult> PushTagAsync(string remote, string tag, Action<string>? onLine, CancellationToken ct = default) =>
        Git.RunAsync(Root, ["push", "--progress", remote, "refs/tags/" + tag], Progress(onLine), ct);

    public Task<GitResult> PushAllTagsAsync(string remote, Action<string>? onLine, CancellationToken ct = default) =>
        Git.RunAsync(Root, ["push", "--progress", remote, "--tags"], Progress(onLine), ct);

    // 원격 저장소
    public async Task<List<RemoteInfo>> RemoteInfosAsync(CancellationToken ct = default)
    {
        var r = await RunAsync(["remote", "-v"], ct).ConfigureAwait(false);
        return r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split('\t', ' '))
            .Where(f => f.Length >= 2)
            .GroupBy(f => f[0])
            .Select(g => new RemoteInfo(g.Key, g.First()[1]))
            .ToList();
    }

    public Task<GitResult> AddRemoteAsync(string name, string url) => RunAsync("remote", "add", name, url);
    public Task<GitResult> RemoveRemoteAsync(string name) => RunAsync("remote", "remove", name);
    public Task<GitResult> SetRemoteUrlAsync(string name, string url) => RunAsync("remote", "set-url", name, url);
}
