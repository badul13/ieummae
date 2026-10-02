using System.ComponentModel;
using System.Runtime.CompilerServices;
using Ieummae.App.Platform;
using Ieummae.App.Views;
using Ieummae.Core.Git;

namespace Ieummae.App.Windows;

// 커밋 창 파일 한 줄 - 체크 여부는 화면에서 바뀜
public sealed class FileRow(WorkingFile f, int added, int deleted) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public WorkingFile File { get; } = f;
    public string Name => File.Name;
    public string Dir => File.OrigPath is { } o ? $"{File.Dir} ← {o}".Trim() : File.Dir;
    public bool HasCounts => !File.IsUntracked && !File.IsConflict;
    public string AddText => $"+{added}";
    public string DelText => $"-{deleted}";
    public bool IsConflict => File.IsConflict;
    public bool CanCheck => !File.IsConflict;
    public string StatusWord => File.Kind switch
    {
        ChangeKind.Added => "Added",
        ChangeKind.Deleted => "Deleted",
        ChangeKind.Renamed => "Renamed",
        ChangeKind.Copied => "Copied",
        ChangeKind.TypeChanged => "Type changed",
        ChangeKind.Untracked => "Untracked",
        ChangeKind.Conflict => "Conflict",
        _ => "Modified",
    };

    bool _checked;
    public bool Checked
    {
        get => _checked;
        set { if (_checked == value) return; _checked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Checked))); CheckedChanged?.Invoke(); }
    }
    public event Action? CheckedChanged;
}

// 커밋 창 상태 - 변경 목록, 메시지, Amend·Push
public sealed class CommitModel(RepoModel repo, string? path) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public RepoModel Repo { get; } = repo;
    public string? PathFilter { get; } = path;
    public RangeList<FileRow> Files { get; } = new();

    string _title = "Changes";
    public string ChangesTitle { get => _title; private set => Set(ref _title, value); }
    string? _notice;
    public string? Notice { get => _notice; private set => Set(ref _notice, value); }

    string _message = "";
    public string Message { get => _message; set { if (Set(ref _message, value)) Notify(nameof(CanCommit)); } }

    bool _amend;
    public bool Amend
    {
        get => _amend;
        set
        {
            if (!Set(ref _amend, value)) return;
            Notify(nameof(CanCommit));
            // Amend 켜면 마지막 커밋 메시지 불러옴 (비어 있을 때만)
            if (value && Message.Trim().Length == 0) _ = LoadHeadMessageAsync();
        }
    }

    bool _push;
    public bool Push { get => _push; set => Set(ref _push, value); }

    bool _busy;
    public bool Busy { get => _busy; set { if (Set(ref _busy, value)) Notify(nameof(CanCommit)); } }

    public bool HasConflicts => Files.Any(f => f.IsConflict);
    public int CheckedCount => Files.Count(f => f.Checked);
    // 커밋 가능 - 메시지 있고, 고른 파일 있거나 Amend (메시지만 고치기), 충돌 없음
    public bool CanCommit => !Busy && Message.Trim().Length > 0 && (CheckedCount > 0 || Amend) && !HasConflicts;

    async Task LoadHeadMessageAsync()
    {
        var m = await Repo.Repo.HeadMessageAsync();
        if (Amend && Message.Trim().Length == 0) Message = m;
    }

    // 변경 목록 - 추적 중인 파일은 체크, 추적 안 하는 파일은 체크 안 함 (실수로 올리는 것 방지)
    // 다시 불러올 때는 사용자가 바꾼 체크 유지
    public async Task LoadAsync()
    {
        var keep = Files.ToDictionary(f => f.File.Path, f => f.Checked);
        try
        {
            var statusTask = Repo.Repo.StatusAsync(PathFilter);
            var countsTask = Repo.Repo.WorkingNumstatAsync();
            var status = await statusTask;
            var counts = await countsTask;
            var rows = status.Files
                .OrderBy(f => f.IsUntracked).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                .Select(f =>
                {
                    var (a, d) = counts.GetValueOrDefault(f.Path);
                    var row = new FileRow(f, a, d) { Checked = keep.TryGetValue(f.Path, out var c) ? c : !f.IsUntracked && !f.IsConflict };
                    row.CheckedChanged += OnChecked;
                    return row;
                }).ToList();
            Files.Reset(rows);
            Notice = rows.Count == 0 ? "커밋할 변경 없음" : HasConflicts ? "충돌 해결 전에는 커밋 불가" : null;
        }
        catch (GitException e) { Files.Clear(); Notice = e.Message; }
        OnChecked();
    }

    void OnChecked()
    {
        ChangesTitle = Files.Count == 0 ? "Changes" : $"Changes {CheckedCount} / {Files.Count}";
        Notify(nameof(CanCommit));
        Notify(nameof(AllChecked));
    }

    public bool AllChecked => Files.Count > 0 && Files.Where(f => f.CanCheck).All(f => f.Checked);

    public void ToggleAll()
    {
        bool to = !AllChecked;
        foreach (var f in Files.Where(f => f.CanCheck)) f.Checked = to;
    }

    public Task<GitResult> CommitAsync() =>
        Repo.Repo.CommitAsync(Message, Files.Where(f => f.Checked).Select(f => f.File).ToList(), Amend);

    public List<string> History() => MessageHistory.Load();

    bool Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Notify(name);
        return true;
    }

    void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
