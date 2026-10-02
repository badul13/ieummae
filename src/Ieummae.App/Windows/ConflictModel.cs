using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Ieummae.Core.Conflict;
using Ieummae.Core.Git;

namespace Ieummae.App.Windows;

// 충돌 파일 한 줄
public sealed record ConflictFileRow(WorkingFile File)
{
    public string Name => File.Name;
    public string Dir => File.Dir;
    public ConflictKind Kind => Repository.KindOf(File);
    public string KindText => Kind switch
    {
        ConflictKind.BothModified => "Both modified",
        ConflictKind.DeletedByUs => "Deleted by us",
        ConflictKind.DeletedByThem => "Deleted by them",
        ConflictKind.BothAdded => "Both added",
        ConflictKind.BothDeleted => "Both deleted",
        _ => "Conflict",
    };
}

// 화면 덩어리 - 공통(접어서) / 충돌(고르기)
public abstract class BlockView : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string name = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class CommonBlockView(CommonSegment s) : BlockView
{
    // 긴 공통 부분은 앞뒤 3줄만
    public string Text { get; } = s.Lines.Count <= 8
        ? string.Join("\n", s.Lines)
        : string.Join("\n", s.Lines.Take(3)) + $"\n  ··· {s.Lines.Count - 6}줄 ···\n" + string.Join("\n", s.Lines.TakeLast(3));
}

public sealed class ConflictBlockView(ConflictSegment s, int number, Action changed) : BlockView
{
    public ConflictSegment Segment { get; } = s;
    public string Number { get; } = $"#{number}";
    public string OursText => string.Join("\n", Segment.Ours);
    public string TheirsText => string.Join("\n", Segment.Theirs);
    public string OursHeader => $"Ours · {Segment.OursLabel}";
    public string TheirsHeader => $"Theirs · {Segment.TheirsLabel}";

    public Choice Choice
    {
        get => Segment.Choice;
        set
        {
            if (Segment.Choice == value) return;
            // 직접 고치기로 넘어갈 때 - 지금 고른 내용(없으면 양쪽)에서 시작
            if (value == Choice.Custom)
                Segment.Custom = string.Join("\n", Segment.Choice is Choice.None or Choice.Custom ? Segment.Ours.Concat(Segment.Theirs) : Segment.Resolved);
            Segment.Choice = value;
            foreach (var n in new[] { nameof(Choice), nameof(IsOurs), nameof(IsTheirs), nameof(IsBoth), nameof(IsCustom), nameof(OursOpacity), nameof(TheirsOpacity), nameof(Custom) }) Notify(n);
            changed();
        }
    }
    public bool IsOurs => Choice == Choice.Ours;
    public bool IsTheirs => Choice == Choice.Theirs;
    public bool IsBoth => Choice == Choice.Both;
    public bool IsCustom => Choice == Choice.Custom;
    // 안 고른 쪽은 흐리게
    public double OursOpacity => Choice is Choice.None or Choice.Ours or Choice.Both ? 1 : 0.4;
    public double TheirsOpacity => Choice is Choice.None or Choice.Theirs or Choice.Both ? 1 : 0.4;
    public string Custom { get => Segment.Custom; set { Segment.Custom = value; Notify(); } }
}

