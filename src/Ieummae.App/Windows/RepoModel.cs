using System.ComponentModel;
using System.Runtime.CompilerServices;
using Ieummae.Core.Git;

namespace Ieummae.App.Windows;

// 창 공통 저장소 정보 - 이름은 바로, 브랜치는 창 표시 후 git 으로
public sealed class RepoModel(Repository repo) : INotifyPropertyChanged
{
    public const string Detached = "detached HEAD";

    public event PropertyChangedEventHandler? PropertyChanged;

    public Repository Repo { get; } = repo;
    public string Name => Repo.Name;

    string _branch = "";
    public string Branch { get => _branch; private set => Set(ref _branch, value); }

    // Push 버튼 - 글자 옆에 올릴 커밋 수(강조색), 올릴 게 없으면 흐리게
    string _pushCount = "";
    public string PushCount { get => _pushCount; private set { Set(ref _pushCount, value); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPushCount))); } }
    public bool HasPushCount => _pushCount.Length > 0;
    bool _canPush;
    public bool CanPush { get => _canPush; private set => Set(ref _canPush, value); }

    // 추적 브랜치 대비 - ↑ 올릴 커밋, ↓ 받을 커밋 (추적 없으면 빈 칸)
    string _sync = "";
    public string SyncText { get => _sync; private set => Set(ref _sync, value); }

    // 진행 중인 병합·리베이스 등 - 제목 줄에 Resolve 버튼
    string _op = "";
    public string OperationText { get => _op; private set { Set(ref _op, value); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasOperation))); } }
    public bool HasOperation => _op.Length > 0;

    public async Task LoadAsync()
    {
        var branch = Repo.BranchAsync();
        var ab = Repo.AheadBehindAsync();
        var op = Repo.OperationAsync();
        Branch = await branch ?? Detached;
        OperationText = await op switch
        {
            Operation.Merge => "Merge 중",
            Operation.Rebase => "Rebase 중",
            Operation.CherryPick => "Cherry-pick 중",
            Operation.Revert => "Revert 중",
            _ => "",
        };
        var sync = await ab;
        // Push - 올릴 커밋 수, 추적 브랜치 없으면(처음 올리는 브랜치) 개수 없이 누를 수 있게
        PushCount = sync is { Ahead: > 0 } s1 ? s1.Ahead.ToString() : "";
        CanPush = sync is null ? Branch != Detached : sync.Value.Ahead > 0;
        SyncText = sync switch
        {
            null => "",
            (0, 0) => "up to date",
            var (a, b) => string.Join(" ", new[] { a > 0 ? $"↑{a}" : "", b > 0 ? $"↓{b}" : "" }.Where(s => s.Length > 0)),
        };
    }

    void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
