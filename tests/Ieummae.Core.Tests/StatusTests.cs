using Ieummae.Core.Git;

namespace Ieummae.Core.Tests;

public class StatusParseTests
{
    [Fact]
    public void 브랜치_줄과_파일_종류()
    {
        var s = Repository.ParseStatus(string.Join('\0',
            "# branch.oid abc", "# branch.head main", "# branch.upstream origin/main", "# branch.ab +2 -1",
            "1 .M N... 100644 100644 100644 a b src/a b.txt",
            "1 A. N... 000000 100644 100644 0 a new.txt",
            "2 R. N... 100644 100644 100644 a a R100 새이름.txt", "옛이름.txt",
            "u UU N... 100644 100644 100644 100644 a b c conflict.txt",
            "? 메모.txt", ""));

        Assert.Equal(("main", "origin/main", 2, 1), (s.Branch, s.Upstream, s.Ahead, s.Behind));
        Assert.Equal(["src/a b.txt", "new.txt", "새이름.txt", "conflict.txt", "메모.txt"], s.Files.Select(f => f.Path));
        Assert.Equal([ChangeKind.Modified, ChangeKind.Added, ChangeKind.Renamed, ChangeKind.Conflict, ChangeKind.Untracked], s.Files.Select(f => f.Kind));
        Assert.Equal("옛이름.txt", s.Files[2].OrigPath);
    }
}

public class CommitTests
{
    static async Task<(TempRepo, Repository)> Setup()
    {
        var t = await TempRepo.CreateAsync();
        t.Write("a.txt", "a\n");
        t.Write("b.txt", "b\n");
        t.Write("c.txt", "c\n");
        await t.GitAsync("add", ".");
        await t.GitAsync("commit", "-q", "-m", "첫");
        return (t, Repository.Discover(t.Root)!);
    }

    static async Task<string[]> Committed(TempRepo t) =>
        (await t.GitAsync("show", "--name-only", "--format=", "HEAD")).Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public async Task 고른_파일만_커밋하고_나머지_스테이징은_유지()
    {
        var (t, repo) = await Setup();
        using var _ = t;
        t.Write("a.txt", "a2\n");
        t.Write("b.txt", "b2\n");
        await t.GitAsync("add", "b.txt");
        t.Write("새 파일.txt", "n\n");

        var files = (await repo.StatusAsync()).Files;
        var pick = files.Where(f => f.Path is "a.txt" or "새 파일.txt").ToList();
        var r = await repo.CommitAsync("둘째\n\n본문", pick, amend: false);

        Assert.True(r.Ok, r.Message);
        Assert.Equal(["a.txt", "새 파일.txt"], (await Committed(t)).Order(StringComparer.Ordinal));
        // b.txt 는 여전히 스테이징된 채
        var left = (await repo.StatusAsync()).Files;
        Assert.Equal(("b.txt", 'M'), (Assert.Single(left).Path, left[0].Index));
        Assert.Equal("둘째\n\n본문", await repo.HeadMessageAsync());
    }

    [Fact]
    public async Task 이름_바꾸기와_삭제_커밋()
    {
        var (t, repo) = await Setup();
        using var _ = t;
        await t.GitAsync("mv", "a.txt", "aa.txt");
        File.Delete(Path.Combine(t.Root, "c.txt"));

        var files = (await repo.StatusAsync()).Files;
        Assert.Contains(files, f => f is { Kind: ChangeKind.Renamed, Path: "aa.txt", OrigPath: "a.txt" });
        Assert.True((await repo.CommitAsync("정리", files, amend: false)).Ok);
        Assert.Empty((await repo.StatusAsync()).Files);
    }

    [Fact]
    public async Task 메시지만_고치는_amend()
    {
        var (t, repo) = await Setup();
        using var _ = t;
        t.Write("b.txt", "b2\n");
        await t.GitAsync("add", "b.txt");

        Assert.True((await repo.CommitAsync("첫 커밋 다시", [], amend: true)).Ok);
        Assert.Equal("첫 커밋 다시", await repo.HeadMessageAsync());
        // 스테이징된 b.txt 는 amend 에 안 들어감
        Assert.Equal(["a.txt", "b.txt", "c.txt"], (await Committed(t)).Order(StringComparer.Ordinal));
        Assert.Equal('M', Assert.Single((await repo.StatusAsync()).Files).Index);
    }

    [Fact]
    public async Task 변경_되돌리기와_ignore()
    {
        var (t, repo) = await Setup();
        using var _ = t;
        t.Write("a.txt", "바뀜\n");
        t.Write("n.txt", "새\n");
        await t.GitAsync("add", "n.txt");
        t.Write("log.tmp", "x");

        var files = (await repo.StatusAsync()).Files;
        Assert.True((await repo.RevertFilesAsync(files.Where(f => !f.IsUntracked).ToList())).Ok);
        Assert.Equal("a\n", File.ReadAllText(Path.Combine(t.Root, "a.txt")).Replace("\r", ""));
        // 새로 스테이징했던 파일은 남고 추적 안 하는 상태로
        Assert.True(File.Exists(Path.Combine(t.Root, "n.txt")));

        repo.AddToIgnore("*.tmp");
        repo.AddToIgnore("*.tmp");
        Assert.Equal(["*.tmp"], File.ReadAllLines(Path.Combine(t.Root, ".gitignore")));
        var left = (await repo.StatusAsync()).Files.Select(f => f.Path).Order(StringComparer.Ordinal);
        Assert.Equal([".gitignore", "n.txt"], left);
    }
}
