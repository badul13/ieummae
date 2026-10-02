using Ieummae.Core.Conflict;
using Ieummae.Core.Git;

namespace Ieummae.Core.Tests;

public class ConflictTextTests
{
    const string Text = "a\r\n<<<<<<< HEAD\r\nmine\r\n||||||| base\r\nold\r\n=======\r\ntheirs 1\r\ntheirs 2\r\n>>>>>>> feat\r\nz\r\n";

    [Fact]
    public void diff3_형식_해석과_다시_쓰기()
    {
        var t = ConflictText.Parse(Text);
        var c = Assert.Single(t.Conflicts);
        Assert.Equal(["mine"], c.Ours);
        Assert.Equal(["old"], c.Base!);
        Assert.Equal(["theirs 1", "theirs 2"], c.Theirs);
        Assert.Equal(("HEAD", "feat"), (c.OursLabel, c.TheirsLabel));
        Assert.False(t.AllChosen);

        c.Choice = Choice.Both;
        Assert.Equal("a\r\nmine\r\ntheirs 1\r\ntheirs 2\r\nz\r\n", t.Build());
        c.Choice = Choice.Custom;
        c.Custom = "직접\n고침";
        Assert.Equal("a\r\n직접\r\n고침\r\nz\r\n", t.Build());
    }

    [Fact]
    public void 끝_표시_없으면_공통으로()
    {
        var t = ConflictText.Parse("a\n<<<<<<< HEAD\nb\n");
        Assert.Empty(t.Conflicts);
        Assert.Equal("a\n<<<<<<< HEAD\nb\n", t.Build());
    }
}

public class ConflictRepoTests
{
    // main 과 옆 브랜치가 같은 줄을 다르게 고치고, 옆에서 지운 파일을 main 에서 고침
    static async Task<(TempRepo, Repository)> Diverged()
    {
        var t = await TempRepo.CreateAsync();
        t.Write("a.txt", "1\n2\n3\n");
        t.Write("gone.txt", "x\n");
        await t.GitAsync("add", ".");
        await t.GitAsync("commit", "-q", "-m", "첫");
        await t.GitAsync("checkout", "-q", "-b", "옆");
        t.Write("a.txt", "1\n옆\n3\n");
        File.Delete(Path.Combine(t.Root, "gone.txt"));
        await t.GitAsync("commit", "-q", "-am", "옆 수정");
        await t.GitAsync("checkout", "-q", "main");
        t.Write("a.txt", "1\n메인\n3\n");
        t.Write("gone.txt", "y\n");
        await t.GitAsync("commit", "-q", "-am", "메인 수정");
        return (t, Repository.Discover(t.Root)!);
    }

    [Fact]
    public async Task 병합_충돌_해결하고_계속()
    {
        var (t, repo) = await Diverged();
        using var _ = t;
        Assert.False((await repo.MergeAsync("옆", false)).Ok);
        Assert.Equal(Operation.Merge, await repo.OperationAsync());

        var conflicts = (await repo.StatusAsync()).Files.Where(f => f.IsConflict).ToList();
        Assert.Equal(2, conflicts.Count);
        var a = conflicts.Single(f => f.Path == "a.txt");
        var gone = conflicts.Single(f => f.Path == "gone.txt");
        Assert.Equal(ConflictKind.BothModified, Repository.KindOf(a));
        Assert.Equal(ConflictKind.DeletedByThem, Repository.KindOf(gone));

        var text = ConflictText.Parse(File.ReadAllText(Path.Combine(t.Root, "a.txt")));
        text.Conflicts.Single().Choice = Choice.Theirs;
        File.WriteAllText(Path.Combine(t.Root, "a.txt"), text.Build());
        Assert.True((await repo.MarkResolvedAsync("a.txt")).Ok);
        // 지운 쪽(상대)을 따름 - 파일 삭제
        Assert.True((await repo.TakeWholeAsync(gone, ours: false)).Ok);

        Assert.DoesNotContain((await repo.StatusAsync()).Files, f => f.IsConflict);
        var r = await repo.ContinueAsync(Operation.Merge);
        Assert.True(r.Ok, r.Message);
        Assert.Equal(Operation.None, await repo.OperationAsync());
        Assert.Equal("1\n옆\n3\n", File.ReadAllText(Path.Combine(t.Root, "a.txt")).Replace("\r", ""));
        Assert.False(File.Exists(Path.Combine(t.Root, "gone.txt")));
    }

    [Fact]
    public async Task 리베이스_충돌_중단()
    {
        var (t, repo) = await Diverged();
        using var _ = t;
        await t.GitAsync("checkout", "-q", "옆");
        Assert.False((await repo.RebaseAsync("main")).Ok);
        Assert.Equal(Operation.Rebase, await repo.OperationAsync());
        Assert.Equal((1, 1), await repo.RebaseStepAsync());

        Assert.True((await repo.AbortAsync(Operation.Rebase)).Ok);
        Assert.Equal(Operation.None, await repo.OperationAsync());
        Assert.Equal("옆", await repo.BranchAsync());
    }
}

public class MergeCommitTests
{
    [Fact]
    public async Task 병합_중_커밋은_전체_커밋()
    {
        using var t = await TempRepo.CreateAsync();
        t.Write("a.txt", "1\n");
        await t.GitAsync("add", ".");
        await t.GitAsync("commit", "-q", "-m", "첫");
        await t.GitAsync("checkout", "-q", "-b", "옆");
        t.Write("a.txt", "옆\n");
        await t.GitAsync("commit", "-q", "-am", "옆");
        await t.GitAsync("checkout", "-q", "main");
        t.Write("a.txt", "메인\n");
        await t.GitAsync("commit", "-q", "-am", "메인");
        var repo = Repository.Discover(t.Root)!;
        await repo.MergeAsync("옆", false);

        t.Write("a.txt", "합침\n");
        var files = (await repo.StatusAsync()).Files;
        var r = await repo.CommitAsync("Merge 옆", files, amend: false);

        Assert.True(r.Ok, r.Message);
        Assert.Equal(Operation.None, await repo.OperationAsync());
        Assert.Equal(2, (await t.GitAsync("log", "-1", "--format=%P")).Output.Trim().Split(' ').Length);
    }
}
