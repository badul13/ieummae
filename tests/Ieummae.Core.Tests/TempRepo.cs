using Ieummae.Core.Git;

namespace Ieummae.Core.Tests;

// 임시 저장소 - 테스트마다 새로 만들고 끝나면 삭제
public sealed class TempRepo : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "ieummae-test-" + Guid.NewGuid().ToString("N")[..8]);

    public static async Task<TempRepo> CreateAsync()
    {
        var t = new TempRepo();
        Directory.CreateDirectory(t.Root);
        await t.GitAsync("init", "-q", "-b", "main");
        // 사용자 전역 설정과 무관하게 - 저장소 단위 이름·메일
        await t.GitAsync("config", "user.name", "tester");
        await t.GitAsync("config", "user.email", "tester@example.com");
        await t.GitAsync("config", "commit.gpgsign", "false");
        return t;
    }

    public async Task<GitResult> GitAsync(params string[] args)
    {
        var r = await Git.Git.RunAsync(Root, args);
        Assert.True(r.Ok, $"git {string.Join(' ', args)} 실패: {r.Error}");
        return r;
    }

    public void Write(string path, string text)
    {
        var full = Path.Combine(Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    public void Dispose()
    {
        // .git 객체 파일은 읽기 전용 - 속성 풀고 삭제
        foreach (var f in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(Root, recursive: true);
    }
}
