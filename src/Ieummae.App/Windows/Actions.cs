using Ieummae.App.Views;
using Ieummae.Core.Git;

namespace Ieummae.App.Windows;

// 저장소 동작 모음 - 로그 창 버튼·더보기 메뉴, 탐색기 메뉴 명령이 함께 씀
// 각 동작은 저장소가 바뀌었으면 true (부른 쪽이 화면 새로 고침)
public sealed class Actions(IDialogs w, RepoModel model)
{
    Repository Repo => model.Repo;

    // 충돌 생기면 부르는 쪽 - 충돌 해결 창 열기
    public Func<Task>? OnConflicts { get; set; }

    public async Task<bool> FetchAsync() =>
        (await w.ProgressAsync("Fetch", (l, ct) => Repo.FetchAsync(l, ct))).Ok;

    public async Task<bool> PullAsync()
    {
        var r = await w.ProgressAsync("Pull", (l, ct) => Repo.PullAsync(l, ct));
        await AfterMergeLikeAsync();
        return true;
    }

    public async Task<bool> PushAsync()
    {
        var r = await w.ProgressAsync("Push", (l, ct) => Repo.PushAsync(l, ct));
        return r.Ok;
    }

    // 병합·리베이스·Pull 뒤 - 충돌 남았으면 해결 창 안내
    async Task AfterMergeLikeAsync()
    {
        var status = await Repo.StatusAsync();
        int n = status.Files.Count(f => f.IsConflict);
        if (n == 0) return;
        if (OnConflicts is { } open && await w.ConfirmAsync("충돌", $"충돌 파일 {n}개 - 해결 전에는 커밋 불가", "Resolve"))
            await open();
    }

    static string Ago(DateTimeOffset t) => LogRow.Relative(t);

    public async Task<bool> SwitchAsync()
    {
        // 로컬 브랜치가 이미 있는 원격 브랜치는 빼고 (같은 곳으로 가는 항목 중복)
        var branches = await Repo.BranchesAsync();
        var locals = branches.Where(b => !b.IsRemote).Select(b => b.Name).ToHashSet();
        var all = branches.Where(b => !b.IsCurrent && !(b.IsRemote && locals.Contains(b.LocalName))).ToList();
        var pick = await w.PickAsync("Switch", "전환할 브랜치 - 원격 브랜치는 같은 이름 로컬 브랜치를 만들어 추적",
            all.Select(b => (b.Name, $"{(b.IsRemote ? "원격" : b.Upstream ?? "로컬")} · {b.Hash} · {Ago(b.Date)}")).ToList(), ["Switch"]);
        if (pick is null) return false;
        return await w.CheckAsync("Switch", Repo.SwitchAsync(all[pick.Index]));
    }

    public async Task<bool> NewBranchAsync()
    {
        if (await w.PromptAsync("New Branch", $"{model.Branch} 에서 시작하는 브랜치 이름", "", "Create", "만든 뒤 Checkout", true) is not { } r) return false;
        return await w.CheckAsync("New Branch", Repo.CreateBranchAsync(r.Text, "HEAD", r.Check));
    }

    public async Task<bool> DeleteBranchAsync()
    {
        var all = (await Repo.BranchesAsync()).Where(b => !b.IsCurrent).ToList();
        var pick = await w.PickAsync("Delete Branch", "지울 브랜치",
            all.Select(b => (b.Name, $"{(b.IsRemote ? "원격 - 서버에서 지움" : "로컬")} · {b.Hash} · {Ago(b.Date)}")).ToList(), ["Delete"]);
        if (pick is null) return false;
        var b = all[pick.Index];
        if (b.IsRemote)
        {
            if (!await w.ConfirmAsync("Delete Branch", $"원격 {b.Name} 를 서버에서 삭제", "Delete")) return false;
            return (await w.ProgressAsync("Delete Branch", (l, ct) => Repo.DeleteRemoteBranchAsync(b, l, ct))).Ok;
        }
        var r = await Repo.DeleteBranchAsync(b.Name, force: false);
        if (r.Ok) return true;
        // 병합 안 된 브랜치 - 강제 삭제 확인
        if (!await w.ConfirmAsync("Delete Branch", $"{b.Name} 는 병합되지 않은 커밋이 있음 - 그래도 삭제", "Delete")) return false;
        return await w.CheckAsync("Delete Branch", Repo.DeleteBranchAsync(b.Name, force: true));
    }

    public async Task<bool> MergeAsync()
    {
        var all = (await Repo.BranchesAsync()).Where(b => !b.IsCurrent).ToList();
        var pick = await w.PickAsync("Merge", $"{model.Branch} 에 합칠 브랜치",
            all.Select(b => (b.Name, $"{b.Hash} · {Ago(b.Date)}")).ToList(), ["Merge"], "병합 커밋 항상 만들기 (--no-ff)");
        if (pick is null) return false;
        var r = await Repo.MergeAsync(all[pick.Index].Name, pick.Check);
        if (!r.Ok && !await HasConflictsAsync()) await w.AlertAsync("Merge 실패", r.Message);
        await AfterMergeLikeAsync();
        return true;
    }

