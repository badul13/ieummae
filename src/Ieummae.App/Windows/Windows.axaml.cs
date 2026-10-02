using Avalonia.Controls;

namespace Ieummae.App.Windows;

public partial class BranchChip : UserControl
{
    public BranchChip() => InitializeComponent();
}

public partial class CommitWindow : IeumWindow
{
    public CommitWindow() : this(null) { }

    public CommitWindow(RepoModel? model)
    {
        InitializeComponent();
        DataContext = model;
    }
}

// 안내 창 - 저장소 아님, 모르는 명령 등
public partial class MessageWindow : IeumWindow
{
    public MessageWindow() : this("", "") { }

    public MessageWindow(string title, string detail)
    {
        InitializeComponent();
        Title = "ieummae · " + title;
        this.FindControl<TextBlock>("TitleText")!.Text = title;
        this.FindControl<TextBlock>("DetailText")!.Text = detail;
    }
}

// diff 창 - 파일 하나. 비교 대상 없으면 작업 트리(추적 안 하는 파일은 빈 파일) 대비
public partial class DiffWindow : IeumWindow
{
    public DiffWindow() : this(null, null, "", "") { }

    public DiffWindow(RepoModel? model, Ieummae.Core.Git.DiffSpec? spec, string relPath, string specText)
    {
        InitializeComponent();
        DataContext = model;
        this.FindControl<TextBlock>("SpecText")!.Text = specText;
        if (model is null) return;
        var view = this.FindControl<Views.DiffView>("Diff")!;
        Opened += async (_, _) =>
        {
            var repo = model.Repo;
            var s = spec ?? (await repo.IsTrackedAsync(relPath) ? new Ieummae.Core.Git.DiffSpec.WorkingTree() : new Ieummae.Core.Git.DiffSpec.Untracked());
            if (s is Ieummae.Core.Git.DiffSpec.Untracked) this.FindControl<TextBlock>("SpecText")!.Text = "추적 안 하는 새 파일";
            await view.LoadAsync(async (opt, ct) => (await repo.DiffAsync(s, [relPath], opt, ct)).FirstOrDefault());
        };
    }
}
