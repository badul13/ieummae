using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Ieummae.Core.Diff;
using Ieummae.Core.Git;

namespace Ieummae.App.Views;

// 파일 하나 diff - 통합·좌우 보기, 공백 무시. 내용은 불러오기 함수로 받아 옵션 바뀌면 다시 불러옴
public partial class DiffView : UserControl, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    // 보기 설정 - 같은 프로세스의 diff 화면끼리 공유
    static bool s_split, s_ignoreWs;

    // 이보다 줄이 많으면 앞부분만 (화면 응답 유지)
    const int MaxLines = 20_000;

    Func<DiffOptions, CancellationToken, Task<FileDiff?>>? _loader;
    CancellationTokenSource? _cts;

    public DiffView()
    {
        InitializeComponent();
        DataContext = this;
    }

    public bool IsSplit { get => s_split; set { s_split = value; Notify(); } }
    public bool IgnoreWhitespace { get => s_ignoreWs; set { s_ignoreWs = value; Notify(); } }

    FileDiff? _file;
    public string FileName => _file is null ? "" : Path.GetFileName(_file.Path);
    public string Dir => _file is null ? "" : Path.GetDirectoryName(_file.Path)?.Replace('\\', '/') ?? "";
    public string AddText => _file is { IsBinary: false } f ? $"+{f.Added}" : "";
    public string DelText => _file is { IsBinary: false } f ? $"-{f.Deleted}" : "";

    string? _notice;
    public string? Notice { get => _notice; private set { _notice = value; Notify(); } }

    List<UnifiedRow> _unified = [];
    public List<UnifiedRow> Unified { get => _unified; private set { _unified = value; Notify(); } }
    List<SplitRowView> _split = [];
    public List<SplitRowView> Split { get => _split; private set { _split = value; Notify(); } }

    // 비우기 - 파일 선택 전
    public void Clear(string? notice = null)
    {
        _cts?.Cancel();
        _loader = null;
        Show(null, notice);
    }

    public Task LoadAsync(Func<DiffOptions, CancellationToken, Task<FileDiff?>> loader)
    {
        _loader = loader;
        return ReloadAsync();
    }

    async Task ReloadAsync()
    {
        if (_loader is not { } loader) return;
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        Notice = "불러오는 중";
        try
        {
            var f = await loader(new DiffOptions(IgnoreWhitespace: IgnoreWhitespace), cts.Token);
            if (cts.IsCancellationRequested) return;
            Show(f, f is null ? "변경 없음" : null);
        }
        catch (OperationCanceledException) { }
        catch (GitException e) { if (!cts.IsCancellationRequested) Show(null, e.Message); }
    }

    void Show(FileDiff? f, string? notice)
    {
        _file = f;
        bool trimmed = false;
        if (f is not null)
        {
            if (f.IsBinary) notice = "바이너리 파일";
            else if (f.Hunks.Count == 0) notice = f.Status == FileStatus.Renamed ? "이름만 바뀜" : f.OldMode != f.NewMode ? $"모드 변경 {f.OldMode} → {f.NewMode}" : "내용 변경 없음";
            else if (f.Hunks.Sum(h => h.Lines.Count) > MaxLines) trimmed = Trim(f);
        }
        var unified = f is null ? [] : UnifiedRow.Build(f);
        var split = f is null ? [] : SplitRowView.Build(f);
        if (trimmed)
        {
            unified.Add(new UnifiedRow { HunkText = TrimNote });
            split.Add(new SplitRowView { HunkText = TrimNote });
        }
        Unified = unified;
        Split = split;
        Notice = notice;
        Notify(nameof(FileName)); Notify(nameof(Dir)); Notify(nameof(AddText)); Notify(nameof(DelText));
    }

    static readonly string TrimNote = $"이후 생략 - {MaxLines:N0}줄 넘는 변경";

    // 큰 변경 - 앞쪽 MaxLines 줄까지만, 첫 덩어리부터 넘치면 그 덩어리를 자름
    static bool Trim(FileDiff f)
    {
        int n = 0, keep = 0;
        while (keep < f.Hunks.Count && n + f.Hunks[keep].Lines.Count <= MaxLines) n += f.Hunks[keep++].Lines.Count;
        if (keep == 0) f.Hunks[0].Lines.RemoveRange(MaxLines, f.Hunks[0].Lines.Count - MaxLines);
        int from = Math.Max(1, keep);
        f.Hunks.RemoveRange(from, f.Hunks.Count - from);
        return true;
    }

    void OnUnified(object? s, RoutedEventArgs e) => IsSplit = false;
    void OnSplit(object? s, RoutedEventArgs e) => IsSplit = true;
    void OnWhitespace(object? s, RoutedEventArgs e)
    {
        IgnoreWhitespace = !IgnoreWhitespace;
        _ = ReloadAsync();
    }

    void Notify([CallerMemberName] string name = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
