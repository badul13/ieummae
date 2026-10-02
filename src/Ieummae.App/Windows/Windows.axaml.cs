using Avalonia.Controls;
using Ieummae.Core.Git;

namespace Ieummae.App.Windows;

public partial class BranchChip : UserControl
{
    public BranchChip() => InitializeComponent();
}

// 안내 화면 - 저장소 아님, 모르는 명령 등
public partial class MessagePage : Page
{
    readonly string _title;

    public MessagePage() : this("", "") { }

    public MessagePage(string title, string detail)
    {
        InitializeComponent();
        _title = title;
        TitleText.Text = title;
        DetailText.Text = detail;
    }

    public override string PageTitle => _title;
}

// diff 화면 - 파일 하나. 비교 대상 없으면 작업 트리(추적 안 하는 파일은 빈 파일) 대비
public partial class DiffPage : Page
{
    readonly string _name;

    public DiffPage() : this(null, null, "", "") { }

    public DiffPage(RepoModel? model, DiffSpec? spec, string relPath, string specText)
    {
        InitializeComponent();
        DataContext = model;
        _name = Path.GetFileName(relPath);
        SpecText.Text = specText;
        if (model is null) return;
        Shown += async first =>
        {
            if (!first) return;
            var repo = model.Repo;
            var s = spec ?? (await repo.IsTrackedAsync(relPath) ? new DiffSpec.WorkingTree() : new DiffSpec.Untracked());
            if (s is DiffSpec.Untracked) SpecText.Text = "추적 안 하는 새 파일";
            await Diff.LoadAsync(async (opt, ct) => (await repo.DiffAsync(s, [relPath], opt, ct)).FirstOrDefault());
        };
    }

    public override string PageTitle => "Diff · " + _name;
}
