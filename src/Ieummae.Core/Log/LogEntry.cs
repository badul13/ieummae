namespace Ieummae.Core.Log;

public enum RefKind { Head, Branch, Remote, Tag }

// 커밋에 붙은 이름 - HEAD 가 가리키는 브랜치는 Head
public sealed record RefLabel(string Name, RefKind Kind);

// 커밋 한 줄 - 로그 목록에 필요한 것만 (본문·변경 파일은 선택 시 따로)
public sealed class LogEntry
{
    public required string Hash { get; init; }
    public required string[] Parents { get; init; }
    public required string Author { get; init; }
    public required string Email { get; init; }
    public required DateTimeOffset Time { get; init; }
    public required string Subject { get; init; }
    public List<RefLabel> Refs { get; init; } = [];

    public string Short => Hash[..Math.Min(8, Hash.Length)];
    public bool IsMerge => Parents.Length > 1;
    public bool IsHead => Refs.Any(r => r.Kind == RefKind.Head) || DetachedHead;
    // 분리된 HEAD - "HEAD" 장식만 있는 경우
    public bool DetachedHead { get; init; }

    // 레인 - 그래프 계산 결과 (GraphBuilder 가 채움)
    public GraphRow Graph { get; set; } = GraphRow.Empty;
}

// 그래프 한 줄 - 점 레인과 선분들. 색은 레인마다 이어지는 선 번호
public sealed record GraphRow(int Dot, int DotColor, (int Lane, int Color)[] Pass, (int Lane, int Color)[] Up, (int Lane, int Color)[] Down)
{
    public static readonly GraphRow Empty = new(0, 0, [], [], []);
    public int Width => Math.Max(Dot, Math.Max(Max(Pass), Math.Max(Max(Up), Max(Down)))) + 1;
    static int Max((int Lane, int Color)[] a) => a.Length == 0 ? 0 : a.Max(x => x.Lane);
}
