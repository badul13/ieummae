using Ieummae.Core.Git;

namespace Ieummae.Core.Tests;

public class BlameTests
{
    [Fact]
    public async Task 줄마다_마지막_수정_커밋()
    {
        using var t = await TempRepo.CreateAsync();
        t.Write("a.txt", "하나\n둘\n");
        await t.GitAsync("add", ".");
        await t.GitAsync("commit", "-q", "-m", "처음");
        t.Write("a.txt", "하나\n둘 고침\n셋\n");
        await t.GitAsync("commit", "-q", "-am", "고침");
        t.Write("a.txt", "하나\n둘 고침\n셋\n넷\n");
        var repo = Repository.Discover(t.Root)!;

        var lines = await repo.BlameAsync("a.txt");

        Assert.Equal(["처음", "고침", "고침"], lines.Take(3).Select(l => l.Summary));
        Assert.Equal([1, 2, 3, 4], lines.Select(l => l.Number));
        Assert.Equal("둘 고침", lines[1].Text);
        Assert.Equal("tester", lines[0].Author);
        // 커밋 안 한 줄
        Assert.True(lines[3].Uncommitted);
    }
}

public class GitToolsTests
{
    [Theory]
    [InlineData("https://github.com/badul13/ieummae.git", "ieummae")]
    [InlineData("git@github.com:user/repo.git", "repo")]
    [InlineData(@"C:\work\local-repo\", "local-repo")]
    public void 주소에서_폴더_이름(string url, string folder) => Assert.Equal(folder, GitTools.FolderFromUrl(url));

    [Fact]
    public async Task 새로_만들고_복제하고_설정()
    {
        var parent = Directory.CreateTempSubdirectory("ieummae-test-").FullName;
        try
        {
            var src = Path.Combine(parent, "원본");
            Directory.CreateDirectory(src);
            Assert.True((await GitTools.InitAsync(src)).Ok);
            Assert.NotNull(Repository.Discover(src));
            Assert.True((await GitTools.SetConfigAsync(src, "--local", "user.name", "양털")).Ok);
            await Git.Git.RunAsync(src, "config", "user.email", "w@x");
            File.WriteAllText(Path.Combine(src, "a.txt"), "1");
            await Git.Git.RunAsync(src, "add", ".");
            await Git.Git.RunAsync(src, "commit", "-q", "-m", "첫");
            Assert.Equal("양털", await GitTools.GetConfigAsync(src, "--local", "user.name"));
            Assert.True((await GitTools.SetConfigAsync(src, "--local", "user.name", "")).Ok);
            Assert.Null(await GitTools.GetConfigAsync(src, "--local", "user.name"));

            var lines = new List<string>();
            var r = await GitTools.CloneAsync(parent, src, "복제", lines.Add);
            Assert.True(r.Ok, r.Message);
            Assert.True(File.Exists(Path.Combine(parent, "복제", "a.txt")));
        }
        finally
        {
            foreach (var f in Directory.EnumerateFiles(parent, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(parent, true);
        }
    }
}
