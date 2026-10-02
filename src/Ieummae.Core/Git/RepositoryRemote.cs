namespace Ieummae.Core.Git;

// 원격 동작 - 진행률(--progress)은 오류 출력 줄로 받아 화면에 표시
public sealed partial class Repository
{
    static GitRunOptions Progress(Action<string>? onLine) => new() { OnErrorLine = onLine };

    public Task<GitResult> FetchAsync(Action<string>? onLine, CancellationToken ct = default) =>
        Git.RunAsync(Root, ["fetch", "--all", "--prune", "--progress"], Progress(onLine), ct);

    public Task<GitResult> PullAsync(Action<string>? onLine, CancellationToken ct = default) =>
        Git.RunAsync(Root, ["pull", "--progress"], Progress(onLine), ct);

    // 추적 브랜치 없으면 origin 에 같은 이름으로 만들고 추적 설정
    public async Task<GitResult> PushAsync(Action<string>? onLine, CancellationToken ct = default)
    {
        var branch = await BranchAsync(ct).ConfigureAwait(false);
        var upstream = await RunAsync(["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}"], ct).ConfigureAwait(false);
        if (!upstream.Ok && branch is not null)
        {
            var remote = (await RemotesAsync(ct).ConfigureAwait(false)).FirstOrDefault() ?? "origin";
            return await Git.RunAsync(Root, ["push", "--progress", "-u", remote, branch], Progress(onLine), ct).ConfigureAwait(false);
        }
        return await Git.RunAsync(Root, ["push", "--progress"], Progress(onLine), ct).ConfigureAwait(false);
    }

    public async Task<List<string>> RemotesAsync(CancellationToken ct = default)
    {
        var r = await RunAsync(["remote"], ct).ConfigureAwait(false);
        return r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
}
