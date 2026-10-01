using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;

namespace IeumMock;

// 시안 창 공통 - 실제 저장소 데이터, 선택에 따라 상세·diff 갱신
public partial class MockWindow : Window, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;
    void Notify(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public List<Commit> Commits { get; } = GitData.Log(600);
    public ObservableCollection<FileChange> Files { get; } = new();
    public ObservableCollection<DiffLine> Lines { get; } = new();
    public string RepoName { get; } = GitData.RepoName;
    public string BranchName { get; } = GitData.Branch;
    public int AheadCount { get; } = GitData.Ahead;
    public string AheadText => AheadCount > 0 ? $"↑{AheadCount} ahead" : "up to date";
    public string PushText => AheadCount > 0 ? $"Push {AheadCount}" : "Push";
    public string CommitCount => $"{Commits.Count} commits";
    public string HeaderTitle => Program.View == "commit" ? "Commit" : RepoName;
    public string HeaderSub => Program.View == "commit" ? $"{RepoName} · {BranchName}" : $"{BranchName} · {AheadText}";

    Commit? _sel;
    public Commit? Sel { get => _sel; set { _sel = value; Notify(nameof(Sel)); } }
    FileChange? _file;
    public FileChange? DiffFile { get => _file; set { _file = value; Notify(nameof(DiffFile)); } }
    string _pal = "";
    public string PaletteName { get => _pal; set { _pal = value; Notify(nameof(PaletteName)); } }
    string _changes = "";
    public string ChangesTitle { get => _changes; set { _changes = value; Notify(nameof(ChangesTitle)); } }

    // 커밋 창 - 작업 트리가 깨끗하면 HEAD 커밋 변경을 대신 보여줌
    public string CommitNote => "작업 트리 변경 없음 · HEAD 커밋 내용으로 표시";
    public string HeadMsg => Commits.FirstOrDefault(c => c.Badges.Any(b => b.IsHead))?.Msg ?? "";

    protected override System.Type StyleKeyOverride => typeof(Window);

    protected void Init()
    {
        PaletteName = Tones.For(Program.Tone)[Program.Pal].Name;
        DataContext = this;
        if (this.FindControl<ListBox>("List") is { } l)
        {
            l.SelectionChanged += (_, _) => { if (l.SelectedItem is Commit c) Select(c); };
            l.SelectedIndex = System.Math.Max(0, Commits.FindIndex(c => c.Badges.Any(b => b.IsHead)));
        }
        if (this.FindControl<ListBox>("FileList") is { } f)
        {
            var head = Commits.FirstOrDefault(c => c.Badges.Any(b => b.IsHead)) ?? Commits[0];
            Select(head);
            f.SelectionChanged += (_, _) => { if (f.SelectedItem is FileChange fc) ShowDiff(fc); };
            int big = Files.Select((x, i) => (x, i)).OrderByDescending(t => t.x.Add + t.x.Del).FirstOrDefault().i;
            f.SelectedIndex = Files.Count > 0 ? big : -1;
        }
    }

    void Select(Commit c)
    {
        Sel = c;
        Files.Clear();
        foreach (var x in GitData.Files(c)) Files.Add(x);
        ChangesTitle = $"Changes {Files.Count}";
    }

    void ShowDiff(FileChange fc)
    {
        DiffFile = fc;
        Lines.Clear();
        if (Sel is null) return;
        foreach (var d in GitData.Diff(Sel, fc.Path)) Lines.Add(d);
    }
}

public partial class LogB : MockWindow { public LogB() { InitializeComponent(); Init(); } }
public partial class CommitB : MockWindow { public CommitB() { InitializeComponent(); Init(); } }
