using Avalonia.Controls;
using Avalonia.Input.Platform;
using Ieummae.Core.Git;
using Ieummae.Core.Log;

namespace Ieummae.App.Windows;

// 저장소 밖 동작 - 끝나면 다음 창(새 저장소 로그·커밋)을 열고 닫힘
public partial class ToolWindow : IeumWindow
{
    public ToolWindow() : this("", "", _ => Task.FromResult<Window?>(null)) { }

    public ToolWindow(string title, string dir, Func<ToolWindow, Task<Window?>> run)
    {
        InitializeComponent();
        TitleText.Text = title;
        DirText.Text = dir;
        Opened += async (_, _) =>
        {
            var next = await run(this);
            next?.Show();
            Close();
        };
    }

    // 복제 - 주소(클립보드에 주소 있으면 미리 채움)·폴더 이름 받아 진행 상자로
    public static ToolWindow Clone(string parent) => new("Clone", parent, async w =>
    {
        var clip = w.Clipboard is { } cb ? await cb.TryGetTextAsync() ?? "" : "";
        var guess = clip.Trim() is var c && (c.StartsWith("http") || c.StartsWith("git@") || c.EndsWith(".git")) && !c.Contains('\n') ? c : "";
        if (await w.PromptAsync("Clone", "저장소 주소", guess, "Next") is not { } url) return null;
        if (await w.PromptAsync("Clone", $"{parent} 아래 만들 폴더 이름", GitTools.FolderFromUrl(url.Text), "Clone") is not { } folder) return null;
        var r = await w.ProgressAsync("Clone", (l, ct) => GitTools.CloneAsync(parent, url.Text, folder.Text, l, ct));
        if (!r.Ok || Repository.Discover(Path.Combine(parent, folder.Text)) is not { } repo) return null;
        var model = new RepoModel(repo);
        return new LogWindow(new LogModel(model, new LogQuery())) { Title = $"{repo.Name} · Log" };
    });

    // 새 저장소 - 확인 후 git init, 이어서 첫 커밋 창
    public static ToolWindow Init(string dir) => new("Init", dir, async w =>
    {
        if (Repository.Discover(dir) is { } existing)
        {
            await w.AlertAsync("Init", $"이미 저장소 안 - {existing.Root}");
            return null;
        }
        if (!await w.ConfirmAsync("Init", $"{dir} 에 새 Git 저장소 만들기", "Init")) return null;
        if (!await w.CheckAsync("Init", GitTools.InitAsync(dir)) || Repository.Discover(dir) is not { } repo) return null;
        var model = new RepoModel(repo);
        var commit = new CommitWindow(new CommitModel(model, null)) { Title = $"{repo.Name} · Commit" };
        commit.Opened += (_, _) => _ = model.LoadAsync();
        return commit;
    });
}
