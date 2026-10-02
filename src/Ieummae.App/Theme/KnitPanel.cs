using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Ieummae.App.Theme;

// 뜨개 패널 - 둥근 뜨개 천 + 털실 테두리 박음질, 자식은 그 위에
// Yarn - 실 색 자원 접두사 (Knit = 흰 양털, Knit2 = 오트밀)
public sealed class KnitPanel : Decorator
{
    public static readonly StyledProperty<string> YarnProperty = AvaloniaProperty.Register<KnitPanel, string>(nameof(Yarn), "Knit");
    public string Yarn { get => GetValue(YarnProperty); set => SetValue(YarnProperty, value); }

    public KnitPanel() => Tone.Redraw(this);

    // 코 한 칸 크기 - 세로 기둥 간격 Sx, V 코 높이 Sy, 타일 해상도 2배
    const double Sx = 12, Sy = 9, Scale = 2, Radius = 18;
    static readonly Dictionary<Color, Bitmap> Tiles = new();

    // 코 한 칸 타일 - 코 하나 = 기울어진 고리 두 개, 이웃 칸에서 넘어오는 고리까지 그림
    // (코를 하나씩 그리면 패널당 고리 만 개 - 호버마다 다시 그려져 반응 느림)
    static Bitmap KnitTile(Color line)
    {
        if (Tiles.TryGetValue(line, out var bmp)) return bmp;
        var rtb = new RenderTargetBitmap(new PixelSize((int)(Sx * Scale), (int)(Sy * Scale)), new Vector(96 * Scale, 96 * Scale));
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
        var b = new Rect(Bounds.Size).Deflate(new Thickness(2, 2, 2, 4));
        if (b.Width <= 0 || b.Height <= 0) return;
        var fill = Tone.Brush(Yarn + "Bg");
        var line = Tone.ColorOf(Yarn + "Line");
        var shape = new RoundedRect(b, Radius);

        // 그림자 + 천 + 메리야스 무늬
        ctx.DrawRectangle(new SolidColorBrush(line, 0.8), null, new RoundedRect(b.Translate(new Vector(0, 2.5)), Radius));
        ctx.DrawRectangle(fill, null, shape);
        var knit = new ImageBrush(KnitTile(line))
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.Fill,
            DestinationRect = new RelativeRect(b.X, b.Y, Sx, Sy, RelativeUnit.Absolute),
        };
        ctx.DrawRectangle(knit, null, shape);

        // 테두리 - 같은 실 색 굵은 털실 선 + 안쪽 홈질
        ctx.DrawRectangle(null, new Pen(new SolidColorBrush(line), 3), shape);
        var inner = new RoundedRect(b.Deflate(7), Radius - 6);
        ctx.DrawRectangle(null, new Pen(new SolidColorBrush(line), 2, lineCap: PenLineCap.Round) { DashStyle = new DashStyle([1.6, 1.8], 0) }, inner);
    }
}
