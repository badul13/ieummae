using Avalonia;
using Avalonia.Media;
using Ieummae.Core;

namespace Ieummae.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        App.Command = CommandLine.Parse(args, Environment.CurrentDirectory);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // 소프트웨어 렌더링 고정 - GPU 렌더링은 메모리 약 80MB 추가 (docs/STACK.md)
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] })
        .With(App.Fonts);
}
