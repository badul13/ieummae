using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Ieummae.App.Theme;

namespace Ieummae.App.Windows;

public partial class WoolTop : UserControl
{
    public WoolTop() => InitializeComponent();

    void OnLight(object? sender, RoutedEventArgs e) => Tone.Apply(Application.Current!, false);
    void OnDark(object? sender, RoutedEventArgs e) => Tone.Apply(Application.Current!, true);
}
