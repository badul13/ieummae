using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Ieummae.App.Views;
using Ieummae.Core.Git;
using Ieummae.Core.Log;

namespace Ieummae.App.Windows;

// 로그 창 상태 - 커밋 목록(스트리밍), 검색, 선택한 커밋 상세
public sealed class LogModel(RepoModel repo, LogQuery query) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public RepoModel Repo { get; } = repo;
    public LogQuery Query { get; } = query;
    public GraphLayout Graph { get; } = new();

    readonly RangeList<LogRow> _all = new();
    readonly RangeList<LogRow> _found = new();
    public IList<LogRow> Rows => _searching ? _found : _all;
    public IReadOnlyList<LogRow> AllRows => _all;

    // 경로 한정 로그 - 제목 줄에 표시
    public string PathText => string.IsNullOrEmpty(Query.Path) ? "" : Query.Path;
    public bool HasPath => !string.IsNullOrEmpty(Query.Path);

    bool _loading;
    public bool Loading { get => _loading; private set { Set(ref _loading, value); Notify(nameof(CountText)); } }
    public string CountText => _searching
        ? $"{_found.Count:N0} / {_all.Count:N0} commits"
        : Loading ? $"불러오는 중 · {_all.Count:N0}" : $"{_all.Count:N0} commits";

    // 첫 화면용 시간 - 측정 (IEUM_TRACE 설정 시 파일로)
    public TimeSpan FirstBatch { get; private set; }
    public TimeSpan Total { get; private set; }

    CancellationTokenSource? _loadCts;

    // 다시 불러오기 - keep: 다시 고를 커밋
    public async Task LoadAsync(string? keep = null)
    {
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        var sw = Stopwatch.StartNew();
        _all.Clear();
        Graph.Lanes = 1;
        Loading = true;
        bool first = true;
        try
        {
            // 읽기·레인 계산은 백그라운드, 묶음으로 화면에 붙임 (처음엔 작게 - 첫 화면 빨리)
            var channel = System.Threading.Channels.Channel.CreateUnbounded<List<LogRow>>();
            var reader = Task.Run(async () =>
            {
                var batch = new List<LogRow>();
                var last = Stopwatch.StartNew();
                int limit = 100;
                try
                {
                    await foreach (var e in LogReader.ReadAsync(Repo.Repo, Query, cts.Token))
                    {
                        batch.Add(new LogRow(e, Graph));
                        if (batch.Count >= limit || last.ElapsedMilliseconds > 120)
                        {
                            channel.Writer.TryWrite(batch);
                            batch = [];
                            limit = 5000;
                            last.Restart();
                        }
                    }
                    if (batch.Count > 0) channel.Writer.TryWrite(batch);
                    channel.Writer.TryComplete();
                }
                catch (Exception ex) { channel.Writer.TryComplete(ex); }
            }, cts.Token);

            await foreach (var batch in channel.Reader.ReadAllAsync(cts.Token))
            {
                int lanes = _all.Count < GraphLayout.SampleRows ? batch.Take(GraphLayout.SampleRows - _all.Count).Max(r => r.Entry.Graph.Width) : 0;
                if (lanes > Graph.Lanes) Graph.Lanes = lanes;
                _all.AddRange(batch);
                Notify(nameof(CountText));
                if (first)
                {
                    first = false;
                    FirstBatch = sw.Elapsed;
                    Select(keep is null ? _all.FirstOrDefault(r => r.IsHead) ?? _all.FirstOrDefault() : _all.FirstOrDefault(r => r.Entry.Hash == keep));
                }
                else if (keep is not null && Selected?.Entry.Hash != keep && batch.FirstOrDefault(r => r.Entry.Hash == keep) is { } k) Select(k);
            }
            await reader;
            Total = sw.Elapsed;
            Trace($"log first={FirstBatch.TotalMilliseconds:F0}ms total={Total.TotalMilliseconds:F0}ms rows={_all.Count}");
        }
        catch (OperationCanceledException) { }
        catch (GitException e) { Error = e.Message; }
        finally
        {
            if (_loadCts == cts) Loading = false;
            if (_searching) ApplySearch();
        }
    }

    string? _error;
    public string? Error { get => _error; private set => Set(ref _error, value); }

    // 검색 - 제목·작성자·해시 앞부분, 대소문자 무시. 입력 멈추고 0.2초 뒤
    string _search = "";
    bool _searching;
    DispatcherTimer? _searchTimer;
    public string Search
    {
        get => _search;
        set
        {
            if (!Set(ref _search, value)) return;
            _searchTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Normal, (_, _) => { _searchTimer!.Stop(); ApplySearch(); });
            _searchTimer.Stop();
            _searchTimer.Start();
        }
    }

    // 바로 적용 - 입력 대기 없이 (미리보기 도구 등)
    public void ApplySearch()
    {
        var q = _search.Trim();
        bool was = _searching;
        _searching = q.Length > 0;
        Graph.Hidden = _searching;
        if (_searching)
            _found.Reset(_all.Where(r => r.Subject.Contains(q, StringComparison.OrdinalIgnoreCase)
                || r.Author.Contains(q, StringComparison.OrdinalIgnoreCase)
                || r.Entry.Hash.StartsWith(q, StringComparison.OrdinalIgnoreCase)));
        if (was != _searching || _searching) Notify(nameof(Rows));
        Notify(nameof(CountText));
    }

    // 선택 커밋 상세
    LogRow? _selected;
    public LogRow? Selected { get => _selected; set => Select(value); }
    CommitInfo? _info;
    public CommitInfo? Info { get => _info; private set { Set(ref _info, value); Notify(nameof(HasBody)); Notify(nameof(BodyText)); Notify(nameof(MetaText)); } }
    public bool HasBody => !string.IsNullOrEmpty(Info?.Body);
    public string BodyText => Info is { } i ? Reflow.Body(i.Body) : "";
    public string MetaText => Info is { } i ? $"{i.Author} · {i.AuthorTime.ToLocalTime():yyyy-MM-dd HH:mm}" : "";
    public string ParentsText => Selected is { } s && s.Entry.Parents.Length > 0
        ? (s.Entry.IsMerge ? "Merge · " : "Parent · ") + string.Join(", ", s.Entry.Parents.Select(p => p[..Math.Min(8, p.Length)]))
        : "";
    public RangeList<ChangedFile> Changes { get; } = new();
    string _changesTitle = "Changes";
    public string ChangesTitle { get => _changesTitle; private set => Set(ref _changesTitle, value); }

    CancellationTokenSource? _detailCts;

    void Select(LogRow? row)
    {
        if (!Set(ref _selected, row, nameof(Selected))) return;
        Notify(nameof(ParentsText));
        _ = LoadDetailAsync(row);
    }

    async Task LoadDetailAsync(LogRow? row)
    {
        _detailCts?.Cancel();
        var cts = _detailCts = new CancellationTokenSource();
        Changes.Clear();
        ChangesTitle = "Changes";
        if (row is null) { Info = null; return; }
        try
        {
            var e = row.Entry;
            var info = Repo.Repo.ShowAsync(e.Hash, cts.Token);
            var files = Repo.Repo.ChangedFilesAsync(e.Hash, e.Parents.FirstOrDefault(), cts.Token);
            Info = await info;
            var list = await files;
            if (cts.IsCancellationRequested) return;
            Changes.Reset(list);
            ChangesTitle = $"Changes {list.Count}";
        }
        catch (OperationCanceledException) { }
        catch (GitException ex) { if (!cts.IsCancellationRequested) ChangesTitle = ex.Message; }
    }

    static void Trace(string line)
    {
        if (Environment.GetEnvironmentVariable("IEUM_TRACE") is { Length: > 0 } path)
            try { File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {line}\n"); } catch (IOException) { }
    }

    bool Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Notify(name);
        return true;
    }

    void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
