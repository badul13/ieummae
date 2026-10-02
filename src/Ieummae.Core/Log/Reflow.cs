using System.Text;

namespace Ieummae.Core.Log;

// 커밋 본문 다시 흘리기 - 72자에서 끊어 쓴 문단을 한 줄로 이어 좁은 칸에서 자연스럽게 줄바꿈
// 빈 줄(문단 구분), 목록(-, *, 숫자.), 들여쓴 줄(코드·인용), 트레일러(Key: 값)는 그대로
public static class Reflow
{
    public static string Body(string text)
    {
        var sb = new StringBuilder();
        bool joinable = false;
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.TrimEnd();
            bool keep = line.Length == 0 || Keeps(line);
            if (joinable && !keep && line.Length > 0)
                sb.Append(' ').Append(line);
            else
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(line);
            }
            // 목록 항목은 다음 줄이 이어 붙을 수 있음 (항목 안 줄바꿈)
            joinable = line.Length > 0 && !line.StartsWith(' ') && !line.StartsWith('\t');
        }
        return sb.ToString();
    }

    static bool Keeps(string line) =>
        line.StartsWith(' ') || line.StartsWith('\t')
        || line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("+ ")
        || (line.Length > 2 && char.IsDigit(line[0]) && line.IndexOf(". ", StringComparison.Ordinal) is > 0 and < 4)
        || IsTrailer(line);

    // Signed-off-by: 이름 <메일> 같은 줄
    static bool IsTrailer(string line)
    {
        int c = line.IndexOf(": ", StringComparison.Ordinal);
        return c > 0 && !line[..c].Contains(' ') && line[..c].Contains('-') && char.IsUpper(line[0]);
    }
}
