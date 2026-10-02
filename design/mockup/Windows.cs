using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
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
            var head = System.Math.Max(0, Commits.FindIndex(c => c.Badges.Any(b => b.IsHead)));
            Select(Commits[head]);
            l.SelectedIndex = head;
            l.SelectionChanged += (_, _) => { if (l.SelectedItem is Commit c && c != Sel) SelectAsync(c); };
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

    // 처음 한 번 - 동기 (창 표시 전 내용 채움)
    void Select(Commit c)
    {
        Sel = c;
        Fill(GitData.Files(c));
    }

    void Fill(List<FileChange> files)
    {
        Files.Clear();
        foreach (var x in files) Files.Add(x);
        ChangesTitle = $"Changes {Files.Count}";
    }

    // 클릭 - git 호출은 백그라운드, 늦게 온 이전 결과는 버림
    int _selSeq, _diffSeq;

    async void SelectAsync(Commit c)
    {
        Sel = c;
        int seq = ++_selSeq;
        var files = await Task.Run(() => GitData.Files(c));
        if (seq == _selSeq) Fill(files);
    }

    async void ShowDiff(FileChange fc)
    {
        DiffFile = fc;
        if (Sel is not { } sel) return;
        int seq = ++_diffSeq;
        var lines = await Task.Run(() => GitData.Diff(sel, fc.Path));
        if (seq != _diffSeq) return;
        Lines.Clear();
        foreach (var d in lines) Lines.Add(d);
    }
}

public partial class LogB : MockWindow { public LogB() { InitializeComponent(); Init(); } }
public partial class CommitB : MockWindow { public CommitB() { InitializeComponent(); Init(); } }
