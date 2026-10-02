using System.Diagnostics;
using System.Text;

namespace Ieummae.Core.Git;

// git 실행 결과 - 종료 코드, 표준 출력·오류 (UTF-8)
public sealed record GitResult(int ExitCode, string Output, string Error)
{
    public bool Ok => ExitCode == 0;
}

// git CLI 실행기 - 설치된 Git for Windows 를 창 없는 프로세스로 호출
public static class Git
{
    // 공통 옵션 - 경로 이스케이프 끔 (한글 파일명 그대로), 색 끔, 로그 출력 UTF-8
    static readonly string[] Common = ["-c", "core.quotepath=false", "-c", "color.ui=never", "-c", "i18n.logOutputEncoding=UTF-8"];
    static readonly Encoding Utf8 = new UTF8Encoding(false);

    // 실행 파일 - 기본은 PATH 의 git, 테스트·설정에서 교체 가능
    public static string Exe { get; set; } = "git";

    public static Task<GitResult> RunAsync(string dir, params string[] args) => RunAsync(dir, args, CancellationToken.None);

    public static async Task<GitResult> RunAsync(string dir, IEnumerable<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(Exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(dir);
        foreach (var a in Common.Concat(args)) psi.ArgumentList.Add(a);
        // 읽기 명령이 index 잠금을 잡지 않게 - 탐색기 쪽 상태 조회와 충돌 방지
        psi.Environment["GIT_OPTIONAL_LOCKS"] = "0";

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("git 실행 실패");
        // 출력·오류 동시 읽기 - 한쪽 버퍼가 차서 멈추는 것 방지
        var output = p.StandardOutput.ReadToEndAsync(ct);
        var error = p.StandardError.ReadToEndAsync(ct);
        try
        {
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
        return new GitResult(p.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }
}
