namespace Ieummae.Core.Tests;

public class CommandLineTests
{
    const string Cwd = @"C:\work\repo";

    [Fact]
    public void 인자가_없으면_현재_폴더_로그()
    {
        var c = CommandLine.Parse([], Cwd);

        Assert.Equal("log", c.Name);
        Assert.Equal(Cwd, c.Path);
        Assert.Null(c.Dark);
    }

    [Fact]
    public void 명령과_상대_경로()
    {
        var c = CommandLine.Parse(["Commit", @"sub\dir"], Cwd);

        Assert.Equal("commit", c.Name);
        Assert.Equal(@"C:\work\repo\sub\dir", c.Path);
    }

    [Fact]
    public void 옵션은_위치와_무관()
    {
        var c = CommandLine.Parse(["--theme", "dark", "log", @"D:\x", "--bench-id", "a1"], Cwd);

        Assert.Equal("log", c.Name);
        Assert.Equal(@"D:\x", c.Path);
        Assert.True(c.Dark);
        Assert.Equal("a1", c.BenchId);
    }

    [Fact]
    public void 값_없는_옵션과_모르는_테마는_무시()
    {
        Assert.Null(CommandLine.Parse(["--theme", "blue"], Cwd).Dark);

        var c = CommandLine.Parse(["log", "--theme"], Cwd);
        Assert.Equal("log", c.Name);
        Assert.Equal(Cwd, c.Path);
    }
}
