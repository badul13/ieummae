using Ieummae.Core.Diff;

namespace Ieummae.App.Views;

// 통합 보기 한 줄 - 덩어리 머리줄 또는 코드 줄
public sealed class UnifiedRow
{
    public string? HunkText { get; init; }
    public DiffLine? Line { get; init; }
    public bool IsHunk => HunkText is not null;
    public bool IsAdd => Line?.Kind == LineKind.Add;
    public bool IsDel => Line?.Kind == LineKind.Del;
    public string OldNo => Line is { OldNo: > 0 } l ? l.OldNo.ToString() : "";
    public string NewNo => Line is { NewNo: > 0 } l ? l.NewNo.ToString() : "";
    public string Sign => Line?.Kind switch { LineKind.Add => "+", LineKind.Del => "-", _ => "" };

    public static List<UnifiedRow> Build(FileDiff f)
    {
        var rows = new List<UnifiedRow>();
        foreach (var h in f.Hunks)
        {
            rows.Add(new UnifiedRow { HunkText = h.Header });
            rows.AddRange(h.Lines.Select(l => new UnifiedRow { Line = l }));
        }
        return rows;
    }
}

// 좌우 보기 한 줄 - 빈 쪽은 빗금 대신 옅은 바탕
public sealed class SplitRowView
{
    public string? HunkText { get; init; }
    public DiffLine? Left { get; init; }
    public DiffLine? Right { get; init; }
    public bool IsHunk => HunkText is not null;
    public bool LeftDel => Left?.Kind == LineKind.Del;
    public bool RightAdd => Right?.Kind == LineKind.Add;
    public bool LeftEmpty => !IsHunk && Left is null;
    public bool RightEmpty => !IsHunk && Right is null;
    public string LeftNo => Left is { OldNo: > 0 } l ? l.OldNo.ToString() : "";
    public string RightNo => Right is { NewNo: > 0 } r ? r.NewNo.ToString() : "";

    public static List<SplitRowView> Build(FileDiff f) => SplitRows.Build(f)
        .Select(r => r.Hunk is { } h ? new SplitRowView { HunkText = h.Header } : new SplitRowView { Left = r.Left, Right = r.Right })
        .ToList();
}
