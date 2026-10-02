using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Ieummae.App.Theme;

// 단추 그리기 공통 - 눈 단추·그래프 단추·버튼 앞 단추·테마 단추
public static class Sew
{
    // X 자 실 + 구멍 넷
    public static void Cross(DrawingContext ctx, Point c, double hd, IBrush thread, double threadW, IBrush hole, double holeR)
    {
        var pen = new Pen(thread, threadW, lineCap: PenLineCap.Round);
        ctx.DrawLine(pen, new Point(c.X - hd, c.Y - hd), new Point(c.X + hd, c.Y + hd));
        ctx.DrawLine(pen, new Point(c.X + hd, c.Y - hd), new Point(c.X - hd, c.Y + hd));
        foreach (var (dx, dy) in new[] { (-hd, -hd), (hd, -hd), (-hd, hd), (hd, hd) })
            ctx.DrawEllipse(hole, null, new Point(c.X + dx, c.Y + dy), holeR, holeR);
    }

    // 레인 색 단추 - 그림자, 진한 테두리, 안쪽 홈, 빛 반사, X 자 실
    // 다크는 레인 색이 밝은 파스텔이라 단추 면만 한 톤 낮춰 밝은 실 대비 확보
    public static void LaneButton(DrawingContext ctx, Point c, double rr, int lane)
    {
        var col = Tone.ColorOf("G" + (lane % 7));
        double k = Tone.IsDark ? 0.8 : 1;
        var face = new SolidColorBrush(Scale(col, k));
        var dark = new SolidColorBrush(Scale(col, 0.68));
        ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), null, new Point(c.X, c.Y + 1.2), rr, rr);
        ctx.DrawEllipse(face, new Pen(dark, 1.2), c, rr, rr);
        ctx.DrawEllipse(null, new Pen(dark, 0.9), c, rr - rr * 0.32, rr - rr * 0.32);
        using (ctx.PushOpacity(0.45))
        {
            var arc = new StreamGeometry();
            using (var g = arc.Open())
            {
                g.BeginFigure(new Point(c.X - rr + 2, c.Y - 1), false);
                g.ArcTo(new Point(c.X - 1, c.Y - rr + 2), new Size(rr - 2, rr - 2), 0, false, SweepDirection.Clockwise);
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, new Pen(Brushes.White, 1.3, lineCap: PenLineCap.Round), arc);
        }
        Cross(ctx, c, rr * 0.25, Tone.Brush("Thread"), 1.2, dark, rr * 0.125);
    }

    static Color Scale(Color c, double k) => Color.FromRgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));
}

// 버튼 글자 앞 작은 단추
public sealed class SewDot : Control
{
    public static readonly StyledProperty<int> LaneProperty = AvaloniaProperty.Register<SewDot, int>(nameof(Lane));
    public int Lane { get => GetValue(LaneProperty); set => SetValue(LaneProperty, value); }

    static SewDot() => AffectsRender<SewDot>(LaneProperty);
    public SewDot() => Tone.Redraw(this);

    protected override Size MeasureOverride(Size available) => new(16, 16);

    public override void Render(DrawingContext ctx) => Sew.LaneButton(ctx, new Point(8, 8), 6.5, Lane);
}

// 테마 단추 - 흰 단추(라이트) / 숯색 단추(다크), 지금 테마 단추만 크게 + X 자 실
// 꺼진 단추도 흐리게 하지 않음 (흐리면 바탕이 비쳐 탁해짐)
public sealed class ThemeButton : Control
{
    public static readonly StyledProperty<bool> DarkProperty = AvaloniaProperty.Register<ThemeButton, bool>(nameof(Dark));
    public bool Dark { get => GetValue(DarkProperty); set => SetValue(DarkProperty, value); }

    static ThemeButton() => AffectsRender<ThemeButton>(DarkProperty);
    public ThemeButton() => Tone.Redraw(this);

    protected override Size MeasureOverride(Size available) => new(26, 26);

    public override void Render(DrawingContext ctx)
    {
        bool on = Tone.IsDark == Dark;
        var c = new Point(13, 12.5);
        double rr = on ? 10 : 8.5;
        var rim = new SolidColorBrush(Color.Parse(Dark ? "#1C1816" : "#BFAF98"));
        ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), null, new Point(c.X, c.Y + 1.4), rr, rr);
        ctx.DrawEllipse(new SolidColorBrush(Color.Parse(Dark ? "#3A332E" : "#FFFFFF")), new Pen(rim, 1.4), c, rr, rr);
        ctx.DrawEllipse(null, new Pen(rim, 1), c, rr - 3, rr - 3);
        IBrush thread = on ? new SolidColorBrush(Color.Parse(Dark ? "#EDE6DA" : "#6B4B3D")) : Brushes.Transparent;
        Sew.Cross(ctx, c, on ? 2.6 : 2.2, thread, 1.6, rim, 1.1);
    }
}
