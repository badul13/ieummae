using Avalonia.Controls;
using Ieummae.Core;
using Ieummae.Core.Git;
using Ieummae.Core.Log;

namespace Ieummae.App.Windows;

// 명령 → 창 하나와 첫 화면. 명령마다 새 프로세스, 그 뒤 이동은 창 안에서
public static class Launcher
{
    static string? Rel(RepoModel m, CommandLine c) => m.Repo.Relative(c.Path) is { Length: > 0 } p ? p : null;

    // 첫 화면이 있는 명령
    static readonly Dictionary<string, Func<RepoModel, CommandLine, Page>> Pages = new()
    {
        // 하위 폴더·파일이면 그 경로만의 로그
        ["log"] = (m, c) => new LogPage(new LogModel(m, new LogQuery(Path: Rel(m, c)))),
        ["commit"] = (m, c) => new CommitPage(new CommitModel(m, Rel(m, c))),
        // 파일 하나 - 작업 트리 변경 (HEAD 대비)
        ["diff"] = (m, c) => new DiffPage(m, null, m.Repo.Relative(c.Path), "작업 트리 · HEAD 대비"),
        ["blame"] = (m, c) => new BlamePage(m, m.Repo.Relative(c.Path)),
        // 충돌 해결 - 병합·리베이스 중 남은 충돌
        ["conflicts"] = (m, _) => new ConflictPage(new ConflictModel(m)),
        ["settings"] = (m, _) => new SettingsPage(m.Repo),
    };

    // 동작 하나짜리 (탐색기 메뉴) - 로그 화면을 띄우고 그 위에서 실행
    static readonly Dictionary<string, Func<Actions, Task<bool>>> Runs = new()
    {
        ["fetch"] = a => a.FetchAsync(),
        ["pull"] = a => a.PullAsync(),
        ["push"] = a => a.PushAsync(),
        ["switch"] = a => a.SwitchAsync(),
        ["branch"] = a => a.NewBranchAsync(),
        ["merge"] = a => a.MergeAsync(),
        ["rebase"] = a => a.RebaseAsync(),
        ["stash"] = a => a.StashAsync(),
        ["stash-list"] = a => a.StashesAsync(),
        ["tags"] = a => a.TagsAsync(),
        ["remotes"] = a => a.RemotesAsync(),
    };

    public static Window Create(CommandLine cmd)
    {
        var w = CreateWindow(cmd);
        // 측정 도구가 창을 찾는 제목
        if (cmd.BenchId is { } id) { w.FixedTitle = "ieummae-" + id; w.Title = w.FixedTitle; }
        return w;
    }

    static MainWindow CreateWindow(CommandLine cmd)
    {
        var dir = Directory.Exists(cmd.Path) ? cmd.Path : Path.GetDirectoryName(cmd.Path)!;
        // 저장소 없이도 되는 명령
        switch (cmd.Name)
        {
            case "clone": return Tools.Clone(dir);
            case "init": return Tools.Init(dir);
            case "settings" when Repository.Discover(cmd.Path) is null: return new MainWindow(new SettingsPage(null), "");
        }
        if (!Pages.ContainsKey(cmd.Name) && !Runs.ContainsKey(cmd.Name))
            return new MainWindow(new MessagePage("알 수 없는 명령",
                $"{cmd.Name} · 사용 가능: {string.Join(", ", Pages.Keys.Concat(Runs.Keys).Concat(["clone", "init"]))}"), "");
        if (Repository.Discover(cmd.Path) is not { } repo)
            return new MainWindow(new MessagePage("Git 저장소 아님", cmd.Path), "");

        var model = new RepoModel(repo);
        if (Pages.TryGetValue(cmd.Name, out var page)) return new MainWindow(page(model, cmd), repo.Name);
        var log = new LogPage(new LogModel(model, new LogQuery())) { StartAction = Runs[cmd.Name] };
        return new MainWindow(log, repo.Name);
    }
}
