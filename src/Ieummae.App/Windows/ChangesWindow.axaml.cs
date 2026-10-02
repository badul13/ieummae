using Avalonia.Controls;
using Ieummae.Core.Git;

namespace Ieummae.App.Windows;

public partial class ChangesWindow : IeumWindow
{
    public ChangesWindow() : this(null, new DiffSpec.WorkingTree(), "") { }

    public ChangesWindow(RepoModel? model, DiffSpec spec, string specText)
    {
        InitializeComponent();
        DataContext = model;
        SpecText.Text = specText;
        if (model is null) return;
        var repo = model.Repo;
        Files.SelectionChanged += (_, _) =>
        {
            if (Files.SelectedItem is ChangedFile f)
                _ = Diff.LoadAsync(async (opt, ct) => (await repo.DiffAsync(spec, [f.Path], opt, ct)).FirstOrDefault());
        };
        Opened += async (_, _) =>
        {
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
