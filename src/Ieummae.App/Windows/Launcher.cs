using Avalonia.Controls;
using Ieummae.Core;
using Ieummae.Core.Git;
using Ieummae.Core.Log;

namespace Ieummae.App.Windows;

// 명령 → 창. 명령마다 새 프로세스라 창 하나만 띄움
public static class Launcher
{
    static readonly Dictionary<string, (string Title, Func<RepoModel, CommandLine, Window> Create)> Commands = new()
    {
        // 하위 폴더·파일이면 그 경로만의 로그
        ["log"] = ("Log", (m, c) => new LogWindow(new LogModel(m, new LogQuery(Path: m.Repo.Relative(c.Path) is { Length: > 0 } p ? p : null)))),
        ["commit"] = ("Commit", (m, c) => new CommitWindow(new CommitModel(m, m.Repo.Relative(c.Path) is { Length: > 0 } p ? p : null))),
        // 파일 하나 - 작업 트리 변경 (HEAD 대비)
        ["diff"] = ("Diff", (m, c) => new DiffWindow(m, null, m.Repo.Relative(c.Path), "작업 트리 · HEAD 대비")),
        // 동작 하나짜리 - 탐색기 메뉴용
        ["fetch"] = ("Fetch", (m, _) => new ActionWindow(m, "Fetch", a => a.FetchAsync())),
        ["pull"] = ("Pull", (m, _) => new ActionWindow(m, "Pull", a => a.PullAsync())),
        ["push"] = ("Push", (m, _) => new ActionWindow(m, "Push", a => a.PushAsync())),
        ["switch"] = ("Switch", (m, _) => new ActionWindow(m, "Switch", a => a.SwitchAsync())),
        ["branch"] = ("New Branch", (m, _) => new ActionWindow(m, "New Branch", a => a.NewBranchAsync())),
        ["merge"] = ("Merge", (m, _) => new ActionWindow(m, "Merge", a => a.MergeAsync())),
        ["rebase"] = ("Rebase", (m, _) => new ActionWindow(m, "Rebase", a => a.RebaseAsync())),
        ["stash"] = ("Stash", (m, _) => new ActionWindow(m, "Stash", a => a.StashAsync())),
        ["stash-list"] = ("Stash List", (m, _) => new ActionWindow(m, "Stash List", a => a.StashesAsync())),
        ["tags"] = ("Tags", (m, _) => new ActionWindow(m, "Tags", a => a.TagsAsync())),
        ["remotes"] = ("Remotes", (m, _) => new ActionWindow(m, "Remotes", a => a.RemotesAsync())),
        // 충돌 해결 - 병합·리베이스 중 남은 충돌
        ["conflicts"] = ("Conflicts", (m, _) => new ConflictWindow(new ConflictModel(m))),
        // 파일 하나 - 줄마다 마지막 수정 커밋
        ["blame"] = ("Blame", (m, c) => new BlameWindow(m, m.Repo.Relative(c.Path))),
    };

    // 저장소 없이도 되는 명령 - 경로는 대상 폴더
    static readonly Dictionary<string, Func<CommandLine, Window>> Tools = new()
    {
        ["clone"] = c => ToolWindow.Clone(Directory.Exists(c.Path) ? c.Path : Path.GetDirectoryName(c.Path)!),
        ["init"] = c => ToolWindow.Init(Directory.Exists(c.Path) ? c.Path : Path.GetDirectoryName(c.Path)!),
        ["settings"] = c => new SettingsWindow(Repository.Discover(c.Path)),
    };

    public static Window Create(CommandLine cmd)
    {
        Window w;
        if (Tools.TryGetValue(cmd.Name, out var tool))
            w = tool(cmd);
        else if (!Commands.TryGetValue(cmd.Name, out var c))
            w = new MessageWindow("알 수 없는 명령", $"{cmd.Name} · 사용 가능: {string.Join(", ", Commands.Keys.Concat(Tools.Keys))}");
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
