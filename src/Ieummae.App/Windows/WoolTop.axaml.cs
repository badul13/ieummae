using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Ieummae.App.Theme;

namespace Ieummae.App.Windows;

public partial class WoolTop : UserControl
{
    public WoolTop() => InitializeComponent();

    Window? Host => TopLevel.GetTopLevel(this) as Window;

    // 고른 테마는 다음 창에도 - 설정에 저장
    void OnLight(object? sender, RoutedEventArgs e) { Tone.Apply(Application.Current!, false); Platform.Settings.Theme = "light"; }
    void OnDark(object? sender, RoutedEventArgs e) { Tone.Apply(Application.Current!, true); Platform.Settings.Theme = "dark"; }

    void OnMinimize(object? sender, RoutedEventArgs e) { if (Host is { } w) w.WindowState = WindowState.Minimized; }
    void OnMaximize(object? sender, RoutedEventArgs e)
    {
        if (Host is { } w) w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
    void OnClose(object? sender, RoutedEventArgs e) => Host?.Close();
}
