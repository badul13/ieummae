using Ieummae.Core.Git;

namespace Ieummae.Core.Tests;

public class GitTests
{
    [Fact]
    public async Task 한글_파일명이_이스케이프_없이_나온다()
    {
        using var t = await TempRepo.CreateAsync();
        t.Write("문서/한글 이름.txt", "내용");

        var r = await t.GitAsync("status", "--porcelain", "-uall");

        Assert.Contains("문서/한글 이름.txt", r.Output);
    }

    [Fact]
    public async Task 실패하면_종료코드와_오류를_돌려준다()
    {
        using var t = await TempRepo.CreateAsync();

        var r = await Git.Git.RunAsync(t.Root, "rev-parse", "--verify", "없는-브랜치");

        Assert.False(r.Ok);
        Assert.NotEqual("", r.Error);
    }

    [Fact]
    public async Task 취소하면_프로세스를_멈춘다()
    {
        using var t = await TempRepo.CreateAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Git.Git.RunAsync(t.Root, ["status"], cts.Token));
    }
}

public class RepositoryTests
{
    [Fact]
    public async Task 하위_폴더와_파일_경로에서_최상위를_찾는다()
    {
        using var t = await TempRepo.CreateAsync();
        t.Write("a/b/c.txt", "x");

        Assert.Equal(t.Root, Repository.Discover(Path.Combine(t.Root, "a", "b"))?.Root);
        Assert.Equal(t.Root, Repository.Discover(Path.Combine(t.Root, "a", "b", "c.txt"))?.Root);
    }

    [Fact]
    public void 저장소_밖이면_null()
    {
        var dir = Directory.CreateTempSubdirectory("ieummae-test-");
        try { Assert.Null(Repository.Discover(dir.FullName)); }
        finally { dir.Delete(); }
    }

    [Fact]
    public async Task 브랜치_이름과_분리된_HEAD()
    {
        using var t = await TempRepo.CreateAsync();
        t.Write("a.txt", "1");
        await t.GitAsync("add", ".");
        await t.GitAsync("commit", "-q", "-m", "첫 커밋");
        var repo = Repository.Discover(t.Root)!;

        Assert.Equal("main", await repo.BranchAsync());

        await t.GitAsync("checkout", "-q", "--detach");
        Assert.Null(await repo.BranchAsync());
    }

    [Fact]
    public async Task 한글_커밋_메시지를_그대로_읽는다()
    {
        using var t = await TempRepo.CreateAsync();
        t.Write("a.txt", "1");
        await t.GitAsync("add", ".");
        await t.GitAsync("commit", "-q", "-m", "양털 단추 ✂");

        var r = await t.GitAsync("log", "-1", "--format=%s");

        Assert.Equal("양털 단추 ✂", r.Output.Trim());
    }
}
