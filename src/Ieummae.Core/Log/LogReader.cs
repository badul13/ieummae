using System.Runtime.CompilerServices;
using Ieummae.Core.Git;

namespace Ieummae.Core.Log;

// 로그 범위 - 전체 브랜치 또는 현재 브랜치, 경로 한정
public sealed record LogQuery(bool AllRefs = true, string? Path = null);

// git log 스트리밍 - 한 줄 = 커밋 하나, 받는 대로 레인까지 계산해서 돌려줌
public static class LogReader
{
    // 필드 구분 \x1f - 제목에 나올 수 없는 제어 문자
    const string Format = "%H%x1f%P%x1f%an%x1f%ae%x1f%at%x1f%D%x1f%s";

    public static List<string> Args(LogQuery q)
    {
        var args = new List<string> { "log", "--topo-order", "--no-color", $"--format={Format}", "--decorate=full" };
        // 스태시는 로그에서 뺌 (--exclude 는 --all 앞에 와야 적용)
        if (q.AllRefs) { args.Add("--exclude=refs/stash"); args.Add("--all"); }
        // 경로 한정 - 부모를 그 경로 기준으로 다시 이어야 그래프가 끊기지 않음
        if (!string.IsNullOrEmpty(q.Path)) { args.Add("--parents"); args.Add("--"); args.Add(q.Path); }
        return args;
    }

    public static async IAsyncEnumerable<LogEntry> ReadAsync(Repository repo, LogQuery q, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var graph = new GraphBuilder();
        await foreach (var line in Git.Git.StreamLinesAsync(repo.Root, Args(q), ct).ConfigureAwait(false))
        {
            if (Parse(line) is not { } e) continue;
            graph.Add(e);
            yield return e;
        }
    }

    public static LogEntry? Parse(string line)
    {
        var f = line.Split('\x1f');
        if (f.Length < 7) return null;
        bool detached = false;
        var refs = new List<RefLabel>();
        foreach (var d in f[5].Split(", ", StringSplitOptions.RemoveEmptyEntries))
        {
            if (d == "HEAD") { detached = true; continue; }
            if (d.StartsWith("HEAD -> ")) { refs.Add(new RefLabel(Short(d[8..]), RefKind.Head)); continue; }
            if (d.StartsWith("tag: ")) { refs.Add(new RefLabel(Short(d[5..]), RefKind.Tag)); continue; }
            // 원격 HEAD 표시는 생략 (origin/HEAD)
            if (d.StartsWith("refs/remotes/") && d.EndsWith("/HEAD")) continue;
            if (d.StartsWith("refs/remotes/")) refs.Add(new RefLabel(d[13..], RefKind.Remote));
            else if (d.StartsWith("refs/heads/")) refs.Add(new RefLabel(d[11..], RefKind.Branch));
            else if (d.StartsWith("refs/stash")) continue;
            else refs.Add(new RefLabel(Short(d), RefKind.Branch));
        }
        return new LogEntry
        {
            Hash = f[0],
            Parents = f[1].Split(' ', StringSplitOptions.RemoveEmptyEntries),
            Author = f[2],
            Email = f[3],
            Time = DateTimeOffset.FromUnixTimeSeconds(long.TryParse(f[4], out var t) ? t : 0),
            Refs = refs,
            DetachedHead = detached,
            Subject = f[6],
        };
    }

    static string Short(string r) =>
        r.StartsWith("refs/heads/") ? r[11..] : r.StartsWith("refs/tags/") ? r[10..] : r.StartsWith("refs/remotes/") ? r[13..] : r;
}
