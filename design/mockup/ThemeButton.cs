using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace IeumMock;

// 테마 단추 - 상아 단추(라이트) / 숯색 단추(다크), 지금 테마 단추만 X 자 실로 꿰맴
public sealed class ThemeButton : Control
{
    public static readonly StyledProperty<int> PalProperty = AvaloniaProperty.Register<ThemeButton, int>(nameof(Pal));
    public int Pal { get => GetValue(PalProperty); set => SetValue(PalProperty, value); }

    static ThemeButton() => AffectsRender<ThemeButton>(PalProperty);

    protected override Size MeasureOverride(Size available) => new(26, 26);

    public override void Render(DrawingContext ctx)
    {
        bool dark = Tones.All[Pal].Dark;
        bool on = Tones.Current == Pal;
        var c = new Point(13, 12.5);
        double rr = on ? 10 : 8.5;
        var face = Color.Parse(dark ? "#3A332E" : "#FFFFFF");
        var rim = Color.Parse(dark ? "#1C1816" : "#BFAF98");
        var thread = Color.Parse(dark ? "#EDE6DA" : "#6B4B3D");

        // 꺼진 단추도 흐리게 하지 않음 (흐리면 바탕이 비쳐 탁해짐) - 크기·실로만 구분
        {
            ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), null, new Point(c.X, c.Y + 1.4), rr, rr);
            ctx.DrawEllipse(new SolidColorBrush(face), new Pen(new SolidColorBrush(rim), 1.4), c, rr, rr);
            ctx.DrawEllipse(null, new Pen(new SolidColorBrush(rim), 1), c, rr - 3, rr - 3);
            double hd = on ? 2.6 : 2.2;
            // 꿰맨 단추만 실, 안 꿰맨 단추는 구멍 넷만
            if (on)
            {
                var pen = new Pen(new SolidColorBrush(thread), 1.6, lineCap: PenLineCap.Round);
                ctx.DrawLine(pen, new Point(c.X - hd, c.Y - hd), new Point(c.X + hd, c.Y + hd));
                ctx.DrawLine(pen, new Point(c.X + hd, c.Y - hd), new Point(c.X - hd, c.Y + hd));
            }
            foreach (var (dx, dy) in new[] { (-hd, -hd), (hd, -hd), (-hd, hd), (hd, hd) })
                ctx.DrawEllipse(new SolidColorBrush(rim), null, new Point(c.X + dx, c.Y + dy), 1.1, 1.1);
        }
    }
}
