using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace IeumMock;

// 팔레트 전환 동그라미 - 강조색 + 두 번째 레인 색
public sealed class PaletteChip
{
    public required string Name { get; init; }
    public required int Index { get; init; }
    public required IBrush A { get; init; }
    public required IBrush B { get; init; }
    public required IBrush Back { get; init; }
}

public partial class MockWindow
{
    public List<PaletteChip> Palettes { get; } = Tones.For(Program.Tone).Select((p, i) => new PaletteChip
    {
        Name = p.Name, Index = i,
        A = new SolidColorBrush(Color.Parse(p.Accent)), B = new SolidColorBrush(Color.Parse(p.G[1])), Back = new SolidColorBrush(Color.Parse(p.Bg)),
    }).ToList();

    public void OnPalette(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int i }) return;
        Tones.Apply(Application.Current!, Program.Tone, i);
        PaletteName = Tones.For(Program.Tone)[i].Name;
        // 레인 색을 바인딩 시점에 읽는 항목들 - 다시 그리기
        if (this.FindControl<ListBox>("List") is { } l) { var s = l.SelectedIndex; l.ItemsSource = null; l.ItemsSource = Commits; l.SelectedIndex = s; }
        if (this.FindControl<ListBox>("FileList") is { } f) { var s = f.SelectedIndex; f.ItemsSource = null; f.ItemsSource = Files; f.SelectedIndex = s; }
        var cur = Sel; Sel = null; Sel = cur;
    }
}
