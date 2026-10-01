using Avalonia.Controls;
using Avalonia.Interactivity;

namespace IeumMock;

public partial class WoolTop : UserControl
{
    public WoolTop() => InitializeComponent();

    public void OnPalette(object? sender, RoutedEventArgs e) => (TopLevel.GetTopLevel(this) as MockWindow)?.OnPalette(sender, e);
}
