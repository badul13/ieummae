using System.Text;
using System.Text.RegularExpressions;

namespace Ieummae.Core.Diff;

// git diff 통합 형식 출력 해석 - 여러 파일, 이름 바꾸기, 바이너리, 모드 변경
public static partial class DiffParser
{
    [GeneratedRegex(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@ ?(.*)$")]
    private static partial Regex HunkHeader();

    public static List<FileDiff> Parse(string text)
    {
        var files = new List<FileDiff>();
        FileDiff? f = null;
        Hunk? h = null;
        int oldNo = 0, newNo = 0;
        DiffLine? last = null;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.EndsWith('\r') ? raw[..^1] : raw;
            if (line.StartsWith("diff --git "))
            {
                f = new FileDiff();
                (f.OldPath, f.NewPath) = SplitGitHeader(line["diff --git ".Length..]);
                files.Add(f);
                h = null;
                continue;
            }
            if (f is null) continue;

            // 덩어리 안 - 첫 글자로 줄 종류 판별
            if (h is not null)
            {
                if (line.Length > 0 && line[0] is ' ' or '+' or '-')
                {
                    last = line[0] switch
                    {
                        '+' => new DiffLine(LineKind.Add, 0, newNo++, line[1..]),
                        '-' => new DiffLine(LineKind.Del, oldNo++, 0, line[1..]),
                        _ => new DiffLine(LineKind.Context, oldNo++, newNo++, line[1..]),
                    };
                    h.Lines.Add(last);
                    continue;
                }
                if (line.StartsWith('\\'))
                {
                    if (last is not null) last.NoNewlineAtEnd = true;
                    continue;
                }
                h = null;
            }

            if (HunkHeader().Match(line) is { Success: true } m)
            {
                int Num(Group g, int def) => g.Success ? int.Parse(g.Value) : def;
                h = new Hunk(Num(m.Groups[1], 0), Num(m.Groups[2], 1), Num(m.Groups[3], 0), Num(m.Groups[4], 1), m.Groups[5].Value, []);
                oldNo = h.OldStart;
                newNo = h.NewStart;
                f.Hunks.Add(h);
            }
            else if (line.StartsWith("--- ")) { if (Strip(line[4..]) is { } p) f.OldPath = p; }
            else if (line.StartsWith("+++ ")) { if (Strip(line[4..]) is { } p) f.NewPath = p; }
            else if (line.StartsWith("new file mode ")) { f.Status = FileStatus.Added; f.NewMode = line[14..]; }
            else if (line.StartsWith("deleted file mode ")) { f.Status = FileStatus.Deleted; f.OldMode = line[18..]; }
            else if (line.StartsWith("old mode ")) f.OldMode = line[9..];
            else if (line.StartsWith("new mode ")) f.NewMode = line[9..];
            else if (line.StartsWith("rename from ")) { f.Status = FileStatus.Renamed; f.OldPath = Unquote(line[12..]); }
            else if (line.StartsWith("rename to ")) { f.Status = FileStatus.Renamed; f.NewPath = Unquote(line[10..]); }
            else if (line.StartsWith("copy from ")) { f.Status = FileStatus.Copied; f.OldPath = Unquote(line[10..]); }
            else if (line.StartsWith("copy to ")) { f.Status = FileStatus.Copied; f.NewPath = Unquote(line[8..]); }
            else if (line.StartsWith("similarity index ")) f.Similarity = int.TryParse(line[17..].TrimEnd('%'), out var s) ? s : 0;
            else if (line.StartsWith("Binary files ") || line == "GIT binary patch") f.IsBinary = true;
        }
        return files;
    }

    // --- a/경로, +++ b/경로 - /dev/null 이면 null
    static string? Strip(string s)
    {
        s = Unquote(s.TrimEnd('\t'));
        if (s == "/dev/null") return null;
        return s.Length > 2 && s[1] == '/' && s[0] is 'a' or 'b' ? s[2..] : s;
    }

    // "diff --git a/x b/y" - 경로에 공백이 있으면 모호하므로 같은 경로 가정 후 rename·---/+++ 줄로 보정
    static (string, string) SplitGitHeader(string s)
    {
        if (s.StartsWith('"'))
        {
            int end = ClosingQuote(s, 0);
            var a = Unquote(s[..(end + 1)]);
            var b = Unquote(s[(end + 2)..]);
            return (a[2..], b.Length > 2 ? b[2..] : b);
        }
        int mid = s.Length / 2;
        if (s.Length % 2 == 1 && s[mid] == ' ' && s[2..mid] == s[(mid + 3)..]) return (s[2..mid], s[(mid + 3)..]);
        int sp = s.IndexOf(" b/", StringComparison.Ordinal);
        return sp > 0 ? (s[2..sp], s[(sp + 3)..]) : (s, s);
    }

    static int ClosingQuote(string s, int open)
    {
        for (int i = open + 1; i < s.Length; i++)
        {
            if (s[i] == '\\') i++;
            else if (s[i] == '"') return i;
        }
        return s.Length - 1;
    }

    // git C 스타일 따옴표 경로 풀기 - \t \n \" \\ 와 8진수 바이트(UTF-8)
    public static string Unquote(string s)
    {
        if (s.Length < 2 || s[0] != '"' || s[^1] != '"') return s;
        var bytes = new List<byte>();
        for (int i = 1; i < s.Length - 1; i++)
        {
            char c = s[i];
            if (c != '\\') { bytes.AddRange(Encoding.UTF8.GetBytes(c.ToString())); continue; }
            char n = s[++i];
            if (n is >= '0' and <= '7' && i + 2 < s.Length)
            {
                bytes.Add(Convert.ToByte(s.Substring(i, 3), 8));
                i += 2;
                continue;
            }
            bytes.Add(n switch { 't' => (byte)'\t', 'n' => (byte)'\n', 'r' => (byte)'\r', 'a' => 7, 'b' => 8, 'f' => 12, 'v' => 11, _ => (byte)n });
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}
