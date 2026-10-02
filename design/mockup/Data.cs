using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace IeumMock;

public sealed class Badge
{
    public required string Text { get; init; }
    public bool IsTag { get; init; }
    public bool IsHead { get; init; }
    public bool IsRemote => Text.StartsWith("origin/");
    public int Lane { get; init; }
    public IBrush LaneBrush => (IBrush)Application.Current!.Resources["GB" + (Lane % 7)]!;
    public IBrush SoftBrush => (IBrush)Application.Current!.Resources["GS" + (Lane % 7)]!;
}

public sealed class Commit
{
    public required string Hash { get; init; }
    public required string Msg { get; init; }
    public required string Author { get; init; }
    public required string Date { get; init; }
    public required string Rel { get; init; }
    public string FullHash { get; init; } = "";
    public string Body { get; init; } = "";
    public string DateText { get; init; } = "";
    public string[] Parents { get; init; } = [];
    public bool HasBody => Body.Length > 0;
    public string Meta => $"{Author} · {DateText} · {Hash}";
    public List<Badge> Badges { get; init; } = new();
    public bool HasBadges => Badges.Count > 0;
    public bool IsMerge => Down.Length > 1;
    public string Initial => Author[..1].ToUpperInvariant();
    // 그래프 - 점 레인, 통과 레인, 위(자식)·아래(부모) 연결 레인
    public int Dot { get; init; }
    public int[] Pass { get; init; } = [];
    public int[] Up { get; init; } = [];
    public int[] Down { get; init; } = [];
    public IBrush LaneBrush => (IBrush)Application.Current!.Resources["GB" + (Dot % 7)]!;
}

public sealed class FileChange
{
    public required string Status { get; init; }
    public required string Path { get; init; }
    public int Add { get; init; }
    public int Del { get; init; }
    public bool Checked { get; init; } = true;
    public string Counts => $"+{Add} -{Del}";
    public string AddText => $"+{Add}";
    public string DelText => $"-{Del}";
    public string Name => Path[(Path.LastIndexOf('/') + 1)..];
    public string Dir => Path.Contains('/') ? Path[..Path.LastIndexOf('/')] : "";
    public string StatusWord => Status switch { "M" => "Modified", "A" => "Added", "D" => "Deleted", "R" => "Renamed", _ => "Untracked" };
    public double AddRatio => Add + Del == 0 ? 0 : 60.0 * Add / (Add + Del);
    public double DelRatio => Add + Del == 0 ? 0 : 60.0 * Del / (Add + Del);
}

public sealed class DiffLine
{
    public required string Kind { get; init; } // " ", "+", "-", "@"
    public required string Text { get; init; }
    public string Old { get; init; } = "";
    public string New { get; init; } = "";
    public bool IsAdd => Kind == "+";
    public bool IsDel => Kind == "-";
    public bool IsHunk => Kind == "@";
    public string Sign => Kind == "@" ? "" : Kind;
}

// 그래프 칸 - 행마다 레인 선과 점, 모양은 시안 자원(GraphW, LaneGap, DotKind, GraphDash)
public sealed class GraphCell : Control
{
    public static readonly StyledProperty<Commit?> RowProperty = AvaloniaProperty.Register<GraphCell, Commit?>(nameof(Row));
    public Commit? Row { get => GetValue(RowProperty); set => SetValue(RowProperty, value); }

    static GraphCell() => AffectsRender<GraphCell>(RowProperty);

    // 그래프 칸 너비 - 로그 전체에서 가장 넓은 레인 기준, 모든 행 같은 너비
    public static int MaxLane;
    protected override Size MeasureOverride(Size available) => new(Math.Max(96, X(MaxLane) + Gap * 0.8 + 4), 0);

    static object Res(string k) => Application.Current!.Resources[k]!;
    static double Gap => (double)Res("LaneGap");
    static double X(int lane) => Gap * 0.8 + lane * Gap;
    static IBrush B(int lane) => (IBrush)Res("GB" + (lane % 7));
    static IPen P(int lane) => new Pen(B(lane), (double)Res("GraphW"), lineCap: PenLineCap.Round)
    {
        DashStyle = (bool)Res("GraphDash") ? new DashStyle(new double[] { 1.4, 1.5 }, 0) : null,
    };

