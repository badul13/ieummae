using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Ieummae.App.Theme;
using Ieummae.Core.Log;

namespace Ieummae.App.Views;

// 그래프 칸 공통 너비 - 로그 전체에서 가장 넓은 레인 기준, 모든 행이 같은 너비를 바인딩
public sealed class GraphLayout : INotifyPropertyChanged
{
    public const double Gap = 20;
    // 이보다 넓은 레인은 잘라서 그림 (메시지 칸 확보)
    public const int MaxLanes = 12;
    // 너비는 최근 커밋 기준 - 오래된 역사의 넓은 레인 때문에 메시지 칸이 줄지 않게
    public const int SampleRows = 2000;

    public event PropertyChangedEventHandler? PropertyChanged;

    int _lanes = 1;
    public int Lanes
    {
        get => _lanes;
        set
        {
            value = Math.Clamp(value, 1, MaxLanes);
            if (value == _lanes) return;
            _lanes = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Width)));
        }
    }

    bool _hidden;
    // 검색 결과처럼 연속이 아닌 목록 - 선 없이 단추만
    public bool Hidden
    {
        get => _hidden;
        set { _hidden = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Width))); }
    }

    public double Width => Hidden ? Gap * 1.6 : Gap * 1.6 + (Lanes - 1) * Gap;
    public static double X(int lane) => Gap * 0.8 + lane * Gap;
}

// 이름표 하나
public sealed record BadgeView(string Text, RefKind Kind, int Color)
{
    public bool IsHead => Kind == RefKind.Head;
    public bool IsTag => Kind == RefKind.Tag;
    public bool IsRemote => Kind == RefKind.Remote;
    public bool IsBranch => Kind == RefKind.Branch;
    public IBrush DotBrush => Tone.Brush("GB" + (Color % 7));
}

// 로그 한 줄
public sealed class LogRow(LogEntry e, GraphLayout graph)
{
    public LogEntry Entry { get; } = e;
    public GraphLayout Graph { get; } = graph;
    public string Subject => Entry.Subject;
    public string Author => Entry.Author;
    public string Rel { get; } = Relative(e.Time);
    public bool IsHead => Entry.IsHead;

    // 같은 커밋의 origin/같은이름 - 로컬 하나로 합쳐 표시
    public List<BadgeView> Badges { get; } = e.Refs
        .Where(r => !(r.Kind == RefKind.Remote && e.Refs.Any(l => l.Kind is RefKind.Head or RefKind.Branch && r.Name.EndsWith("/" + l.Name))))
        .Select(r => new BadgeView(r.Name, r.Kind, e.Graph.DotColor)).ToList();
    public bool HasBadges => Badges.Count > 0;

    public static string Relative(DateTimeOffset t)
    {
        var d = DateTimeOffset.Now - t;
        if (d.TotalMinutes < 1) return "방금";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes}분 전";
        if (d.TotalDays < 1) return $"{(int)d.TotalHours}시간 전";
        if (d.TotalDays < 2) return "어제";
        if (d.TotalDays < 30) return $"{(int)d.TotalDays}일 전";
        return t.ToLocalTime().ToString("yyyy-MM-dd");
    }
}

// 그래프 칸 - 레인 선(점선 털실)과 단추
public sealed class GraphCell : Control
{
    public static readonly StyledProperty<LogRow?> RowProperty = AvaloniaProperty.Register<GraphCell, LogRow?>(nameof(Row));
    public LogRow? Row { get => GetValue(RowProperty); set => SetValue(RowProperty, value); }

    static GraphCell() => AffectsRender<GraphCell>(RowProperty);
    public GraphCell() => Tone.Redraw(this);

    static IPen Pen(int color) => new Pen(Tone.Brush("GB" + (color % 7)), 2.4, lineCap: PenLineCap.Round)
    {
        DashStyle = new DashStyle([1.4, 1.5], 0),
    };

    static bool Visible(int lane) => lane < GraphLayout.MaxLanes;

    public override void Render(DrawingContext ctx)
    {
        if (Row is not { } row) return;
        var g = row.Entry.Graph;
        double h = Bounds.Height, cy = h / 2;
        bool head = row.IsHead;
        if (row.Graph.Hidden)
        {
            Sew.LaneButton(ctx, new Point(GraphLayout.X(0), cy), head ? 9 : 7.5, g.DotColor);
            return;
        }
        var c = new Point(GraphLayout.X(Math.Min(g.Dot, GraphLayout.MaxLanes - 1)), cy);
        foreach (var (l, col) in g.Pass)
            if (Visible(l)) ctx.DrawLine(Pen(col), new Point(GraphLayout.X(l), 0), new Point(GraphLayout.X(l), h));
        foreach (var (l, col) in g.Up)
            if (Visible(l)) Link(ctx, Pen(col), new Point(GraphLayout.X(l), 0), c);
        foreach (var (l, col) in g.Down)
            if (Visible(l)) Link(ctx, Pen(col), c, new Point(GraphLayout.X(l), h));
        Sew.LaneButton(ctx, c, head ? 9 : 7.5, g.DotColor);
    }

    // 레인 사이 이음 - 부드러운 S 곡선
    static void Link(DrawingContext ctx, IPen pen, Point a, Point b)
    {
        if (a.X == b.X) { ctx.DrawLine(pen, a, b); return; }
        var geo = new StreamGeometry();
        using (var s = geo.Open())
        {
            s.BeginFigure(a, false);
            double my = (a.Y + b.Y) / 2;
            s.CubicBezierTo(new Point(a.X, my), new Point(b.X, my), b);
            s.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, geo);
    }
}
