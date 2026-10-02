using Avalonia.Controls;
using Ieummae.Core.Git;

namespace Ieummae.App.Windows;

// 비교 화면 - 두 커밋(또는 커밋 하나)의 변경 파일 + 고른 파일 diff
public partial class ChangesPage : Page
{
    readonly string _spec;

    public ChangesPage() : this(null, new DiffSpec.WorkingTree(), "") { }

    public override string PageTitle => "Compare · " + _spec;

    public ChangesPage(RepoModel? model, DiffSpec spec, string specText)
    {
        InitializeComponent();
        _spec = specText;
        DataContext = model;
        SpecText.Text = specText;
        if (model is null) return;
        var repo = model.Repo;
        Files.SelectionChanged += (_, _) =>
        {
            if (Files.SelectedItem is ChangedFile f)
                _ = Diff.LoadAsync(async (opt, ct) => (await repo.DiffAsync(spec, [f.Path], opt, ct)).FirstOrDefault());
        };
        Shown += async first =>
        {
            if (!first) return;
            var (from, to) = spec switch
            {
                DiffSpec.Range r => (r.From, r.To),
                DiffSpec.Commit c => (c.Parent ?? Repository.EmptyTree, c.Hash),
                _ => throw new NotSupportedException(),
            };
            try
            {
                var list = await repo.ChangedFilesAsync(to, from);
                Files.ItemsSource = list;
                TitleText.Text = $"Changes {list.Count}";
                if (list.Count > 0) Files.SelectedIndex = 0;
                else Diff.Clear("변경 없음");
            }
            catch (GitException e) { Diff.Clear(e.Message); }
        };
    }
}
