using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Ieummae.App.Platform;
using Ieummae.Core.Git;
using Ieummae.Core.Log;

namespace Ieummae.App.Windows;

// 커밋 창 - 고른 파일만 커밋, 파일 우클릭으로 되돌리기·삭제·ignore
public partial class CommitWindow : IeumWindow
{
    public CommitModel? Model { get; }
    Repository Repo => Model!.Repo.Repo;

    public CommitWindow() : this(null) { }

    public CommitWindow(CommitModel? model)
    {
        InitializeComponent();
        DataContext = Model = model;
        if (model is null) return;

        Opened += async (_, _) => { await model.LoadAsync(); SelectFirst(); MessageBox.Focus(); };
        FileList.SelectionChanged += (_, _) => ShowDiff();
        AllButton.Click += (_, _) => model.ToggleAll();
        RefreshButton.Click += async (_, _) => await RefreshAsync();
        LogButton.Click += (_, _) => new LogWindow(new LogModel(model.Repo, new LogQuery())).Show();
        CommitButton.Click += async (_, _) => await CommitAsync();
        HistoryButton.Click += (_, _) => ShowHistory();
        KeyDown += async (_, e) =>
        {
            if (DialogOpen) return;
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control && model.CanCommit) { e.Handled = true; await CommitAsync(); }
            else if (e.Key == Key.F5) await RefreshAsync();
        };
        // 스페이스 - 고른 줄들 체크 전환
        FileList.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Space) return;
            var rows = Selected();
            bool to = !rows.All(r => r.Checked);
            foreach (var r in rows.Where(r => r.CanCheck)) r.Checked = to;
            e.Handled = true;
        };
        var menu = new MenuFlyout();
        menu.Opening += (_, _) => BuildFileMenu(menu);
        FileList.ContextFlyout = menu;
    }

    List<FileRow> Selected() => FileList.SelectedItems?.OfType<FileRow>().ToList() ?? [];

    void SelectFirst()
    {
        if (Model!.Files.Count > 0 && FileList.SelectedItem is null) FileList.SelectedIndex = 0;
        else if (Model.Files.Count == 0) Diff.Clear();
    }

    async Task RefreshAsync()
    {
        var keep = (FileList.SelectedItem as FileRow)?.File.Path;
        await Task.WhenAll(Model!.LoadAsync(), Model.Repo.LoadAsync());
        var again = Model.Files.FirstOrDefault(f => f.File.Path == keep);
        if (again is not null) FileList.SelectedItem = again; else SelectFirst();
        ShowDiff();
    }

    void ShowDiff()
    {
        if (FileList.SelectedItem is not FileRow row) { Diff.Clear(); return; }
        var f = row.File;
        DiffSpec spec = f.IsUntracked ? new DiffSpec.Untracked() : new DiffSpec.WorkingTree();
        var paths = f.OrigPath is { } o ? new[] { o, f.Path } : [f.Path];
        _ = Diff.LoadAsync(async (opt, ct) => (await Repo.DiffAsync(spec, paths, opt, ct)).FirstOrDefault(d => d.Path == f.Path || d.NewPath == f.Path));
    }

    async Task CommitAsync()
    {
        var m = Model!;
        if (!m.CanCommit) return;
        m.Busy = true;
        try
        {
            var message = m.Message;
            if (!await CheckAsync("Commit", m.CommitAsync())) { await m.LoadAsync(); return; }
            MessageHistory.Add(message);
            if (m.Push)
            {
                var r = await ProgressAsync("Push", (onLine, ct) => Repo.PushAsync(onLine, ct));
                // 커밋은 됐으므로 Push 실패해도 창은 남겨 다시 시도할 수 있게
                if (!r.Ok) { m.Message = ""; m.Amend = false; await RefreshAsync(); return; }
            }
            Close();
        }
        finally { m.Busy = false; }
    }

    void ShowHistory()
    {
        var list = Model!.History();
        var menu = new MenuFlyout { Placement = PlacementMode.TopEdgeAlignedRight };
        if (list.Count == 0) menu.Items.Add(new MenuItem { Header = "기록 없음", IsEnabled = false });
        foreach (var msg in list)
        {
            var first = msg.Split('\n')[0];
            var mi = new MenuItem { Header = first.Length > 60 ? first[..60] + "..." : first };
            mi.Click += (_, _) => { Model.Message = msg; MessageBox.Focus(); };
            menu.Items.Add(mi);
        }
        menu.ShowAt(HistoryButton);
    }

    void BuildFileMenu(MenuFlyout menu)
    {
        menu.Items.Clear();
        var rows = Selected();
        if (rows.Count == 0) return;
        void Item(string header, Func<Task> run)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += async (_, _) => await run();
            menu.Items.Add(mi);
        }
        var tracked = rows.Where(r => !r.File.IsUntracked && !r.IsConflict).Select(r => r.File).ToList();
        var untracked = rows.Where(r => r.File.IsUntracked).Select(r => r.File).ToList();

        if (rows.Count == 1)
        {
            var f = rows[0].File;
            Item("Diff", () => { new DiffWindow(Model!.Repo, null, f.Path, "작업 트리 · HEAD 대비").Show(); return Task.CompletedTask; });
            Item("탐색기에서 보기", () => { OpenInExplorer(f.Path); return Task.CompletedTask; });
            menu.Items.Add(new Separator());
        }
        if (tracked.Count > 0)
            Item(tracked.Count == 1 ? "Revert" : $"Revert {tracked.Count}", async () =>
            {
                var names = string.Join(", ", tracked.Take(3).Select(t => t.Name)) + (tracked.Count > 3 ? $" 외 {tracked.Count - 3}개" : "");
                if (!await ConfirmAsync("Revert", $"{names} 변경 버림 - HEAD 상태로 되돌림", "Revert")) return;
                await CheckAsync("Revert", Repo.RevertFilesAsync(tracked));
                await RefreshAsync();
            });
        if (untracked.Count > 0)
        {
            Item(untracked.Count == 1 ? "Delete" : $"Delete {untracked.Count}", async () =>
            {
                if (!await ConfirmAsync("Delete", $"추적 안 하는 파일 {untracked.Count}개 휴지통으로", "Delete")) return;
                if (!RecycleBin.Send(untracked.Select(u => Path.Combine(Repo.Root, u.Path))))
                    await AlertAsync("Delete 실패", "휴지통으로 보내지 못함");
                await RefreshAsync();
            });
            Item("Add to .gitignore", () => IgnoreAsync(untracked));
        }
    }

    async Task IgnoreAsync(List<WorkingFile> files)
    {
        var f = files[0];
        var ext = Path.GetExtension(f.Path);
        var options = new List<(string, string)> { (files.Count == 1 ? "/" + f.Path : $"고른 파일 {files.Count}개", "이 파일만") };
        if (ext.Length > 0) options.Add(("*" + ext, $"{ext} 파일 전부"));
        if (f.Dir.Length > 0) options.Add(("/" + f.Dir + "/", "이 폴더 전부"));
        int pick = await ChooseAsync("Add to .gitignore", "무시할 범위", options, "Add");
        if (pick < 0) return;
        if (pick == 0) foreach (var x in files) Repo.AddToIgnore("/" + x.Path);
        else Repo.AddToIgnore(options[pick].Item1);
        await RefreshAsync();
    }

    void OpenInExplorer(string rel)
    {
        var full = Path.Combine(Repo.Root, rel.Replace('/', '\\'));
        var arg = File.Exists(full) ? $"/select,\"{full}\"" : $"\"{Path.GetDirectoryName(full)}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", arg) { UseShellExecute = false });
    }
}
