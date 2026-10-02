using Avalonia.Input.Platform;
using Ieummae.Core.Git;
using Ieummae.Core.Log;

namespace Ieummae.App.Windows;

// 저장소 밖 동작 - 안내 화면 위 대화 상자로 진행하고, 끝나면 새 저장소 화면으로 바꿈 (취소면 창 닫힘)
public static class Tools
{
    // 복제 - 주소(클립보드에 주소 있으면 미리 채움)·폴더 이름 받아 진행 상자로, 끝나면 새 저장소 로그
    public static MainWindow Clone(string parent)
    {
        var w = new MainWindow(new MessagePage("Clone", parent), "");
        w.Opened += async (_, _) =>
        {
            var clip = w.Clipboard is { } cb ? await cb.TryGetTextAsync() ?? "" : "";
            var c = clip.Trim();
            var guess = (c.StartsWith("http") || c.StartsWith("git@") || c.EndsWith(".git")) && !c.Contains('\n') ? c : "";
            if (await w.PromptAsync("Clone", "저장소 주소", guess, "Next") is not { } url) { w.Close(); return; }
            if (await w.PromptAsync("Clone", $"{parent} 아래 만들 폴더 이름", GitTools.FolderFromUrl(url.Text), "Clone") is not { } folder) { w.Close(); return; }
            var r = await w.ProgressAsync("Clone", (l, ct) => GitTools.CloneAsync(parent, url.Text, folder.Text, l, ct));
            if (!r.Ok || Repository.Discover(Path.Combine(parent, folder.Text)) is not { } repo) { w.Close(); return; }
            w.RepoName = repo.Name;
            w.Replace(new LogPage(new LogModel(new RepoModel(repo), new LogQuery())));
        };
        return w;
    }

    // 새 저장소 - 확인 후 git init, 이어서 첫 커밋 화면
    public static MainWindow Init(string dir)
    {
        var w = new MainWindow(new MessagePage("Init", dir), "");
        w.Opened += async (_, _) =>
        {
            if (Repository.Discover(dir) is { } existing)
            {
                await w.AlertAsync("Init", $"이미 저장소 안 - {existing.Root}");
                w.Close();
                return;
            }
            if (!await w.ConfirmAsync("Init", $"{dir} 에 새 Git 저장소 만들기", "Init")
                || !await w.CheckAsync("Init", GitTools.InitAsync(dir))
                || Repository.Discover(dir) is not { } repo) { w.Close(); return; }
            w.RepoName = repo.Name;
            w.Replace(new CommitPage(new CommitModel(new RepoModel(repo), null)));
        };
        return w;
    }
}
