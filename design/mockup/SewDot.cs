using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace IeumMock;

// 작은 단추 - 버튼 글자 앞 장식, 그래프 단추와 같은 모양 (레인 색, 진한 테두리, X 자 실)
public sealed class SewDot : Control
{
    public static readonly StyledProperty<int> LaneProperty = AvaloniaProperty.Register<SewDot, int>(nameof(Lane));
    public int Lane { get => GetValue(LaneProperty); set => SetValue(LaneProperty, value); }

    static SewDot() => AffectsRender<SewDot>(LaneProperty);

    static object Res(string k) => Application.Current!.Resources[k]!;

    protected override Size MeasureOverride(Size available) => new(16, 16);

    public override void Render(DrawingContext ctx)
    {
        var col = (Color)Res("G" + (Lane % 7));
        double k = (bool)Res("IsDark") ? 0.8 : 1;
        var face = new SolidColorBrush(Color.FromRgb((byte)(col.R * k), (byte)(col.G * k), (byte)(col.B * k)));
        var dark = new SolidColorBrush(Color.FromRgb((byte)(col.R * 0.68), (byte)(col.G * 0.68), (byte)(col.B * 0.68)));
        var c = new Point(8, 8);
        const double rr = 6.5, hd = 1.7;
        ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), null, new Point(c.X, c.Y + 1), rr, rr);
        ctx.DrawEllipse(face, new Pen(dark, 1.1), c, rr, rr);
        ctx.DrawEllipse(null, new Pen(dark, 0.8), c, rr - 2.2, rr - 2.2);
        var thread = new Pen((IBrush)Res("Thread"), 1.1, lineCap: PenLineCap.Round);
        ctx.DrawLine(thread, new Point(c.X - hd, c.Y - hd), new Point(c.X + hd, c.Y + hd));
        ctx.DrawLine(thread, new Point(c.X + hd, c.Y - hd), new Point(c.X - hd, c.Y + hd));
        foreach (var (dx, dy) in new[] { (-hd, -hd), (hd, -hd), (-hd, hd), (hd, hd) })
            ctx.DrawEllipse(dark, null, new Point(c.X + dx, c.Y + dy), 0.85, 0.85);
    }
}