    public async Task<bool> RebaseAsync()
    {
        var all = (await Repo.BranchesAsync()).Where(b => !b.IsCurrent).ToList();
        var pick = await w.PickAsync("Rebase", $"{model.Branch} 커밋들을 옮겨 얹을 브랜치",
            all.Select(b => (b.Name, $"{b.Hash} · {Ago(b.Date)}")).ToList(), ["Rebase"]);
        if (pick is null) return false;
        var target = all[pick.Index].Name;
        if (!await w.ConfirmAsync("Rebase", $"{model.Branch} 의 커밋을 {target} 위에 다시 쌓음 - 커밋 해시가 바뀜 (이미 Push 한 커밋이면 주의)", "Rebase")) return false;
        var r = await Repo.RebaseAsync(target);
        if (!r.Ok && !await HasConflictsAsync()) await w.AlertAsync("Rebase 실패", r.Message);
        await AfterMergeLikeAsync();
        return true;
    }

    async Task<bool> HasConflictsAsync() => (await Repo.StatusAsync()).Files.Any(f => f.IsConflict);

    public async Task<bool> StashAsync()
    {
        if (await w.PromptAsync("Stash", "임시 보관 이름 (비워도 됨)", "작업 중", "Stash", "추적 안 하는 파일 포함", false) is not { } r) return false;
        return await w.CheckAsync("Stash", Repo.StashPushAsync(r.Text, r.Check));
    }

    public async Task<bool> StashesAsync()
    {
        var all = await Repo.StashesAsync();
        var pick = await w.PickAsync("Stash List", "Pop - 꺼내고 목록에서 지움 / Apply - 꺼내고 남김 / Drop - 버림",
            all.Select(s => (s.Message, $"{s.Ref} · {Ago(s.Date)}")).ToList(), ["Pop", "Apply", "Drop"]);
        if (pick is null) return false;
        var s = all[pick.Index];
        switch (pick.Action)
        {
            case 0: await CheckStashAsync("Stash Pop", Repo.StashPopAsync(s.Ref)); break;
            case 1: await CheckStashAsync("Stash Apply", Repo.StashApplyAsync(s.Ref)); break;
            default:
                if (!await w.ConfirmAsync("Stash Drop", $"\"{s.Message}\" 보관 내용 버림 - 되돌릴 수 없음", "Drop")) return false;
                await w.CheckAsync("Stash Drop", Repo.StashDropAsync(s.Ref));
                break;
        }
        return true;
    }

    async Task CheckStashAsync(string what, Task<GitResult> run)
    {
        var r = await run;
        if (!r.Ok && !await HasConflictsAsync()) await w.AlertAsync(what + " 실패", r.Message);
        await AfterMergeLikeAsync();
    }

    public async Task<bool> TagsAsync()
    {
        var all = await Repo.TagsAsync();
        var pick = await w.PickAsync("Tags", "Push - 원격에 올림 / Delete - 로컬에서 지움",
            all.Select(t => (t.Name, $"{t.Hash} · {Ago(t.Date)} · {t.Subject}")).ToList(), ["Push", "Delete", "Push All"]);
        if (pick is null) return false;
        var remote = (await Repo.RemotesAsync()).FirstOrDefault();
        var t = all[pick.Index];
        switch (pick.Action)
        {
            case 0:
                if (remote is null) { await w.AlertAsync("Push", "원격 저장소 없음"); return false; }
                return (await w.ProgressAsync("Push Tag", (l, ct) => Repo.PushTagAsync(remote, t.Name, l, ct))).Ok;
            case 1:
                if (!await w.ConfirmAsync("Delete Tag", $"{t.Name} 태그 삭제 (로컬)", "Delete")) return false;
                return await w.CheckAsync("Delete Tag", Repo.DeleteTagAsync(t.Name));
            default:
                if (remote is null) { await w.AlertAsync("Push", "원격 저장소 없음"); return false; }
                return (await w.ProgressAsync("Push Tags", (l, ct) => Repo.PushAllTagsAsync(remote, l, ct))).Ok;
        }
    }

    public async Task<bool> RemotesAsync()
    {
        var all = await Repo.RemoteInfosAsync();
        var pick = await w.PickAsync("Remotes", "원격 저장소", all.Select(r => (r.Name, r.Url)).ToList(), ["Edit URL", "Remove", "Add"]);
        // 목록이 비어 있으면 고를 수 없으므로 바로 추가
        if (pick is null && all.Count > 0) return false;
        if (pick is null || pick.Action == 2)
        {
            if (await w.PromptAsync("Add Remote", "이름", all.Count == 0 ? "origin" : "", "Next") is not { } n) return false;
            if (await w.PromptAsync("Add Remote", $"{n.Text} 주소", "", "Add") is not { } u) return false;
            return await w.CheckAsync("Add Remote", Repo.AddRemoteAsync(n.Text, u.Text));
        }
        var r = all[pick.Index];
        if (pick.Action == 0)
        {
            if (await w.PromptAsync("Edit URL", $"{r.Name} 주소", r.Url, "Save") is not { } u) return false;
            return await w.CheckAsync("Edit URL", Repo.SetRemoteUrlAsync(r.Name, u.Text));
        }
        if (!await w.ConfirmAsync("Remove Remote", $"{r.Name} 연결과 그 원격 브랜치 기록 삭제 (서버는 그대로)", "Remove")) return false;
        return await w.CheckAsync("Remove Remote", Repo.RemoveRemoteAsync(r.Name));
    }
}
