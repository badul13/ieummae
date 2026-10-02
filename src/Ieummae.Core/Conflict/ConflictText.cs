using System.Text;

namespace Ieummae.Core.Conflict;

public enum Choice { None, Ours, Theirs, Both, BothReverse, Custom }

// 파일 조각 - 공통 부분 또는 충돌 덩어리
public abstract class Segment;

public sealed class CommonSegment(List<string> lines) : Segment
{
    public List<string> Lines { get; } = lines;
}

public sealed class ConflictSegment : Segment
{
    public required List<string> Ours { get; init; }
    public required List<string> Theirs { get; init; }
    // diff3 형식이면 공통 조상 쪽
    public List<string>? Base { get; init; }
    public string OursLabel { get; init; } = "";
    public string TheirsLabel { get; init; } = "";
    public Choice Choice { get; set; }
    public string Custom { get; set; } = "";

    public IEnumerable<string> Resolved => Choice switch
    {
        Choice.Ours => Ours,
        Choice.Theirs => Theirs,
        Choice.Both => Ours.Concat(Theirs),
        Choice.BothReverse => Theirs.Concat(Ours),
        Choice.Custom => Custom.Replace("\r\n", "\n").Split('\n'),
        _ => throw new InvalidOperationException("고르지 않은 충돌"),
    };
}

// 충돌 표시(<<<<<<< ======= >>>>>>>)가 든 파일 - 덩어리별로 고르고 다시 씀
public sealed class ConflictText
{
    public List<Segment> Segments { get; } = [];
    public string NewLine { get; private set; } = "\n";
    public bool EndsWithNewLine { get; private set; }
    public IEnumerable<ConflictSegment> Conflicts => Segments.OfType<ConflictSegment>();
    public bool AllChosen => Conflicts.All(c => c.Choice != Choice.None);

    public static ConflictText Parse(string text)
    {
        var t = new ConflictText
        {
            NewLine = text.Contains("\r\n") ? "\r\n" : "\n",
            EndsWithNewLine = text.EndsWith('\n'),
        };
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (t.EndsWithNewLine) lines.RemoveAt(lines.Count - 1);

        var common = new List<string>();
        int i = 0;
        while (i < lines.Count)
        {
            if (!lines[i].StartsWith("<<<<<<<")) { common.Add(lines[i++]); continue; }
            // 충돌 덩어리 - 끝 표시 없으면 공통으로 취급
            int start = i;
            var oursLabel = lines[i].Length > 8 ? lines[i][8..] : "";
            var ours = new List<string>();
            List<string>? @base = null;
            var theirs = new List<string>();
            var cur = ours;
            string theirsLabel = "";
            bool closed = false;
            for (i++; i < lines.Count; i++)
            {
                var l = lines[i];
                if (l.StartsWith("|||||||") && cur == ours) { @base = []; cur = @base; }
                else if (l == "=======" && cur != theirs) cur = theirs;
                else if (l.StartsWith(">>>>>>>") && cur == theirs) { theirsLabel = l.Length > 8 ? l[8..] : ""; closed = true; i++; break; }
                else cur.Add(l);
            }
            if (!closed)
            {
                common.AddRange(lines.Skip(start));
                break;
            }
            if (common.Count > 0) { t.Segments.Add(new CommonSegment(common)); common = []; }
            t.Segments.Add(new ConflictSegment { Ours = ours, Theirs = theirs, Base = @base, OursLabel = oursLabel, TheirsLabel = theirsLabel });
        }
        if (common.Count > 0) t.Segments.Add(new CommonSegment(common));
        return t;
    }

    // 고른 대로 합친 내용 - 원래 줄바꿈 방식 유지
    public string Build()
    {
        var all = Segments.SelectMany(s => s switch
        {
            CommonSegment c => c.Lines,
            ConflictSegment c => c.Resolved,
            _ => [],
        });
        var sb = new StringBuilder(string.Join(NewLine, all));
        if (EndsWithNewLine) sb.Append(NewLine);
        return sb.ToString();
    }
}
