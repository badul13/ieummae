using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Ieummae.Core.Git;

// git 실행 결과 - 종료 코드, 표준 출력·오류 (UTF-8)
public sealed record GitResult(int ExitCode, string Output, string Error)
{
    public bool Ok => ExitCode == 0;

    // 화면용 오류 문구 - 오류 출력 우선, 없으면 표준 출력
    public string Message => (Error.Trim().Length > 0 ? Error : Output).Trim();
}

// 실행 선택 사항 - 표준 입력, 진행 줄 받기 (--progress 출력은 \r 로 갱신되므로 \r 도 줄 끝으로)
public sealed class GitRunOptions
{
    public string? Input { get; init; }
    public Action<string>? OnErrorLine { get; init; }
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
}

// git CLI 실행기 - 설치된 Git for Windows 를 창 없는 프로세스로 호출
public static class Git
{
    // 공통 옵션 - 경로 이스케이프 끔 (한글 파일명 그대로), 색 끔, 로그 출력 UTF-8
    static readonly string[] Common = ["-c", "core.quotepath=false", "-c", "color.ui=never", "-c", "i18n.logOutputEncoding=UTF-8"];
    static readonly Encoding Utf8 = new UTF8Encoding(false);

    // 실행 파일 - 기본은 PATH 의 git, 테스트·설정에서 교체 가능
    public static string Exe { get; set; } = "git";

    public static Task<GitResult> RunAsync(string dir, params string[] args) => RunAsync(dir, args, null, CancellationToken.None);

    public static Task<GitResult> RunAsync(string dir, IEnumerable<string> args, CancellationToken ct) => RunAsync(dir, args, null, ct);

    public static async Task<GitResult> RunAsync(string dir, IEnumerable<string> args, GitRunOptions? opt, CancellationToken ct)
    {
        using var p = Start(dir, args, opt);
        // 출력·오류 동시 읽기 - 한쪽 버퍼가 차서 멈추는 것 방지
        var output = p.StandardOutput.ReadToEndAsync(ct);
        var error = opt?.OnErrorLine is { } onLine ? ReadLinesAsync(p.StandardError, onLine, ct) : p.StandardError.ReadToEndAsync(ct);
        if (opt?.Input is { } input)
        {
            await p.StandardInput.WriteAsync(input.AsMemory(), ct).ConfigureAwait(false);
            p.StandardInput.Close();
        }
        try
        {
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(p);
            throw;
        }
        return new GitResult(p.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }

    // 출력 한 줄씩 - 큰 로그를 다 기다리지 않고 받는 대로 처리
    public static async IAsyncEnumerable<string> StreamLinesAsync(string dir, IEnumerable<string> args, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var p = Start(dir, args, null);
        var error = p.StandardError.ReadToEndAsync(ct);
        try
        {
            while (await p.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
                yield return line;
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            if (!p.HasExited) Kill(p);
        }
        var err = await error.ConfigureAwait(false);
        if (p.ExitCode != 0) throw new GitException(p.ExitCode, err);
    }

    static Process Start(string dir, IEnumerable<string> args, GitRunOptions? opt)
    {
        var psi = new ProcessStartInfo(Exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = opt?.Input is not null,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
            StandardInputEncoding = opt?.Input is not null ? Utf8 : null,
        };
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(dir);
        foreach (var a in Common.Concat(args)) psi.ArgumentList.Add(a);
        // 읽기 명령이 index 잠금을 잡지 않게 - 탐색기 쪽 상태 조회와 충돌 방지
        psi.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        // 터미널 입력 대기 금지 - 인증은 Git Credential Manager 창으로만
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        if (opt?.Environment is { } env)
            foreach (var (k, v) in env) psi.Environment[k] = v;
        return Process.Start(psi) ?? throw new InvalidOperationException("git 실행 실패");
    }

    // \r, \n 어느 쪽이든 줄 끝 - 진행률은 같은 줄을 \r 로 덮어씀
    static async Task<string> ReadLinesAsync(StreamReader r, Action<string> onLine, CancellationToken ct)
    {
        var all = new StringBuilder();
        var line = new StringBuilder();
        var buf = new char[1024];
        int n;
        while ((n = await r.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false)) > 0)
        {
            all.Append(buf, 0, n);
            for (int i = 0; i < n; i++)
            {
                if (buf[i] is '\r' or '\n')
                {
                    if (line.Length > 0) onLine(line.ToString());
                    line.Clear();
                }
                else line.Append(buf[i]);
            }
        }
        if (line.Length > 0) onLine(line.ToString());
        return all.ToString();
    }

    static void Kill(Process p)
    {
        try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
    }
}

public sealed class GitException(int exitCode, string error) : Exception(error.Trim().Length > 0 ? error.Trim() : $"git 종료 코드 {exitCode}")
{
    public int ExitCode { get; } = exitCode;
}
