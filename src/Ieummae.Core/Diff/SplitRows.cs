namespace Ieummae.Core.Diff;

// 좌우 보기 한 줄 - 왼쪽(옛 파일)·오른쪽(새 파일), 덩어리 머리줄이면 Hunk
public sealed record SplitRow(DiffLine? Left, DiffLine? Right, Hunk? Hunk = null);

public static class SplitRows
{
    // 통합 덩어리 → 좌우 줄. 삭제 묶음과 추가 묶음은 같은 높이로 나란히, 남는 쪽은 빈칸
    public static List<SplitRow> Build(FileDiff file)
    {
        var rows = new List<SplitRow>();
        foreach (var h in file.Hunks)
        {
            rows.Add(new SplitRow(null, null, h));
            var lines = h.Lines;
            int i = 0;
            while (i < lines.Count)
            {
                if (lines[i].Kind == LineKind.Context) { rows.Add(new SplitRow(lines[i], lines[i])); i++; continue; }
                var dels = new List<DiffLine>();
                var adds = new List<DiffLine>();
                while (i < lines.Count && lines[i].Kind == LineKind.Del) dels.Add(lines[i++]);
                while (i < lines.Count && lines[i].Kind == LineKind.Add) adds.Add(lines[i++]);
                for (int k = 0; k < Math.Max(dels.Count, adds.Count); k++)
                    rows.Add(new SplitRow(k < dels.Count ? dels[k] : null, k < adds.Count ? adds[k] : null));
            }
        }
        return rows;
    }
}
