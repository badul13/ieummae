namespace Ieummae.Core.Log;

// 그래프 레인 계산 - 위(최신)에서 아래로 한 커밋씩, 레인마다 "다음에 올 커밋 해시"를 들고 감
// 위상 순서(--topo-order) 입력 가정: 자식이 항상 부모보다 먼저 나옴
public sealed class GraphBuilder
{
    // 레인 = (기다리는 커밋, 선 색 번호), 빈 레인은 null
    readonly List<(string Hash, int Color)?> _lanes = [];
    int _nextColor;

    public GraphRow Add(LogEntry e)
    {
        // 이 커밋을 기다리던 레인들 - 위에서 내려와 점으로 모임
        var up = new List<(int, int)>();
        for (int i = 0; i < _lanes.Count; i++)
            if (_lanes[i] is { } l && l.Hash == e.Hash) up.Add((i, l.Color));
        var pass = new List<(int, int)>();
        for (int i = 0; i < _lanes.Count; i++)
            if (_lanes[i] is { } l && l.Hash != e.Hash) pass.Add((i, l.Color));

        // 점 레인 - 기다리던 첫 레인, 없으면(브랜치 끝) 빈 레인 또는 새 레인
        int dot, color;
        if (up.Count > 0) (dot, color) = up[0];
        else { dot = FreeLane(); color = _nextColor++; }
        foreach (var (i, _) in up) _lanes[i] = null;

        // 부모로 내려가는 선 - 첫 부모는 같은 레인·같은 색, 나머지 부모는 이미 기다리는 레인이 있으면 합류
        var down = new List<(int, int)>();
        if (e.Parents.Length > 0)
        {
            _lanes[dot] = (e.Parents[0], color);
            down.Add((dot, color));
        }
        for (int k = 1; k < e.Parents.Length; k++)
        {
            int j = _lanes.FindIndex(l => l is { } x && x.Hash == e.Parents[k]);
            if (j >= 0) { down.Add((j, _lanes[j]!.Value.Color)); continue; }
            j = FreeLane();
            int c = _nextColor++;
            _lanes[j] = (e.Parents[k], c);
            down.Add((j, c));
        }
        while (_lanes.Count > 0 && _lanes[^1] is null) _lanes.RemoveAt(_lanes.Count - 1);

        return e.Graph = new GraphRow(dot, color, [.. pass], [.. up], [.. down]);
    }

    int FreeLane()
    {
        int i = _lanes.IndexOf(null);
        if (i >= 0) return i;
        _lanes.Add(null);
        return _lanes.Count - 1;
    }
}
