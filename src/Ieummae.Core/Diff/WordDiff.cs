namespace Ieummae.Core.Diff;

// 단어 단위 강조 - 덩어리 안에서 연속된 삭제 줄 묶음과 추가 줄 묶음을 순서대로 짝지어
// 두 줄의 토큰 최장 공통 부분열(LCS) 밖 토큰을 바뀐 구간으로 표시
// 줄 비교 자체는 git 결과 그대로, 이 클래스는 짝지은 두 줄 안의 강조만 계산
public static class WordDiff
{
    // 이보다 크면 계산 생략 (토큰 수 곱)
    const int MaxCells = 250_000;
    // 바뀐 글자 비율이 이보다 크면 강조 생략 - 거의 다 바뀐 줄은 강조가 오히려 방해
    const double MaxChangedRatio = 0.6;

    public static void Apply(FileDiff file)
    {
        foreach (var h in file.Hunks) Apply(h.Lines);
    }

    public static void Apply(List<DiffLine> lines)
    {
        int i = 0;
        while (i < lines.Count)
        {
            if (lines[i].Kind != LineKind.Del) { i++; continue; }
            int delStart = i;
            while (i < lines.Count && lines[i].Kind == LineKind.Del) i++;
            int addStart = i;
            while (i < lines.Count && lines[i].Kind == LineKind.Add) i++;
            int pairs = Math.Min(addStart - delStart, i - addStart);
            for (int k = 0; k < pairs; k++) Mark(lines[delStart + k], lines[addStart + k]);
        }
    }

    static void Mark(DiffLine del, DiffLine add)
    {
        var a = Tokenize(del.Text);
        var b = Tokenize(add.Text);
        if ((long)a.Count * b.Count > MaxCells) return;

        // LCS 길이 표 - 뒤에서부터
        var dp = new int[a.Count + 1, b.Count + 1];
        for (int x = a.Count - 1; x >= 0; x--)
            for (int y = b.Count - 1; y >= 0; y--)
                dp[x, y] = a[x].Text == b[y].Text ? dp[x + 1, y + 1] + 1 : Math.Max(dp[x + 1, y], dp[x, y + 1]);

        var keepA = new bool[a.Count];
        var keepB = new bool[b.Count];
        for (int x = 0, y = 0; x < a.Count && y < b.Count;)
        {
            if (a[x].Text == b[y].Text) { keepA[x++] = true; keepB[y++] = true; }
            else if (dp[x + 1, y] >= dp[x, y + 1]) x++;
            else y++;
        }

        var ca = Spans(a, keepA);
        var cb = Spans(b, keepB);
        if (Ratio(ca, del.Text) > MaxChangedRatio || Ratio(cb, add.Text) > MaxChangedRatio) return;
        del.Changes = ca;
        add.Changes = cb;
    }

    // 바뀐 토큰을 이어 붙인 구간 - 사이 공백만 있는 경우도 한 구간으로
    static List<(int, int)> Spans(List<Token> t, bool[] keep)
    {
        var r = new List<(int, int)>();
        int start = -1, end = -1;
        for (int i = 0; i < t.Count; i++)
        {
            if (!keep[i])
            {
                if (start < 0) start = t[i].Start;
                end = t[i].Start + t[i].Text.Length;
            }
            else if (start >= 0 && !string.IsNullOrWhiteSpace(t[i].Text))
            {
                r.Add((start, end - start));
                start = -1;
            }
        }
        if (start >= 0) r.Add((start, end - start));
        return r;
    }

    static double Ratio(List<(int Start, int Length)> spans, string text) =>
        text.Trim().Length == 0 ? 0 : (double)spans.Sum(s => s.Length) / text.Length;

    readonly record struct Token(int Start, string Text);

    // 토큰 - 글자·숫자·밑줄 묶음(한글 포함), 공백 묶음, 기호는 한 글자씩
    static List<Token> Tokenize(string s)
    {
        var r = new List<Token>();
        int i = 0;
        while (i < s.Length)
        {
            int st = i;
            if (IsWord(s[i])) while (i < s.Length && IsWord(s[i])) i++;
            else if (char.IsWhiteSpace(s[i])) while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            else i++;
            r.Add(new Token(st, s[st..i]));
        }
        return r;
    }

    static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';
}
