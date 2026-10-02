namespace Ieummae.Core;

// 실행 인자 - ieummae <명령> [경로] [--theme light|dark] [--bench-id X]
// 명령 생략 시 log, 경로 생략 시 현재 폴더. 탐색기 메뉴는 항상 명령·경로를 넘김
public sealed record CommandLine(string Name, string Path, bool? Dark = null, string? BenchId = null)
{
    public const string DefaultName = "log";

    public static CommandLine Parse(IReadOnlyList<string> args, string currentDir)
    {
        var plain = new List<string>();
        bool? dark = null;
        string? bench = null;
        for (int i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                // 다음 값이 없으면 옵션 무시
                case "--theme" when i + 1 < args.Count:
                    dark = args[++i].ToLowerInvariant() switch { "dark" => true, "light" => false, _ => null };
                    break;
                // 측정 도구용 - 창 제목을 ieummae-<id> 로 고정
                case "--bench-id" when i + 1 < args.Count:
                    bench = args[++i];
                    break;
                // 모르는 옵션·값 빠진 옵션 - 무시
                case var a when a.StartsWith("--"):
                    break;
                default:
                    plain.Add(args[i]);
                    break;
            }
        }
        var name = plain.Count > 0 ? plain[0].ToLowerInvariant() : DefaultName;
        var path = plain.Count > 1 ? System.IO.Path.GetFullPath(plain[1], currentDir) : currentDir;
        return new CommandLine(name, path, dark, bench);
    }
}
