using System;
using Avalonia;
using Avalonia.Media;

namespace IeumMock;

// 시안 실행 - ieum-mock --repo 경로 --view log|commit --pal 0|1 --id X
internal static class Program
{
    public static string Id = "0";
    public static string Tone = "b";
    public static string View = "log";
    public static int Pal = 0;

    [STAThread]
    public static int Main(string[] args)
    {
        for (int i = 0; i + 1 < args.Length; i++)
        {
            switch (args[i])
            {
                case "--id": Id = args[++i]; break;
                case "--tone": Tone = args[++i]; break;
                case "--view": View = args[++i]; break;
                case "--pal": Pal = int.Parse(args[++i]); break;
                case "--repo": GitData.Repo = args[++i]; break;
            }
        }
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions { RenderingMode = new[] { Win32RenderingMode.Software } })
            // 고정폭·도트 글꼴에 없는 한글 - Pretendard 로 대체
            .With(new FontManagerOptions
            {
                DefaultFamilyName = "avares://ieum-mock/Assets/Fonts#Pretendard",
                FontFallbacks = new[] { new FontFallback { FontFamily = new Avalonia.Media.FontFamily("avares://ieum-mock/Assets/Fonts#Pretendard") } },
            })
            .StartWithClassicDesktopLifetime(args);
    }
}
