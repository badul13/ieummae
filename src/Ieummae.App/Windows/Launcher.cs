using Avalonia.Controls;
using Ieummae.Core;
using Ieummae.Core.Git;

namespace Ieummae.App.Windows;

// 명령 → 창. 명령마다 새 프로세스라 창 하나만 띄움
public static class Launcher
{
    static readonly Dictionary<string, (string Title, Func<RepoModel, CommandLine, Window> Create)> Commands = new()
    {
        ["log"] = ("Log", (m, _) => new LogWindow(m)),
        ["commit"] = ("Commit", (m, _) => new CommitWindow(m)),
        // 파일 하나 - 작업 트리 변경 (HEAD 대비)
        ["diff"] = ("Diff", (m, c) => new DiffWindow(m, null, m.Repo.Relative(c.Path), "작업 트리 · HEAD 대비")),
    };

    public static Window Create(CommandLine cmd)
    {
        Window w;
        if (!Commands.TryGetValue(cmd.Name, out var c))
            w = new MessageWindow("알 수 없는 명령", $"{cmd.Name} · 사용 가능: {string.Join(", ", Commands.Keys)}");
        else if (Repository.Discover(cmd.Path) is not { } repo)
            w = new MessageWindow("Git 저장소 아님", cmd.Path);
        else
        {
            var model = new RepoModel(repo);
            w = c.Create(model, cmd);
            w.Title = $"{repo.Name} · {c.Title}";
            // 창이 뜬 뒤 git 조회 - 창 표시를 기다리게 하지 않음
            w.Opened += (_, _) => _ = model.LoadAsync();
        }
        // 측정 도구가 창을 찾는 제목
        if (cmd.BenchId is { } id) w.Title = "ieummae-" + id;
        return w;
    }
}
