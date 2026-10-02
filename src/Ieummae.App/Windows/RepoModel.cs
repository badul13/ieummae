using System.ComponentModel;
using System.Runtime.CompilerServices;
using Ieummae.Core.Git;

namespace Ieummae.App.Windows;

// 창 공통 저장소 정보 - 이름은 바로, 브랜치는 창 표시 후 git 으로
public sealed class RepoModel(Repository repo) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public Repository Repo { get; } = repo;
    public string Name => Repo.Name;

    string _branch = "";
    public string Branch { get => _branch; private set => Set(ref _branch, value); }

    public async Task LoadAsync()
    {
        Branch = await Repo.BranchAsync() ?? "detached HEAD";
    }

    void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
