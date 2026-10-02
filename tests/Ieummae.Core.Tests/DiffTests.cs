using Ieummae.Core.Diff;
using Ieummae.Core.Git;

namespace Ieummae.Core.Tests;

public class DiffParserTests
{
    [Fact]
    public void 덩어리와_줄_번호()
    {
        var f = DiffParser.Parse("""
            diff --git a/a.txt b/a.txt
            index 1111111..2222222 100644
            --- a/a.txt
            +++ b/a.txt
            @@ -1,3 +1,3 @@ class A
             one
            -two
            +TWO
             three
            """.Replace("\r", ""))[0];

        var h = Assert.Single(f.Hunks);
        Assert.Equal("class A", h.Heading);
        Assert.Equal([LineKind.Context, LineKind.Del, LineKind.Add, LineKind.Context], h.Lines.Select(l => l.Kind));
        Assert.Equal((2, 0), (h.Lines[1].OldNo, h.Lines[1].NewNo));
        Assert.Equal((0, 2), (h.Lines[2].OldNo, h.Lines[2].NewNo));
        Assert.Equal((3, 3), (h.Lines[3].OldNo, h.Lines[3].NewNo));
        Assert.Equal((1, 1), (f.Added, f.Deleted));
    }

    [Fact]
    public void 이름_바꾸기_바이너리_새_파일()
    {
        var files = DiffParser.Parse("""
            diff --git a/old name.txt b/new name.txt
            similarity index 90%
            rename from old name.txt
            rename to new name.txt
            diff --git a/img.png b/img.png
            index 1..2 100644
            Binary files a/img.png and b/img.png differ
            diff --git a/n.txt b/n.txt
            new file mode 100644
            --- /dev/null
            +++ b/n.txt
            @@ -0,0 +1 @@
            +x
            \ No newline at end of file
            """.Replace("\r", ""));

        Assert.Equal(3, files.Count);
        Assert.Equal((FileStatus.Renamed, "old name.txt", "new name.txt", 90), (files[0].Status, files[0].OldPath, files[0].NewPath, files[0].Similarity));
        Assert.True(files[1].IsBinary);
        Assert.Equal(FileStatus.Added, files[2].Status);
        Assert.True(files[2].Hunks[0].Lines[0].NoNewlineAtEnd);
    }

    [Fact]
    public void 따옴표_경로_풀기()
    {
        Assert.Equal("a\tb.txt", DiffParser.Unquote("\"a\\tb.txt\""));
        // 8진수 바이트 = UTF-8 '한'
        Assert.Equal("한.txt", DiffParser.Unquote("\"\\355\\225\\234.txt\""));
    }
}

public class WordDiffTests
{
    static (DiffLine Del, DiffLine Add) Pair(string a, string b)
    {
        var lines = new List<DiffLine> { new(LineKind.Del, 1, 0, a), new(LineKind.Add, 0, 1, b) };
        WordDiff.Apply(lines);
        return (lines[0], lines[1]);
    }

    [Fact]
    public void 바뀐_단어만_표시()
    {
        var (del, add) = Pair("var count = items.Length;", "var count = items.Count;");

        Assert.Equal([(18, 6)], del.Changes);
        Assert.Equal([(18, 5)], add.Changes);
    }

    [Fact]
    public void 한글_단어()
    {
        var (del, add) = Pair("양털 단추 하나", "양털 실 하나");

        Assert.Equal("단추", del.Text.Substring(del.Changes[0].Start, del.Changes[0].Length));
        Assert.Equal("실", add.Text.Substring(add.Changes[0].Start, add.Changes[0].Length));
    }

    [Fact]
    public void 거의_다_바뀌면_강조_안_함()
    {
        var (del, add) = Pair("alpha beta gamma", "one two three");

        Assert.Empty(del.Changes);
        Assert.Empty(add.Changes);
    }
}

public class SplitRowsTests
{
    [Fact]
    public void 삭제_추가_묶음을_나란히()
    {
        var f = new FileDiff();
        f.Hunks.Add(new Hunk(1, 3, 1, 2, "", [
            new(LineKind.Context, 1, 1, "a"),
            new(LineKind.Del, 2, 0, "b"),
            new(LineKind.Del, 3, 0, "c"),
            new(LineKind.Add, 0, 2, "B"),
        ]));

        var rows = SplitRows.Build(f);

        Assert.NotNull(rows[0].Hunk);
        Assert.Equal(("a", "a"), (rows[1].Left!.Text, rows[1].Right!.Text));
        Assert.Equal(("b", "B"), (rows[2].Left!.Text, rows[2].Right!.Text));
        Assert.Equal("c", rows[3].Left!.Text);
        Assert.Null(rows[3].Right);
    }
}

public class RepositoryDiffTests
{
    [Fact]
    public async Task 작업_트리_첫_커밋_추적_안_하는_파일()
    {
        using var t = await TempRepo.CreateAsync();
        t.Write("한글.txt", "하나\n둘\n");
        await t.GitAsync("add", ".");
        await t.GitAsync("commit", "-q", "-m", "첫");
        var head = (await t.GitAsync("rev-parse", "HEAD")).Output.Trim();
        t.Write("한글.txt", "하나\n셋\n");
        t.Write("new.txt", "새 줄\n");
        var repo = Repository.Discover(t.Root)!;

        var work = Assert.Single(await repo.DiffAsync(new DiffSpec.WorkingTree(), []));
        Assert.Equal("한글.txt", work.Path);
        Assert.Equal((1, 1), (work.Added, work.Deleted));

        var root = Assert.Single(await repo.DiffAsync(new DiffSpec.Commit(head, null), []));
        Assert.Equal(FileStatus.Added, root.Status);
        Assert.Equal(2, root.Added);

        var untracked = Assert.Single(await repo.DiffAsync(new DiffSpec.Untracked(), ["new.txt"]));
        Assert.Equal(("new.txt", FileStatus.Added, 1), (untracked.Path, untracked.Status, untracked.Added));
    }

    [Fact]
    public async Task 커밋_없는_저장소도_작업_트리_비교()
    {
        using var t = await TempRepo.CreateAsync();
        t.Write("a.txt", "x\n");
        await t.GitAsync("add", ".");
        var repo = Repository.Discover(t.Root)!;

        Assert.False(await repo.HasHeadAsync());
        Assert.Equal("a.txt", Assert.Single(await repo.DiffAsync(new DiffSpec.WorkingTree(), [])).Path);
    }
}
