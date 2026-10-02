using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Ieummae.App.Theme;
using Ieummae.App.Views;
using Ieummae.Core.Git;
using Ieummae.Core.Log;

namespace Ieummae.App.Windows;

// 로그 화면 - 커밋 목록, 행 우클릭 동작, 변경 파일 diff. 다른 화면에서 돌아오면 새로 고침
public partial class LogPage : Page
{
    public LogModel? Model { get; }
    Repository Repo => Model!.Repo.Repo;

    public LogPage() : this(null) { }

    public override string PageTitle => Model?.HasPath == true ? "Log · " + Model.PathText : "Log";

    public LogPage(LogModel? model)
    {
        InitializeComponent();
        DataContext = Model = model;
        if (model is null) return;

        Shown += async first =>
        {
            if (!first) { await ReloadAsync(); return; }
            await Task.WhenAll(model.LoadAsync(), model.Repo.LoadAsync());
            // 탐색기 메뉴의 Pull 등 - 로그가 뜬 뒤 바로 실행
            if (StartAction is { } start && Actions is { } act) { StartAction = null; Run(() => start(act)); }
        };
        AttachMenu(List, BuildRowMenu);
        FileList.DoubleTapped += (_, _) => OpenDiff();
        FileList.KeyDown += (_, e) => { if (e.Key == Key.Enter) OpenDiff(); };
        CommitButton.Click += (_, _) => OpenCommit();
        // 테마 전환 - 이름표 점 색은 만들 때 정해지므로 목록 다시 연결
        AttachedToVisualTree += (_, _) => Tone.Changed += RebindRows;
        DetachedFromVisualTree += (_, _) => Tone.Changed -= RebindRows;

        // 원격·브랜치 동작 - 끝나면 새로 고침
        var act = new Actions(this, model.Repo);
        // Fetch·Pull·Push - 팝업 없이 누른 버튼의 박음질이 돌아감. 돌아가는 버튼을 다시 누르면 취소
        act.Runner = (title, work) => RunOnButtonAsync(title switch { "Fetch" => FetchButton, "Pull" => PullButton, _ => PushButton }, title, work);
        FetchButton.Click += (_, _) => { if (!CancelBusy(FetchButton)) Run(act.FetchAsync); };
        PullButton.Click += (_, _) => { if (!CancelBusy(PullButton)) Run(act.PullAsync); };
        PushButton.Click += (_, _) => { if (!CancelBusy(PushButton)) Run(act.PushAsync); };
        Chip.Tapped += (_, _) => Run(act.SwitchAsync);
        var more = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        void Add(string header, Func<Task<bool>> run)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => Run(run);
            more.Items.Add(mi);
        }
        Add("Switch", act.SwitchAsync);
        Add("New Branch", act.NewBranchAsync);
        Add("Delete Branch", act.DeleteBranchAsync);
        Add("Merge", act.MergeAsync);
        Add("Rebase", act.RebaseAsync);
        more.Items.Add(new Separator());
        Add("Stash", act.StashAsync);
        Add("Stash List", act.StashesAsync);
        more.Items.Add(new Separator());
        Add("Tags", act.TagsAsync);
        Add("Remotes", act.RemotesAsync);
        more.Items.Add(new Separator());
        var settings = new MenuItem { Header = "설정" };
        settings.Click += (_, _) => Navigate(new SettingsPage(model.Repo.Repo));
        more.Items.Add(settings);
        MoreButton.Flyout = more;
        act.OnConflicts = () => { OpenConflicts(); return Task.CompletedTask; };
        ResolveButton.Click += (_, _) => OpenConflicts();
        Actions = act;
    }

    public Actions? Actions { get; }

    // 커밋 화면에서 Push 체크하고 돌아왔을 때
    public void PushNow() { if (Actions is { } a) Run(a.PushAsync); }

    // 처음 보일 때 한 번 실행할 동작
    public Func<Actions, Task<bool>>? StartAction { get; set; }

    public override void OnKey(KeyEventArgs e)
    {
        if (e.Key == Key.F5) { _ = ReloadAsync(); e.Handled = true; }
    }

    // 동작 하나 실행 - 대화 상자 열려 있으면 무시, 끝나면 목록·브랜치 새로 고침
    async void Run(Func<Task<bool>> action)
    {
        if (DialogOpen) return;
        if (await action()) await ReloadAsync();
        else await Model!.Repo.LoadAsync();
    }

    void RebindRows()
    {
        var sel = Model!.Selected;
        List.ItemsSource = null;
        List.ItemsSource = Model.Rows;
        Model.Selected = sel;
    }

    // 다시 불러오기 - 고른 커밋 유지, 브랜치 이름도 갱신
    public async Task ReloadAsync()
    {
        var keep = Model!.Selected?.Entry.Hash;
        await Task.WhenAll(Model.LoadAsync(keep), Model.Repo.LoadAsync());
    }

    void OpenDiff()
    {
        if (Model?.Selected is not { } row || FileList.SelectedItem is not ChangedFile f) return;
        var e = row.Entry;
        Navigate(new DiffPage(Model.Repo, new DiffSpec.Commit(e.Hash, e.Parents.FirstOrDefault()), f.Path, $"{e.Short} · {e.Subject}"));
    }

    void OpenConflicts() => Navigate(new ConflictPage(new ConflictModel(Model!.Repo)));

    void OpenCommit() => Navigate(new CommitPage(new CommitModel(Model!.Repo, null)));

    List<LogRow> SelectedRows() => List.SelectedItems?.OfType<LogRow>().ToList() ?? [];

    void BuildRowMenu(MenuFlyout menu)
    {
        menu.Items.Clear();
        var rows = SelectedRows();
        void Item(string header, Func<Task> run, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled };
            mi.Click += async (_, _) => await run();
            menu.Items.Add(mi);
        }
        void Sep() => menu.Items.Add(new Separator());

        if (rows.Count == 2)
        {
            // 두 커밋 비교 - 위(최신)가 새 쪽
            var ordered = rows.OrderBy(IndexOf).ToList();
            Item("Compare", () => { OpenCompare(ordered[1].Entry, ordered[0].Entry); return Task.CompletedTask; });
            return;
        }
        if (rows.Count != 1) return;
        var e = rows[0].Entry;
        bool isHead = e.IsHead;
        foreach (var b in e.Refs.Where(r => r.Kind == RefKind.Branch))
            Item($"Checkout {b.Name}", () => Act("Checkout", Repo.CheckoutAsync(b.Name)));
        foreach (var b in e.Refs.Where(r => r.Kind == RefKind.Remote))
            Item($"Checkout {b.Name}", () => CheckoutRemoteAsync(b.Name));
        Item("Checkout (detached)", async () =>
        {
            if (await ConfirmAsync("Checkout", $"{e.Short} 커밋으로 이동 - 브랜치 없는 상태(detached HEAD)", "Checkout"))
                await Act("Checkout", Repo.CheckoutAsync(e.Hash));
        });
        Sep();
        Item("New Branch", () => NewBranchAsync(e));
        Item("New Tag", () => NewTagAsync(e));
        Sep();
        var branch = Model!.Repo.Branch;
        Item($"Reset {branch} to here", () => ResetAsync(e, branch), !isHead && branch != RepoModel.Detached);
        Item("Revert", async () =>
        {
            if (await ConfirmAsync("Revert", $"{e.Short} \"{e.Subject}\" 변경을 되돌리는 새 커밋 생성", "Revert"))
                await Act("Revert", Repo.RevertAsync(e.Hash, e.IsMerge));
        });
        Item("Cherry-pick", async () =>
        {
            if (await ConfirmAsync("Cherry-pick", $"{e.Short} \"{e.Subject}\" 변경을 {branch} 에 새 커밋으로 적용", "Cherry-pick"))
                await Act("Cherry-pick", Repo.CherryPickAsync(e.Hash, e.IsMerge));
        }, !isHead);
        Sep();
        Item("Copy Hash", async () => { if (Clipboard is { } cb) await cb.SetTextAsync(e.Hash); });
    }

    int IndexOf(LogRow r)
    {
        var all = Model!.AllRows;
        for (int i = 0; i < all.Count; i++) if (ReferenceEquals(all[i], r)) return i;
        return int.MaxValue;
    }

    void OpenCompare(LogEntry older, LogEntry newer) =>
        Navigate(new ChangesPage(Model!.Repo, new DiffSpec.Range(older.Hash, newer.Hash), $"{older.Short} → {newer.Short}"));

    // 동작 실행 - 실패면 메시지, 성공이든 실패든 목록 새로 고침 (일부만 적용된 경우 대비)
    async Task Act(string what, Task<GitResult> run)
    {
        await CheckAsync(what, run);
        await ReloadAsync();
    }

    // 원격 브랜치 - 같은 이름 로컬 브랜치를 만들어 추적
    async Task CheckoutRemoteAsync(string remote)
    {
        var local = remote[(remote.IndexOf('/') + 1)..];
        await Act("Checkout", Repo.RunAsync("checkout", "--track", "-b", local, remote));
    }

    async Task NewBranchAsync(LogEntry e)
    {
        if (await PromptAsync("New Branch", $"{e.Short} 에서 시작하는 브랜치 이름", "", "Create", "만든 뒤 Checkout", true) is not { } r) return;
        await Act("New Branch", Repo.CreateBranchAsync(r.Text, e.Hash, r.Check));
    }

    async Task NewTagAsync(LogEntry e)
    {
        if (await PromptAsync("New Tag", $"{e.Short} 에 붙일 태그 이름", "", "Create", second: "메시지 (비우면 가벼운 태그)") is not { } r) return;
        await Act("New Tag", Repo.CreateTagAsync(r.Text, e.Hash, SecondText));
    }

    async Task ResetAsync(LogEntry e, string branch)
    {
        int mode = await ChooseAsync($"Reset {branch}", $"{branch} 를 {e.Short} \"{e.Subject}\" 로 이동", [
            ("Soft", "커밋만 되돌림 - 변경 내용은 스테이징된 채로 남음"),
            ("Mixed", "커밋과 스테이징 되돌림 - 변경 내용은 작업 트리에 남음"),
            ("Hard", "작업 트리까지 되돌림 - 커밋 안 한 변경 사라짐"),
        ], "Reset", 1);
        if (mode < 0) return;
        if (mode == 2 && !await ConfirmAsync("Reset --hard", "커밋하지 않은 변경이 모두 사라짐 - 되돌릴 수 없음", "Reset --hard")) return;
        await Act("Reset", Repo.ResetAsync((ResetMode)mode, e.Hash));
    }
}
