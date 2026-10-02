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

    // 추적 브랜치 대비 - ↑ 올릴 커밋, ↓ 받을 커밋 (추적 없으면 빈 칸)
    string _sync = "";
    public string SyncText { get => _sync; private set => Set(ref _sync, value); }

    public async Task LoadAsync()
    {
        var branch = Repo.BranchAsync();
        var ab = Repo.AheadBehindAsync();
        Branch = await branch ?? Detached;
        SyncText = await ab switch
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
