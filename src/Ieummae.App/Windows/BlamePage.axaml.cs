using Avalonia.Controls;
using Ieummae.Core.Git;
using Ieummae.Core.Log;

namespace Ieummae.App.Windows;

// Blame 한 줄 - 묶음 첫 줄 여부, 묶음 번갈아 바탕
public sealed record BlameRow(BlameLine Line, bool First, bool Alt)
{
    public string Date => Line.Uncommitted ? "커밋 전" : Line.Time.ToLocalTime().ToString("yy-MM-dd");
    // 커밋 전 줄 - git 이 채우는 가짜 정보(0000…, Not Committed Yet) 대신 빈칸
    public string Hash => Line.Uncommitted ? "" : Line.Short;
    public string Author => Line.Uncommitted ? "" : Line.Author;
    public string Summary => Line.Uncommitted ? "" : Line.Summary;
    public string Code => Line.Text.Replace("\t", "    ");
}

// Blame 화면 - 줄마다 마지막 수정 커밋
public partial class BlamePage : Page
{
    readonly string _name;

    public BlamePage() : this(null, "") { }

    public override string PageTitle => "Blame · " + _name;

    public BlamePage(RepoModel? model, string relPath)
    {
        InitializeComponent();
        _name = Path.GetFileName(relPath);
        DataContext = model;
        PathText.Text = relPath;
        if (model is null) return;
        var repo = model.Repo;
        LogButton.Click += (_, _) => Navigate(new LogPage(new LogModel(model, new LogQuery(Path: relPath))));
        Shown += async shownFirst =>
        {
            if (!shownFirst) return;
            try
            {
                var lines = await Task.Run(() => repo.BlameAsync(relPath));
                var rows = new List<BlameRow>(lines.Count);
                bool alt = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    bool first = i == 0 || lines[i - 1].Hash != lines[i].Hash;
                    // 번갈아 바탕은 커밋된 묶음끼리만 (커밋 전 묶음은 따로 초록)
                    if (first && i > 0 && !lines[i].Uncommitted) alt = !alt;
                    rows.Add(new BlameRow(lines[i], first, alt));
                }
                Lines.ItemsSource = rows;
                CountText.Text = $"{lines.Count:N0}줄 · 커밋 {lines.Select(l => l.Hash).Distinct().Count():N0}개";
                Notice.IsVisible = false;
            }
            catch (GitException e) { Notice.Text = e.Message; }
        };

        // 줄 우클릭 - 그 커밋의 변경 보기
        AttachMenu(Lines, menu =>
        {
            if (Lines.SelectedItem is not BlameRow { Line.Uncommitted: false } row) return;
            var mi = new MenuItem { Header = $"{row.Line.Short} 변경 보기" };
            mi.Click += async (_, _) =>
            {
                var parent = await repo.RunAsync("rev-parse", "-q", "--verify", row.Line.Hash + "^");
                var spec = new DiffSpec.Commit(row.Line.Hash, parent.Ok ? parent.Output.Trim() : null);
                Navigate(new ChangesPage(model, spec, $"{row.Line.Short} · {row.Line.Summary}"));
            };
            menu.Items.Add(mi);
        });
    }
}