// 충돌 창 상태
public sealed class ConflictModel(RepoModel repo) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public RepoModel Repo { get; } = repo;
    Repository Git => Repo.Repo;

    public List<ConflictFileRow> Files { get; private set; } = [];
    Operation _op;
    public Operation Operation { get => _op; private set { Set(ref _op, value); Notify(nameof(IsRebase)); } }
    public bool IsRebase => Operation == Operation.Rebase;
    string _opText = "";
    public string OperationText { get => _opText; private set => Set(ref _opText, value); }
    public string Hint => IsRebase ? "Rebase 중 - Ours 는 옮겨 얹는 대상, Theirs 는 내 커밋" : "Ours 는 지금 브랜치, Theirs 는 합치려는 쪽";
    public bool CanContinue => Files.Count == 0 && Operation != Operation.None;

    ConflictFileRow? _selected;
    public ConflictFileRow? Selected { get => _selected; set { if (Set(ref _selected, value)) _ = OpenAsync(value); } }

    List<BlockView> _blocks = [];
    public List<BlockView> Blocks { get => _blocks; private set => Set(ref _blocks, value); }
    string? _notice;
    public string? Notice { get => _notice; private set => Set(ref _notice, value); }
    public bool CanSave => _text is { } t && t.AllChosen && t.Conflicts.Any();
    public bool CanPickWhole => Selected is not null;
    public string WholeOurs => Selected?.Kind == ConflictKind.DeletedByUs ? "Ours (삭제)" : "Ours 전부";
    public string WholeTheirs => Selected?.Kind == ConflictKind.DeletedByThem ? "Theirs (삭제)" : "Theirs 전부";

    ConflictText? _text;
    bool _bom;

    public async Task LoadAsync()
    {
        var keep = Selected?.File.Path;
        var status = await Git.StatusAsync();
        // 덩어리를 고를 수 있는 양쪽 수정 파일 먼저
        Files = status.Files.Where(f => f.IsConflict).Select(f => new ConflictFileRow(f))
            .OrderBy(f => f.Kind is ConflictKind.BothModified or ConflictKind.BothAdded ? 0 : 1).ThenBy(f => f.File.Path, StringComparer.OrdinalIgnoreCase).ToList();
        Notify(nameof(Files));
        Operation = await Git.OperationAsync();
        var step = Operation == Operation.Rebase ? await Git.RebaseStepAsync() : null;
        OperationText = Operation switch
        {
            Operation.Merge => "Merge 중",
            Operation.Rebase => step is var (n, e) ? $"Rebase 중 {n}/{e}" : "Rebase 중",
            Operation.CherryPick => "Cherry-pick 중",
            Operation.Revert => "Revert 중",
            _ => "",
        };
        Notify(nameof(Hint));
        Notify(nameof(CanContinue));
        Selected = Files.FirstOrDefault(f => f.File.Path == keep) ?? Files.FirstOrDefault();
        if (Selected is null) { Blocks = []; _text = null; Notice = Files.Count == 0 ? (Operation == Operation.None ? "충돌 없음" : "충돌 모두 해결 - Continue 로 마무리") : null; Changed(); }
    }

    // 파일 열기 - UTF-8 이 아니거나 바이너리면 통째로 고르기만
    async Task OpenAsync(ConflictFileRow? row)
    {
        _text = null;
        Blocks = [];
        Notice = null;
        Notify(nameof(WholeOurs)); Notify(nameof(WholeTheirs)); Notify(nameof(CanPickWhole));
        if (row is null) { Changed(); return; }
        if (row.Kind != ConflictKind.BothModified && row.Kind != ConflictKind.BothAdded)
        {
            Notice = row.Kind switch
            {
                ConflictKind.DeletedByUs => "지금 브랜치(Ours)에서 지운 파일을 상대(Theirs)가 고침 - 남길지 지울지 고르기",
                ConflictKind.DeletedByThem => "상대(Theirs)가 지운 파일을 지금 브랜치(Ours)에서 고침 - 남길지 지울지 고르기",
                _ => "파일 통째로 한쪽 고르기",
            };
            Changed();
            return;
        }
        var path = Path.Combine(Git.Root, row.File.Path);
        byte[] bytes;
        try { bytes = await File.ReadAllBytesAsync(path); }
        catch (IOException e) { Notice = e.Message; Changed(); return; }
        _bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        string content;
        try { content = new UTF8Encoding(false, true).GetString(bytes, _bom ? 3 : 0, bytes.Length - (_bom ? 3 : 0)); }
        catch (DecoderFallbackException) { Notice = "UTF-8 아닌 파일 - 통째로 한쪽 고르기만 가능"; Changed(); return; }
        if (content.Contains('\0')) { Notice = "바이너리 파일 - 통째로 한쪽 고르기만 가능"; Changed(); return; }

        var text = ConflictText.Parse(content);
        if (!text.Conflicts.Any()) { Notice = "충돌 표시 없음 - 이미 고친 파일이면 Resolve 로 해결 표시"; _text = text; Changed(); return; }
        _text = text;
        int n = 0;
        Blocks = text.Segments.Select(s => s switch
        {
            ConflictSegment c => (BlockView)new ConflictBlockView(c, ++n, Changed),
            CommonSegment c => new CommonBlockView(c),
            _ => throw new InvalidOperationException(),
        }).ToList();
        Changed();
    }

    void Changed() { Notify(nameof(CanSave)); Notify(nameof(CanMarkAsIs)); }

    // 충돌 표시 없는 파일 - 그대로 해결 표시
    public bool CanMarkAsIs => _text is { } t && !t.Conflicts.Any();

    // 고른 대로 저장하고 해결 표시
    public async Task<GitResult> SaveAsync()
    {
        if (Selected is not { } row || _text is not { } text) return new GitResult(1, "", "열린 파일 없음");
        if (text.Conflicts.Any())
        {
            var content = new UTF8Encoding(_bom).GetBytes(text.Build());
            var bytes = _bom ? [0xEF, 0xBB, 0xBF, .. content] : content;
            await File.WriteAllBytesAsync(Path.Combine(Git.Root, row.File.Path), bytes);
        }
        return await Git.MarkResolvedAsync(row.File.Path);
    }

    public Task<GitResult> TakeWholeAsync(bool ours) =>
        Selected is { } row ? Git.TakeWholeAsync(row.File, ours) : Task.FromResult(new GitResult(1, "", "고른 파일 없음"));

    public void ChooseAll(Choice c)
    {
        foreach (var b in Blocks.OfType<ConflictBlockView>()) b.Choice = c;
    }

    public Task<GitResult> ContinueAsync() => Git.ContinueAsync(Operation);
    public Task<GitResult> AbortAsync() => Git.AbortAsync(Operation);
    public Task<GitResult> SkipAsync() => Git.SkipAsync();

    bool Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Notify(name);
        return true;
    }

    void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
