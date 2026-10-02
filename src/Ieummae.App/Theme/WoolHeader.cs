using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Ieummae.App.Theme;

// 창 윗부분 - 봉제 인형 정면: 양털 덩어리, 단추 눈, 처진 펠트 귀, 분홍 코·고양이 입
public sealed class WoolHeader : Control
{
    public WoolHeader() => Tone.Redraw(this);

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        const double bump = 14;
        double baseY = h - bump;
        var wool = Tone.Brush("Wool");
        var woolLine = Tone.ColorOf("WoolLine");

        // 양털 몸통 + 구름 가장자리, 다크는 양털 결 색이 양털과 비슷해 바탕보다 진한 그림자로
        var shade = Tone.IsDark ? new SolidColorBrush(Color.Parse("#161412")) : new SolidColorBrush(woolLine, 0.55);
        ctx.FillRectangle(shade, new Rect(0, 3, w, baseY));
        Bumps(ctx, shade, w, baseY + 3, bump);
        ctx.FillRectangle(wool, new Rect(0, 0, w, baseY));
        Bumps(ctx, wool, w, baseY, bump);

        // 곱슬 털 결 - 고정 씨앗이라 창마다 같은 무늬
        var curl = new Pen(new SolidColorBrush(woolLine, 0.75), 1.3, lineCap: PenLineCap.Round);
        uint seed = 7;
        double Next() { seed = seed * 1664525 + 1013904223; return (seed >> 8) / 16777216.0; }
        int n = (int)(w * baseY / 700);
        for (int i = 0; i < n; i++)
        {
            double x = 10 + Next() * (w - 20), y = 6 + Next() * (baseY - 6);
            double r = 2.5 + Next() * 2.5;
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(x - r, y), false);
                c.ArcTo(new Point(x + r, y), new Size(r, r), 0, false, Next() > 0.5 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
                c.EndFigure(false);
            }
            ctx.DrawGeometry(null, curl, g);
        }

        // 얼굴 간격 - 기본 창(1240) 기준 고정, 좁은 창은 너비에 맞춰 줄임
        double cx = w / 2, ey = 74;
        double ear = Math.Min(316, w * 0.4), eye = Math.Min(214, w * 0.27);
        DrawEar(ctx, new Point(cx - ear, ey + 22), -1);
        DrawEar(ctx, new Point(cx + ear, ey + 22), 1);
        DrawEye(ctx, new Point(cx - eye, ey), 20);
        DrawEye(ctx, new Point(cx + eye, ey), 20);
        DrawNoseMouth(ctx, new Point(cx, ey + 26));
    }

    static void Bumps(DrawingContext ctx, IBrush b, double w, double y, double r)
    {
        int i = 0;
        for (double x = 6; x < w + 10; x += r * 1.55, i++)
            ctx.DrawEllipse(b, null, new Point(x, y), r + (i % 3 == 1 ? 2 : 0), r);
    }

    // 처진 펠트 귀 - 바깥 펠트 + 안쪽 분홍 + 홈질
    static void DrawEar(DrawingContext ctx, Point c, int dir)
    {
        using (ctx.PushTransform(Matrix.CreateTranslation(-c.X, -c.Y) * Matrix.CreateRotation(dir * 0.28) * Matrix.CreateTranslation(c.X, c.Y)))
        {
            ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), null, new Point(c.X, c.Y + 2.5), 36, 16);
            ctx.DrawEllipse(Tone.Brush("Muzzle"), null, c, 36, 16);
            using (ctx.PushOpacity(0.7)) ctx.DrawEllipse(Tone.Brush("Blush"), null, new Point(c.X + dir * 4, c.Y), 23, 8);
            ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Colors.White, 0.35), 1.1) { DashStyle = new DashStyle([2, 2], 0) }, c, 31.5, 12);
        }
    }

    // 단추 눈 - 테두리, 안쪽 홈, 빛 반사, 구멍 넷에 X 자 실
    static void DrawEye(DrawingContext ctx, Point c, double rr)
    {
        var col = Tone.ColorOf("Pupil");
        var face = new SolidColorBrush(Color.FromRgb((byte)Math.Min(255, col.R + 34), (byte)Math.Min(255, col.G + 32), (byte)Math.Min(255, col.B + 34)));
        var rim = new SolidColorBrush(col);
        ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x38, 0, 0, 0)), null, new Point(c.X, c.Y + 2.5), rr, rr);
        ctx.DrawEllipse(face, new Pen(rim, 2), c, rr, rr);
        ctx.DrawEllipse(null, new Pen(rim, 1.4), c, rr - 5, rr - 5);
        using (ctx.PushOpacity(0.5))
        {
            var arc = new StreamGeometry();
            using (var g = arc.Open())
            {
                g.BeginFigure(new Point(c.X - rr + 4, c.Y - 2), false);
                g.ArcTo(new Point(c.X - 2, c.Y - rr + 4), new Size(rr - 4, rr - 4), 0, false, SweepDirection.Clockwise);
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, new Pen(Brushes.White, 2.2, lineCap: PenLineCap.Round), arc);
        }
        Sew.Cross(ctx, c, 4.2, Tone.Brush("EyeThread"), 2.2, rim, 1.9);
    }

    static void DrawNoseMouth(DrawingContext ctx, Point c)
    {
        var pink = Tone.Brush("Nose");
        var nose = new StreamGeometry();
        using (var g = nose.Open())
        {
            g.BeginFigure(new Point(c.X - 9, c.Y - 4), true);
            g.QuadraticBezierTo(new Point(c.X, c.Y - 8), new Point(c.X + 9, c.Y - 4));
            g.QuadraticBezierTo(new Point(c.X + 1.5, c.Y + 4.5), new Point(c.X, c.Y + 4.5));
            g.QuadraticBezierTo(new Point(c.X - 1.5, c.Y + 4.5), new Point(c.X - 9, c.Y - 4));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(pink, null, nose);
        var mouth = new StreamGeometry();
        using (var g = mouth.Open())
        {
            g.BeginFigure(new Point(c.X - 13, c.Y + 7), false);
            g.QuadraticBezierTo(new Point(c.X - 6.5, c.Y + 14), new Point(c.X, c.Y + 4.5));
            g.QuadraticBezierTo(new Point(c.X + 6.5, c.Y + 14), new Point(c.X + 13, c.Y + 7));
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(pink, 2.2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), mouth);
    }
}
