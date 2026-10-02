using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace IeumMock;

// 창 윗부분 - 봉제 인형 정면: 양털 덩어리 위로 치켜든 보송한 얼굴, 타원 눈, 분홍 코, 고양이 입, 처진 귀
public sealed class WoolHeader : Control
{
    static object Res(string k) => Application.Current!.Resources[k]!;
    static IBrush R(string k) => (IBrush)Res(k);
    static Color C(string k) => ((SolidColorBrush)Res(k)).Color;

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        const double bump = 14;
        double baseY = h - bump;
        var wool = R("Wool");
        var woolLine = C("WoolLine");

        // 양털 몸통 + 구름 가장자리
        // 다크 - 양털 결 색이 양털과 비슷해 구름 가장자리가 묻힘, 바탕보다 진한 그림자로
        var shade = (bool)Res("IsDark") ? new SolidColorBrush(Color.Parse("#161412")) : new SolidColorBrush(woolLine, 0.55);
        ctx.FillRectangle(shade, new Rect(0, 3, w, baseY));
        Bumps(ctx, shade, w, baseY + 3, bump);
        ctx.FillRectangle(wool, new Rect(0, 0, w, baseY));
        Bumps(ctx, wool, w, baseY, bump);

        // 곱슬 털 결
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

        // 직전 얼굴 - 처진 펠트 귀, 단추 눈 + 코·고양이 입
        double cx = w / 2, ey = 74;
        DrawEar(ctx, new Point(cx - 316, ey + 22), -1);
        DrawEar(ctx, new Point(cx + 316, ey + 22), 1);
        DrawButtonEye(ctx, new Point(cx - 214, ey), 20);
        DrawButtonEye(ctx, new Point(cx + 214, ey), 20);
        DrawNoseMouth(ctx, new Point(cx, ey + 26));
    }

    static void Bumps(DrawingContext ctx, IBrush b, double w, double y, double r)
    {
        int i = 0;
        for (double x = 6; x < w + 10; x += r * 1.55, i++)
            ctx.DrawEllipse(b, null, new Point(x, y), r + (i % 3 == 1 ? 2 : 0), r);
    }

    static void DrawEar(DrawingContext ctx, Point c, int dir)
    {
        using (ctx.PushTransform(Matrix.CreateTranslation(-c.X, -c.Y) * Matrix.CreateRotation(dir * 0.28) * Matrix.CreateTranslation(c.X, c.Y)))
        {
            ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), null, new Point(c.X, c.Y + 2.5), 36, 16);
            ctx.DrawEllipse(R("Muzzle"), null, c, 36, 16);
            using (ctx.PushOpacity(0.7)) ctx.DrawEllipse(R("Blush"), null, new Point(c.X + dir * 4, c.Y), 23, 8);
            ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Colors.White, 0.35), 1.1) { DashStyle = new DashStyle(new double[] { 2, 2 }, 0) }, c, 31.5, 12);
        }
    }

    static void DrawButtonEye(DrawingContext ctx, Point c, double rr)
    {
        var col = C("Pupil");
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
        double hd = 4.2;
        var thread = new Pen(R("EyeThread"), 2.2, lineCap: PenLineCap.Round);
        ctx.DrawLine(thread, new Point(c.X - hd, c.Y - hd), new Point(c.X + hd, c.Y + hd));
        ctx.DrawLine(thread, new Point(c.X + hd, c.Y - hd), new Point(c.X - hd, c.Y + hd));
        foreach (var (dx, dy) in new[] { (-hd, -hd), (hd, -hd), (-hd, hd), (hd, hd) })
            ctx.DrawEllipse(rim, null, new Point(c.X + dx, c.Y + dy), 1.9, 1.9);
    }

    // 분홍 코 + 고양이 입
    static void DrawNoseMouth(DrawingContext ctx, Point c)
    {
        var pink = R("Nose");
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

// 뜨개 패널 - 둥근 뜨개 천 + 털실 테두리 박음질, 자식은 그 위에
// Yarn - 실 색 자원 접두사 (Knit = 깃 로그, Knit2 = Changes)
public sealed class KnitPanel : Decorator
{
    public static readonly StyledProperty<string> YarnProperty = AvaloniaProperty.Register<KnitPanel, string>(nameof(Yarn), "Knit");
    public string Yarn { get => GetValue(YarnProperty); set => SetValue(YarnProperty, value); }

    static object Res(string k) => Application.Current!.Resources[k]!;

    // 코 한 칸 크기 - 세로 기둥 간격 sx, V 코 높이 sy
    const double Sx = 12, Sy = 9, Scale = 2;
    static readonly System.Collections.Generic.Dictionary<Color, Avalonia.Media.Imaging.Bitmap> Tiles = new();

    // 코 한 칸 타일 - 코 하나 = 기울어진 고리 두 개, 이웃 칸 고리가 넘어오는 부분까지 그림
    static Avalonia.Media.Imaging.Bitmap KnitTile(Color line)
    {
        if (Tiles.TryGetValue(line, out var bmp)) return bmp;
        var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(
            new PixelSize((int)(Sx * Scale), (int)(Sy * Scale)), new Vector(96 * Scale, 96 * Scale));
        var loop = new SolidColorBrush(line, 0.30);
        var edge = new Pen(new SolidColorBrush(line, 0.55), 0.9);
        using (var ctx = rtb.CreateDrawingContext())
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    foreach (var dir in new[] { -1, 1 })
                    {
                        var c = new Point(Sx / 2 + dx * Sx + dir * 2.6, dy * Sy);
                        using (ctx.PushTransform(Matrix.CreateTranslation(-c.X, -c.Y) * Matrix.CreateRotation(dir * -0.55) * Matrix.CreateTranslation(c.X, c.Y)))
                            ctx.DrawEllipse(loop, edge, c, 2.4, 5.2);
                    }
        }
        Tiles[line] = rtb;
        return rtb;
    }

    public override void Render(DrawingContext ctx)
    {
        const double radius = 18;
        var b = new Rect(Bounds.Size).Deflate(new Thickness(2, 2, 2, 4));
        if (b.Width <= 0 || b.Height <= 0) return;
        var fill = (IBrush)Res(Yarn + "Bg");
        var line = ((SolidColorBrush)Res(Yarn + "Line")).Color;
        var shape = new RoundedRect(b, radius);

        // 그림자 + 천
        ctx.DrawRectangle(new SolidColorBrush(line, 0.8), null, new RoundedRect(b.Translate(new Vector(0, 2.5)), radius));
        ctx.DrawRectangle(fill, null, shape);

        // 메리야스 뜨기 - 코 한 칸을 비트맵 타일로 한 번만 그리고 바둑판 채우기
        // (코를 하나씩 그리면 패널당 고리 만 개 - 호버마다 다시 그려져 반응 느림)
        var knit = new ImageBrush(KnitTile(line))
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.Fill,
            DestinationRect = new RelativeRect(b.X, b.Y, Sx, Sy, RelativeUnit.Absolute),
        };
        ctx.DrawRectangle(knit, null, shape);

        // 테두리 - 같은 실 색 굵은 털실 선 + 안쪽 홈질
        ctx.DrawRectangle(null, new Pen(new SolidColorBrush(line), 3), shape);
        var inner = new RoundedRect(b.Deflate(7), radius - 6);
        ctx.DrawRectangle(null, new Pen(new SolidColorBrush(line), 2, lineCap: PenLineCap.Round) { DashStyle = new DashStyle(new double[] { 1.6, 1.8 }, 0) }, inner);
    }
}
