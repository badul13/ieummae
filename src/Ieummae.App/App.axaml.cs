using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Ieummae.App.Theme;
using Ieummae.App.Windows;
using Ieummae.Core;

namespace Ieummae.App;

public partial class App : Application
{
    public static CommandLine Command { get; set; } = CommandLine.Parse([], Environment.CurrentDirectory);

    // 기본 글꼴 - Jua·Gaegu 에 없는 글자(… 등)는 Pretendard 로
    const string Pretendard = "avares://ieummae/Assets/Fonts#Pretendard";
    public static readonly FontManagerOptions Fonts = new()
    {
        DefaultFamilyName = Pretendard,
        FontFallbacks = [new FontFallback { FontFamily = new FontFamily(Pretendard) }],
    };

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // 테마 - 인자 지정 없으면 Windows 앱 테마 따라감
        var system = PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;
        // 인자 > 설정 > Windows 테마
        bool? saved = Platform.Settings.Theme switch { "dark" => true, "light" => false, _ => null };
        Tone.Apply(this, Command.Dark ?? saved ?? system);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = Launcher.Create(Command);
        base.OnFrameworkInitializationCompleted();
    }
}
