namespace Ieummae.Core.Diff;

public enum LineKind { Context, Add, Del }

// 한 줄 - 줄 번호는 해당 쪽에 없으면 0
public sealed record DiffLine(LineKind Kind, int OldNo, int NewNo, string Text)
{
    // 단어 단위 강조 구간 - (시작, 길이), 짝 줄이 없거나 너무 많이 바뀌면 비어 있음
    public IReadOnlyList<(int Start, int Length)> Changes { get; set; } = [];
    public bool NoNewlineAtEnd { get; set; }
}

// 덩어리 - @@ -OldStart,OldCount +NewStart,NewCount @@ Heading
public sealed record Hunk(int OldStart, int OldCount, int NewStart, int NewCount, string Heading, List<DiffLine> Lines)
{
    public string Header => $"@@ -{OldStart},{OldCount} +{NewStart},{NewCount} @@{(Heading.Length > 0 ? " " + Heading : "")}";
}

public enum FileStatus { Modified, Added, Deleted, Renamed, Copied, TypeChanged }

// 파일 하나의 변경
public sealed class FileDiff
{
    public string OldPath { get; set; } = "";
    public string NewPath { get; set; } = "";
    public FileStatus Status { get; set; } = FileStatus.Modified;
    public bool IsBinary { get; set; }
    public int Similarity { get; set; }
    public string? OldMode { get; set; }
    public string? NewMode { get; set; }
    public List<Hunk> Hunks { get; } = [];

    // 화면에 쓸 경로 - 삭제는 옛 경로
    public string Path => Status == FileStatus.Deleted ? OldPath : NewPath;
    public int Added => Hunks.Sum(h => h.Lines.Count(l => l.Kind == LineKind.Add));
    public int Deleted => Hunks.Sum(h => h.Lines.Count(l => l.Kind == LineKind.Del));
}
