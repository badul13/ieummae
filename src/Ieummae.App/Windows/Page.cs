using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Ieummae.Core.Git;

namespace Ieummae.App.Windows;

// 대화 상자 - 창(IeumWindow)과 화면(Page) 둘 다, 동작 모음(Actions)이 씀
public interface IDialogs
{
    Task<bool> ConfirmAsync(string title, string message, string ok = "OK");
    Task AlertAsync(string title, string message);
    Task<(string Text, bool Check)?> PromptAsync(string title, string label, string initial = "", string ok = "OK", string? check = null, bool checkDefault = false, string? second = null);
    Task<int> ChooseAsync(string title, string message, IReadOnlyList<(string Name, string Detail)> options, string ok, int selected = 0);
    Task<IeumWindow.PickResult?> PickAsync(string title, string message, IReadOnlyList<(string Name, string Detail)> items, IReadOnlyList<string> actions, string? check = null, bool checkDefault = false);
    Task<GitResult> ProgressAsync(string title, Func<Action<string>, CancellationToken, Task<GitResult>> work);
    Task<bool> CheckAsync(string what, Task<GitResult> run);
    string SecondText { get; }
}

// 창 안 화면 하나 - 로그·커밋·diff 등. 양털 머리 아래 내용만, 이동은 MainWindow 가 맡음
public abstract class Page : UserControl, IDialogs
{
    // 이 화면이 놓인 창 - 화면에 붙은 뒤에만 유효
    public MainWindow Host => TopLevel.GetTopLevel(this) as MainWindow
        ?? throw new InvalidOperationException("창에 붙기 전");

    // 뒤로 가기 버튼·창 제목에 쓰는 이름
    public abstract string PageTitle { get; }

    // 화면에 나올 때 - first: 처음 / false: 다른 화면에서 돌아옴 (새로 고침 기회)
    public event Action<bool>? Shown;
    internal void RaiseShown(bool first) => Shown?.Invoke(first);

    // 창 단위 키 (F5, Ctrl+Enter 등) - 지금 보이는 화면만 받음
    public virtual void OnKey(KeyEventArgs e) { }

    protected void Navigate(Page page) => Host.Navigate(page);
    // 뒤로 - 첫 화면이면 창 닫기
    protected void GoBack() => Host.Back();
    protected IClipboard? Clipboard => Host.Clipboard;
    protected bool DialogOpen => Host.DialogOpen;
    // 우클릭 메뉴 - 생성자에서 불리므로 창은 누를 때 찾음
    protected void AttachMenu(Control target, Action<MenuFlyout> build)
    {
        target.ContextRequested += (_, e) =>
        {
            if (DialogOpen) return;
            var menu = new MenuFlyout();
            build(menu);
            if (menu.Items.Count == 0) return;
            menu.ShowAt(target, showAtPointer: true);
            e.Handled = true;
        };
    }

    readonly Dictionary<Button, CancellationTokenSource> _busy = [];

    // 진행 중인 버튼이면 취소하고 true - 버튼 Click 처리 맨 앞에서
    protected bool CancelBusy(Button b)
    {
        if (!_busy.TryGetValue(b, out var cts)) return false;
        cts.Cancel();
        return true;
    }

    // 버튼 위에서 실행 (팝업 없음) - 박음질이 진해지며 돌아감, 마지막 진행 줄은 풍선 도움말로, 실패할 때만 오류 상자
    protected async Task<GitResult> RunOnButtonAsync(Button b, string title, Func<Action<string>, CancellationToken, Task<GitResult>> work)
    {
        using var cts = new CancellationTokenSource();
        _busy[b] = cts;
        b.Classes.Add("busy");
        ToolTip.SetTip(b, "진행 중 - 누르면 취소");
        try
        {
            var r = await work(line => Avalonia.Threading.Dispatcher.UIThread.Post(() => ToolTip.SetTip(b, line + " - 누르면 취소")), cts.Token);
            if (!r.Ok) await AlertAsync(title + " 실패", r.Message);
            return r;
        }
        catch (OperationCanceledException) { return new GitResult(-1, "", "취소됨"); }
        finally
        {
            _busy.Remove(b);
            b.Classes.Remove("busy");
            ToolTip.SetTip(b, null);
        }
    }

    public Task<bool> ConfirmAsync(string title, string message, string ok = "OK") => Host.ConfirmAsync(title, message, ok);
    public Task AlertAsync(string title, string message) => Host.AlertAsync(title, message);
    public Task<(string Text, bool Check)?> PromptAsync(string title, string label, string initial = "", string ok = "OK", string? check = null, bool checkDefault = false, string? second = null) =>
        Host.PromptAsync(title, label, initial, ok, check, checkDefault, second);
    public Task<int> ChooseAsync(string title, string message, IReadOnlyList<(string Name, string Detail)> options, string ok, int selected = 0) =>
        Host.ChooseAsync(title, message, options, ok, selected);
    public Task<IeumWindow.PickResult?> PickAsync(string title, string message, IReadOnlyList<(string Name, string Detail)> items, IReadOnlyList<string> actions, string? check = null, bool checkDefault = false) =>
        Host.PickAsync(title, message, items, actions, check, checkDefault);
    public Task<GitResult> ProgressAsync(string title, Func<Action<string>, CancellationToken, Task<GitResult>> work) => Host.ProgressAsync(title, work);
    public Task<bool> CheckAsync(string what, Task<GitResult> run) => Host.CheckAsync(what, run);
    public string SecondText => Host.SecondText;
}