    static void Link(DrawingContext ctx, IPen pen, Point a, Point b)
    {
        if (a.X == b.X) { ctx.DrawLine(pen, a, b); return; }
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(a, false);
            double my = (a.Y + b.Y) / 2;
            g.CubicBezierTo(new Point(a.X, my), new Point(b.X, my), b);
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, geo);
    }

    public override void Render(DrawingContext ctx)
    {
        var row = Row;
        if (row is null) return;
        double h = Bounds.Height, cy = h / 2;
        var c = new Point(X(row.Dot), cy);
        foreach (var l in row.Pass) ctx.DrawLine(P(l), new Point(X(l), 0), new Point(X(l), h));
        foreach (var l in row.Up) Link(ctx, P(l), new Point(X(l), 0), c);
        foreach (var l in row.Down) Link(ctx, P(l), c, new Point(X(l), h));
        bool head = row.Badges.Exists(b => b.IsHead);
        var white = (IBrush)Res("StickerEdge");
        switch ((string)Res("DotKind"))
        {
            // 단추 - 동그라미 + 실 구멍 두 개
            case "button":
            {
                // 단추 - 진한 테두리, 안쪽 홈, 구멍 넷에 X 자 실
                double rr = head ? 9.5 : 8;
                var col = (Color)Res("G" + (row.Dot % 7));
                var dark = new SolidColorBrush(Color.FromRgb((byte)(col.R * 0.68), (byte)(col.G * 0.68), (byte)(col.B * 0.68)));
                // 다크 - 레인 색이 밝은 파스텔이라 단추 면만 한 톤 낮춤, 밝은 실 대비 확보
                double k = (bool)Res("IsDark") ? 0.8 : 1;
                var face = new SolidColorBrush(Color.FromRgb((byte)(col.R * k), (byte)(col.G * k), (byte)(col.B * k)));
                ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), null, new Point(c.X, c.Y + 1.2), rr, rr);
                ctx.DrawEllipse(face, new Pen(dark, 1.2), c, rr, rr);
                ctx.DrawEllipse(null, new Pen(dark, 0.9), c, rr - 2.6, rr - 2.6);
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
                double hd = head ? 2.3 : 2.0;
                var thread = new Pen((IBrush)Res("Thread"), 1.2, lineCap: PenLineCap.Round);
                ctx.DrawLine(thread, new Point(c.X - hd, c.Y - hd), new Point(c.X + hd, c.Y + hd));
                ctx.DrawLine(thread, new Point(c.X + hd, c.Y - hd), new Point(c.X - hd, c.Y + hd));
                foreach (var (dx, dy) in new[] { (-hd, -hd), (hd, -hd), (-hd, hd), (hd, hd) })
                    ctx.DrawEllipse(dark, null, new Point(c.X + dx, c.Y + dy), 1.0, 1.0);
                break;
            }
            // 스티커 - 흰 테두리 + 아래 그림자
            case "sticker":
            {
                double rr = head ? 8.5 : 7;
                ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x22, 0x30, 0x28, 0x20)), null, new Point(c.X, c.Y + 1.4), rr + 0.6, rr + 0.6);
                ctx.DrawEllipse(white, null, c, rr, rr);
                ctx.DrawEllipse(B(row.Dot), null, c, rr - 2.6, rr - 2.6);
                if (head) ctx.DrawEllipse(white, null, c, 2.2, 2.2);
                break;
            }
            // 말랑 - 흰 고리 두른 동그라미
            default:
            {
                double rr = head ? 7.5 : 5.5;
                ctx.DrawEllipse(white, null, c, rr + 2, rr + 2);
                ctx.DrawEllipse(B(row.Dot), null, c, rr, rr);
                if (head) ctx.DrawEllipse(white, null, c, 2.5, 2.5);
                break;
            }
        }
    }
}
