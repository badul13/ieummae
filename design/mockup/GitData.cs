using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace IeumMock;

// 시안용 실제 저장소 읽기 - git CLI 호출 + 출력 파싱
public static class GitData
{
    public static string Repo = System.Environment.CurrentDirectory;

    public static string Run(params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in new[] { "-C", Repo, "-c", "core.quotepath=false", "-c", "color.ui=never" }.Concat(args)) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return o;
    }

    public static string RepoName => System.IO.Path.GetFileName(Repo.TrimEnd('\\'));
    public static string Branch => Run("branch", "--show-current").Trim();

    public static int Ahead
    {
        get { int.TryParse(Run("rev-list", "--count", "@{u}..HEAD").Trim(), out var n); return n; }
    }

    // 로그 + 레인 계산 - 기대 커밋 해시를 레인마다 들고 위에서 아래로
    public static List<Commit> Log(int max)
    {
        var raw = Run("log", "--all", "--topo-order", $"-n{max}", "--format=%H%x1f%P%x1f%an%x1f%ct%x1f%D%x1f%s%x1f%b%x1e");
        var list = new List<Commit>();
        var lanes = new List<string?>();
        foreach (var rec in raw.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = rec.TrimStart('\n', '\r').Split('\x1f');
            if (f.Length < 7) continue;
            string hash = f[0];
            var parents = f[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var up = new List<int>();
            for (int i = 0; i < lanes.Count; i++) if (lanes[i] == hash) up.Add(i);
            var pass = new List<int>();
            for (int i = 0; i < lanes.Count; i++) if (lanes[i] != null && lanes[i] != hash) pass.Add(i);

            int dot;
            if (up.Count > 0) dot = up[0];
            else { dot = lanes.IndexOf(null); if (dot < 0) { dot = lanes.Count; lanes.Add(null); } }
            foreach (var i in up) lanes[i] = null;

            var down = new List<int>();
            if (parents.Length > 0) { lanes[dot] = parents[0]; down.Add(dot); }
            for (int k = 1; k < parents.Length; k++)
            {
                int j = lanes.IndexOf(parents[k]);
                if (j < 0) { j = lanes.IndexOf(null); if (j < 0) { j = lanes.Count; lanes.Add(null); } lanes[j] = parents[k]; }
                down.Add(j);
            }
            while (lanes.Count > 0 && lanes[^1] == null) lanes.RemoveAt(lanes.Count - 1);

            var when = DateTimeOffset.FromUnixTimeSeconds(long.Parse(f[3])).ToLocalTime();
            var c = new Commit
            {
                Hash = hash[..8], FullHash = hash, Author = f[2], Msg = f[5], Body = f[6].Trim(),
                Date = when.ToString("MM-dd"), DateText = when.ToString("yyyy-MM-dd HH:mm"), Rel = Rel(when),
                Dot = dot, Pass = pass.ToArray(), Up = up.ToArray(), Down = down.ToArray(), Parents = parents,
            };
            foreach (var d in f[4].Split(", ", StringSplitOptions.RemoveEmptyEntries))
            {
                if (d == "HEAD" || d.EndsWith("/HEAD")) continue;
                if (d.StartsWith("HEAD -> ")) c.Badges.Add(new Badge { Text = d[8..], IsHead = true, Lane = dot });
                else if (d.StartsWith("tag: ")) c.Badges.Add(new Badge { Text = d[5..], IsTag = true, Lane = dot });
                else c.Badges.Add(new Badge { Text = d, Lane = dot });
            }
            // 로컬과 같은 위치의 origin/같은이름 - 로컬 하나로 합침
            c.Badges.RemoveAll(b => b.Text.StartsWith("origin/") && c.Badges.Exists(l => l.Text == b.Text[7..]));
            GraphCell.MaxLane = Math.Max(GraphCell.MaxLane, pass.Concat(up).Concat(down).Append(dot).Max());
            list.Add(c);
        }
        return list;
    }

    static string Rel(DateTimeOffset t)
    {
        var d = DateTimeOffset.Now - t;
        if (d.TotalMinutes < 1) return "방금";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes}분 전";
        if (d.TotalDays < 1) return $"{(int)d.TotalHours}시간 전";
        if (d.TotalDays < 2) return "어제";
        if (d.TotalDays < 30) return $"{(int)d.TotalDays}일 전";
        if (d.TotalDays < 365) return $"{(int)(d.TotalDays / 30)}개월 전";
        return $"{(int)(d.TotalDays / 365)}년 전";
    }

    // 커밋 변경 파일 - 병합 커밋은 첫 부모 기준
    public static List<FileChange> Files(Commit c)
    {
        string range = c.Parents.Length > 0 ? c.Parents[0] : "4b825dc642cb6eb9a060e54bf8d69288fbee4904";
        // 두 호출 동시 실행 - git 프로세스 시작 비용이 대부분이라 순서대로 하면 두 배
        var numTask = System.Threading.Tasks.Task.Run(() => Run("diff", "--numstat", "-M", range, c.FullHash));
        var st = Run("diff", "--name-status", "-M", range, c.FullHash);
        var num = numTask.Result;
        var status = new Dictionary<string, string>();
        foreach (var line in st.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Split('\t');
            status[p[^1]] = p[0][..1];
        }
        var list = new List<FileChange>();
        foreach (var line in num.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Split('\t');
            if (p.Length < 3) continue;
            string path = p[2].Contains("=>") ? status.Keys.FirstOrDefault(k => p[2].Contains(System.IO.Path.GetFileName(k))) ?? p[2] : p[2];
            int.TryParse(p[0], out var a); int.TryParse(p[1], out var dl);
            list.Add(new FileChange { Status = status.GetValueOrDefault(path, "M"), Path = path, Add = a, Del = dl });
        }
        return list;
    }

    // 파일 하나 diff - unified 출력 파싱, 줄 번호 계산
    public static List<DiffLine> Diff(Commit c, string path, int maxLines = 600)
    {
        string range = c.Parents.Length > 0 ? c.Parents[0] : "4b825dc642cb6eb9a060e54bf8d69288fbee4904";
        var raw = Run("diff", "-M", "-U3", range, c.FullHash, "--", path);
        var list = new List<DiffLine>();
        int o = 0, n = 0;
        bool body = false;
        foreach (var l in raw.Split('\n'))
        {
            if (l.StartsWith("@@"))
            {
                body = true;
                var m = System.Text.RegularExpressions.Regex.Match(l, @"-(\d+)(?:,\d+)? \+(\d+)");
                o = int.Parse(m.Groups[1].Value); n = int.Parse(m.Groups[2].Value);
                list.Add(new DiffLine { Kind = "@", Text = l.TrimEnd('\r') });
                continue;
            }
            if (!body || l.Length == 0) continue;
            string text = l[1..].TrimEnd('\r');
            switch (l[0])
            {
                case '+': list.Add(new DiffLine { Kind = "+", New = n++.ToString(), Text = text }); break;
                case '-': list.Add(new DiffLine { Kind = "-", Old = o++.ToString(), Text = text }); break;
                case ' ': list.Add(new DiffLine { Kind = " ", Old = o++.ToString(), New = n++.ToString(), Text = text }); break;
            }
            if (list.Count >= maxLines) break;
        }
        return list;
    }
}
