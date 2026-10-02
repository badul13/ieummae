using Ieummae.Core.Diff;
using Ieummae.Core.Git;
using Ieummae.Core.Log;

namespace Ieummae.Core.Tests;

public class LogParseTests
{
    [Fact]
    public void 장식_해석()
    {
        var e = LogReader.Parse(string.Join('\u001f', "abc", "p1 p2", "양털", "w@x", "1700000000",
            "HEAD -> refs/heads/main, tag: refs/tags/v1, refs/remotes/origin/main, refs/remotes/origin/HEAD", "병합"))!;

        Assert.True(e.IsMerge);
        Assert.True(e.IsHead);
        Assert.Equal([new RefLabel("main", RefKind.Head), new RefLabel("v1", RefKind.Tag), new RefLabel("origin/main", RefKind.Remote)], e.Refs);
    }

    [Fact]
    public void 분리된_HEAD()
    {
        var e = LogReader.Parse(string.Join('\u001f', "abc", "", "a", "b", "0", "HEAD", "s"))!;
        Assert.True(e.DetachedHead);
        Assert.Empty(e.Refs);
    }
}

public class GraphBuilderTests
{
    static LogEntry E(string h, params string[] p) => new() { Hash = h, Parents = p, Author = "", Email = "", Time = default, Subject = h };

    [Fact]
    public void 갈라졌다_합쳐지는_레인()
    {
        // M(병합) ← A ← R,  M ← B ← R   (위상 순서: M, B, A, R)
        var g = new GraphBuilder();
        var m = g.Add(E("M", "A", "B"));
        var b = g.Add(E("B", "R"));
        var a = g.Add(E("A", "R"));
        var r = g.Add(E("R"));

        Assert.Equal(0, m.Dot);
        Assert.Equal([0, 1], m.Down.Select(x => x.Lane));
        Assert.Equal(1, b.Dot);
        Assert.Equal([0], b.Pass.Select(x => x.Lane));
        Assert.Equal(0, a.Dot);
        // R 은 두 레인이 모이는 점 - 위에서 두 선이 들어옴
        Assert.Equal(0, r.Dot);
        Assert.Equal([0, 1], r.Up.Select(x => x.Lane));
        Assert.Empty(r.Down);
        // 첫 부모 선은 색 유지, 갈라진 선은 새 색
        Assert.Equal(m.DotColor, a.DotColor);
        Assert.NotEqual(m.DotColor, b.DotColor);
    }

    [Fact]
    public void 끝난_레인은_다시_씀()
    {
        var g = new GraphBuilder();
        g.Add(E("X", "R"));
        g.Add(E("Y", "Z"));
        g.Add(E("R"));
        var z = g.Add(E("Z"));
        Assert.Equal(1, z.Dot);
        var w = g.Add(E("W"));
        Assert.Equal(0, w.Dot);
    }
}

public class ChangesTests
{
    [Fact]
    public void 이름_바꾸기와_바이너리()
    {
        var files = Repository.ParseChanges(
            "M\0a.txt\0R090\0old.txt\0new.txt\0A\0img.png\0",
            "1\t2\ta.txt\0" + "3\t0\t\0old.txt\0new.txt\0" + "-\t-\timg.png\0");

        Assert.Equal(3, files.Count);
        Assert.Equal((FileStatus.Modified, 1, 2), (files[0].Status, files[0].Added, files[0].Deleted));
        Assert.Equal((FileStatus.Renamed, "new.txt", "old.txt", 3), (files[1].Status, files[1].Path, files[1].OldPath, files[1].Added));
        Assert.True(files[2].Binary);
    }
}

public class LogRepositoryTests
{
    static async Task<string> Commit(TempRepo t, string file, string text, string msg)
    {
        t.Write(file, text);
        await t.GitAsync("add", "-A");
        await t.GitAsync("commit", "-q", "-m", msg);
        return (await t.GitAsync("rev-parse", "HEAD")).Output.Trim();
    }

    [Fact]
    public async Task 스트리밍_로그와_경로_한정()
    {
        using var t = await TempRepo.CreateAsync();
        await Commit(t, "a.txt", "1", "첫");
        await Commit(t, "b/c.txt", "2", "둘");
        await Commit(t, "a.txt", "3", "셋");
        var repo = Repository.Discover(t.Root)!;

        var all = await LogReader.ReadAsync(repo, new LogQuery()).ToListAsync();
        Assert.Equal(["셋", "둘", "첫"], all.Select(e => e.Subject));
        Assert.Contains(all[0].Refs, r => r is { Name: "main", Kind: RefKind.Head });

        var onlyA = await LogReader.ReadAsync(repo, new LogQuery(Path: "a.txt")).ToListAsync();
        Assert.Equal(["셋", "첫"], onlyA.Select(e => e.Subject));
        // 경로 한정이면 부모도 그 경로 기준으로 이어짐
        Assert.Equal([onlyA[1].Hash], onlyA[0].Parents);
    }

    [Fact]
    public async Task 브랜치_태그_리셋_되돌리기_체리픽()
    {
        using var t = await TempRepo.CreateAsync();
        var c1 = await Commit(t, "a.txt", "1\n", "첫");
        var c2 = await Commit(t, "a.txt", "1\n2\n", "둘");
        var repo = Repository.Discover(t.Root)!;

        Assert.True((await repo.CreateBranchAsync("옆", c1, checkout: false)).Ok);
        Assert.True((await repo.CreateTagAsync("v1", c2, "첫 태그")).Ok);
        Assert.True((await repo.RevertAsync(c2, merge: false)).Ok);
        Assert.Equal("1\n", File.ReadAllText(Path.Combine(t.Root, "a.txt")).Replace("\r", ""));

        Assert.True((await repo.CheckoutAsync("옆")).Ok);
        Assert.True((await repo.CherryPickAsync(c2, merge: false)).Ok);
        Assert.Equal("옆", await repo.BranchAsync());
        Assert.Equal("1\n2\n", File.ReadAllText(Path.Combine(t.Root, "a.txt")).Replace("\r", ""));

        Assert.True((await repo.ResetAsync(ResetMode.Hard, c1)).Ok);
        Assert.Equal("1\n", File.ReadAllText(Path.Combine(t.Root, "a.txt")).Replace("\r", ""));

        var info = await repo.ShowAsync(c2);
        Assert.Equal("둘", info.Subject);
        var files = await repo.ChangedFilesAsync(c2, c1);
        Assert.Equal(("a.txt", 1, 0), (Assert.Single(files).Path, files[0].Added, files[0].Deleted));
    }

    [Fact]
    public async Task 실패한_동작은_메시지를_돌려준다()
    {
        using var t = await TempRepo.CreateAsync();
        await Commit(t, "a.txt", "1", "첫");
        var repo = Repository.Discover(t.Root)!;

        var r = await repo.CreateBranchAsync("main", "HEAD", checkout: false);
        Assert.False(r.Ok);
        Assert.Contains("main", r.Message);
    }
}

public class ReflowTests
{
    [Fact]
    public void 문단은_잇고_목록_트레일러는_유지()
    {
        var body = "Both reference rename and copy have\nbeen refactored to use the API.\n\n* first item\n  continues here\n- second\n\nSigned-off-by: A <a@x>\nSigned-off-by: B <b@x>";

        Assert.Equal(
            "Both reference rename and copy have been refactored to use the API.\n\n* first item\n  continues here\n- second\n\nSigned-off-by: A <a@x>\nSigned-off-by: B <b@x>",
            Reflow.Body(body));
    }
}
