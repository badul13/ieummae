using Ieummae.Core.Git;

namespace Ieummae.Core.Tests;

// 원격 동작 - 맨 저장소(bare)를 원격으로 두고 실제로 fetch·pull·push
public class RemoteTests
{
    static async Task<(TempRepo Origin, TempRepo A, TempRepo B)> Pair()
    {
        var origin = await TempRepo.CreateAsync();
        // 원격 역할 - 맨 저장소로 다시 만들기
        Directory.Delete(origin.Root, true);
        Directory.CreateDirectory(origin.Root);
        await origin.GitAsync("init", "-q", "--bare", "-b", "main");

        var a = await TempRepo.CreateAsync();
        a.Write("a.txt", "1\n");
        await a.GitAsync("add", ".");
        await a.GitAsync("commit", "-q", "-m", "첫");
        await a.GitAsync("remote", "add", "origin", origin.Root);
        var push = await Repository.Discover(a.Root)!.PushAsync(null);
        Assert.True(push.Ok, push.Message);

        var b = await TempRepo.CreateAsync();
        await b.GitAsync("remote", "add", "origin", origin.Root);
        await b.GitAsync("fetch", "-q", "origin");
        await b.GitAsync("checkout", "-q", "-b", "main", "--track", "origin/main");
        return (origin, a, b);
    }

    [Fact]
    public async Task 첫_Push_는_추적_설정_Pull_Fetch_앞섬_뒤처짐()
    {
        var (origin, a, b) = await Pair();
        using var _o = origin; using var _a = a; using var _b = b;
        var ra = Repository.Discover(a.Root)!;
        var rb = Repository.Discover(b.Root)!;
        Assert.Equal((0, 0), await ra.AheadBehindAsync());

        a.Write("a.txt", "1\n2\n");
        await a.GitAsync("commit", "-q", "-am", "둘");
        Assert.Equal((1, 0), await ra.AheadBehindAsync());
        var lines = new List<string>();
        Assert.True((await ra.PushAsync(lines.Add)).Ok);

        Assert.True((await rb.FetchAsync(null)).Ok);
        Assert.Equal((0, 1), await rb.AheadBehindAsync());
        Assert.True((await rb.PullAsync(null)).Ok);
        Assert.Equal("1\n2\n", File.ReadAllText(Path.Combine(b.Root, "a.txt")).Replace("\r", ""));
    }

    [Fact]
    public async Task 원격_브랜치로_전환하면_추적_브랜치_생성()
    {
        var (origin, a, b) = await Pair();
        using var _o = origin; using var _a = a; using var _b = b;
        await a.GitAsync("checkout", "-q", "-b", "feat/x");
        await a.GitAsync("push", "-q", "-u", "origin", "feat/x");
        var rb = Repository.Discover(b.Root)!;
        await rb.FetchAsync(null);

        var remote = (await rb.BranchesAsync()).Single(x => x.Name == "origin/feat/x");
        Assert.True(remote.IsRemote);
        Assert.True((await rb.SwitchAsync(remote)).Ok);
        Assert.Equal("feat/x", await rb.BranchAsync());
        var local = (await rb.BranchesAsync()).Single(x => x.Name == "feat/x");
        Assert.Equal(("origin/feat/x", true), (local.Upstream, local.IsCurrent));

        var remotes = await rb.RemoteInfosAsync();
        Assert.Equal("origin", Assert.Single(remotes).Name);
    }
}

public class BranchOpsTests
{
    [Fact]
    public async Task 다른_워크트리가_쓰는_브랜치는_그_폴더_표시()
    {
        using var t = await TempRepo.CreateAsync();
        await t.GitAsync("commit", "-q", "--allow-empty", "-m", "첫");
        await t.GitAsync("branch", "옆");
        var other = t.Root + "-옆";
        await t.GitAsync("worktree", "add", "-q", other, "옆");
        try
        {
            var branches = await Repository.Discover(t.Root)!.BranchesAsync();
            Assert.Equal(Path.GetFullPath(other), branches.Single(b => b.Name == "옆").Worktree);
            // 지금 워크트리 브랜치는 null
            Assert.Null(branches.Single(b => b.IsCurrent).Worktree);
        }
        finally { Directory.Delete(other, true); }
    }

    [Fact]
    public async Task 병합_리베이스_스태시_태그()
    {
        using var t = await TempRepo.CreateAsync();
        t.Write("a.txt", "1\n");
        await t.GitAsync("add", ".");
        await t.GitAsync("commit", "-q", "-m", "첫");
        await t.GitAsync("checkout", "-q", "-b", "옆");
        t.Write("b.txt", "b\n");
        await t.GitAsync("add", ".");
        await t.GitAsync("commit", "-q", "-m", "옆 커밋");
        await t.GitAsync("checkout", "-q", "main");
        t.Write("c.txt", "c\n");
        await t.GitAsync("add", ".");
        await t.GitAsync("commit", "-q", "-m", "main 커밋");
        var repo = Repository.Discover(t.Root)!;

        // 리베이스 - 옆 브랜치를 main 위로
        await t.GitAsync("checkout", "-q", "옆");
        Assert.True((await repo.RebaseAsync("main")).Ok);
        Assert.True(File.Exists(Path.Combine(t.Root, "c.txt")));

        // 병합 - 빨리 감기 금지
        await t.GitAsync("checkout", "-q", "main");
        Assert.True((await repo.MergeAsync("옆", noFastForward: true)).Ok);
        Assert.Equal(2, (await t.GitAsync("log", "-1", "--format=%P")).Output.Trim().Split(' ').Length);

        // 브랜치 삭제 - 병합됐으므로 -d 로 됨
        Assert.True((await repo.DeleteBranchAsync("옆", force: false)).Ok);

        // 스태시 - 추적 안 하는 파일 포함
        t.Write("a.txt", "바뀜\n");
        t.Write("새.txt", "n\n");
        Assert.True((await repo.StashPushAsync("작업 중", untracked: true)).Ok);
        Assert.Empty((await repo.StatusAsync()).Files);
        var stash = Assert.Single(await repo.StashesAsync());
        Assert.Contains("작업 중", stash.Message);
        Assert.True((await repo.StashPopAsync(stash.Ref)).Ok);
        Assert.Equal(2, (await repo.StatusAsync()).Files.Count);

        // 태그
        Assert.True((await repo.CreateTagAsync("v1", "HEAD", "첫 판")).Ok);
        var tag = Assert.Single(await repo.TagsAsync());
        Assert.Equal(("v1", "첫 판"), (tag.Name, tag.Subject));
        Assert.True((await repo.DeleteTagAsync("v1")).Ok);
    }
}
