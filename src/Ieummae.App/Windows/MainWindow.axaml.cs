using Avalonia.Input;

namespace Ieummae.App.Windows;

// 창 하나 + 화면 이동 - 앞 화면은 쌓아 두고 뒤로 가면 그대로 다시 보여 줌 (스크롤·선택 유지)
public partial class MainWindow : IeumWindow
{
    readonly Stack<Page> _back = new();

    // 창 제목 앞부분 - 저장소 이름
    public string RepoName { get; set; } = "";
    // 측정 도구용 고정 제목 (ieummae-<id>)
    public string? FixedTitle { get; set; }


    public MainWindow() : this(null, "") { }

    public MainWindow(Page? first, string repoName)
    {
        InitializeComponent();
        RepoName = repoName;
        BackButton.Click += (_, _) => Back();
        // 창 단위 키 - 입력 칸이 먼저 먹지 않게 내려가는 단계(Tunnel)에서 받음
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (DialogOpen || e.Handled) return;
            if (e.Key == Key.Left && e.KeyModifiers == KeyModifiers.Alt) { Back(); e.Handled = true; return; }
            Current?.OnKey(e);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        if (first is not null) Opened += (_, _) => ShowPage(first, true);
    }

    public Page? Current => PageHost.Content as Page;
    // 바로 앞 화면 - 뒤로 가면 나올 화면
    public Page? Previous => _back.Count > 0 ? _back.Peek() : null;

    public void Navigate(Page page)
    {
        if (Current is { } cur) _back.Push(cur);
        ShowPage(page, true);
    }

    // 뒤로 - 쌓인 화면 없으면 창 닫기
    public void Back()
    {
        if (_back.Count == 0) { Close(); return; }
        ShowPage(_back.Pop(), false);
    }

    // 지금 화면 바꿔치기 (뒤로 쌓지 않음) - 탐색기 동작이 끝난 뒤 로그로 등
    public void Replace(Page page) => ShowPage(page, true);

    void ShowPage(Page page, bool first)
    {
        PageHost.Content = page;
        BackButton.IsVisible = _back.Count > 0;
        if (_back.Count > 0) BackText.Text = _back.Peek().PageTitle;
        Title = FixedTitle ?? (string.IsNullOrEmpty(RepoName) ? $"ieummae · {page.PageTitle}" : $"{RepoName} · {page.PageTitle}");
        page.RaiseShown(first);
    }
}
