using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Ieummae.Core.Git;

namespace Ieummae.App.Windows;

// 창 공통 - 제목 표시줄을 내용 영역으로 확장 (양털 머리가 맨 위까지), 창 안 대화 상자
public class IeumWindow : Window
{
    protected override Type StyleKeyOverride => typeof(Window);

    readonly Border _dim = new() { IsVisible = false, Background = new SolidColorBrush(Colors.Black, 0.32) };
    Action? _cancel;
    Action? _accept;

    public IeumWindow()
    {
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = 40;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Bind(BackgroundProperty, this.GetResourceObservable("Bg"));
        Bind(FontFamilyProperty, this.GetResourceObservable("FontJua"));
        FontSize = 14.5;
        AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    // XAML 내용 위에 대화 상자 층 얹기 - 처음 대화 상자를 열 때
    void EnsureLayer()
    {
        if (_dim.Parent is not null || Content is not Control c) return;
        Content = null;
        Content = new Panel { Children = { c, _dim } };
    }

    public bool DialogOpen => _dim.IsVisible;

    void OnKey(object? s, KeyEventArgs e)
    {
        if (!_dim.IsVisible) return;
        if (e.Key == Key.Escape) { _cancel?.Invoke(); e.Handled = true; }
        else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && _accept is not null && e.Source is not TextBox { AcceptsReturn: true })
        { _accept(); e.Handled = true; }
    }

    // 대화 상자 틀 - 가운데 천 조각 카드, 제목 + 내용 + 오른쪽 아래 버튼
    Task<T> ShowCard<T>(string title, Control? body, IReadOnlyList<(string Text, bool Hot, Func<T> Result)> buttons, T cancel, Control? focus = null)
    {
        EnsureLayer();
        var tcs = new TaskCompletionSource<T>();
        void Close(T value)
        {
            _dim.IsVisible = false;
            _dim.Child = null;
            _cancel = _accept = null;
            tcs.TrySetResult(value);
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var (text, hot, result) in buttons)
        {
            var b = new Button { Content = text, Classes = { "patch" } };
            if (hot) b.Classes.Add("hot");
            b.Click += (_, _) => Close(result());
            row.Children.Add(b);
        }
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 20 });
        if (body is not null) stack.Children.Add(body);
        stack.Children.Add(row);
        var card = new ContentControl
        {
            Classes = { "patch", "hot" },
            Content = stack,
            MinWidth = 380,
            MaxWidth = 560,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(28, 22, 24, 20),
        };
        _dim.Child = card;
        _dim.IsVisible = true;
        _cancel = () => Close(cancel);
        var hotButton = buttons.FirstOrDefault(b => b.Hot);
        _accept = hotButton.Text is null ? null : () => Close(hotButton.Result());
        Avalonia.Threading.Dispatcher.UIThread.Post(() => (focus ?? row.Children.LastOrDefault() as Control)?.Focus());
        return tcs.Task;
    }

    static TextBlock Hand(string text) => new() { Text = text, Classes = { "hand" }, TextWrapping = TextWrapping.Wrap };

    // 확인 - 위험한 동작은 ok 문구를 동작 이름으로
    public Task<bool> ConfirmAsync(string title, string message, string ok = "OK") =>
        ShowCard(title, Hand(message), [("Cancel", false, () => false), (ok, true, () => true)], false);

    public Task AlertAsync(string title, string message) =>
        ShowCard(title, new SelectableTextBlock { Text = message, Classes = { "hand" }, TextWrapping = TextWrapping.Wrap, MaxHeight = 320 },
            [("OK", true, () => true)], true);

    // 이름 입력 - 선택 사항 체크 하나
    public async Task<(string Text, bool Check)?> PromptAsync(string title, string label, string initial = "", string ok = "OK", string? check = null, bool checkDefault = false, string? second = null)
    {
        var box = new TextBox { Text = initial, Classes = { "sew" }, MinWidth = 360 };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Hand(label));
        body.Children.Add(box);
        TextBox? box2 = null;
        if (second is not null)
        {
            body.Children.Add(Hand(second));
            body.Children.Add(box2 = new TextBox { Classes = { "sew" }, AcceptsReturn = true, Height = 70, TextWrapping = TextWrapping.Wrap });
        }
        CheckBox? cb = check is null ? null : new CheckBox { Content = check, IsChecked = checkDefault };
        if (cb is not null) body.Children.Add(cb);
        box.AttachedToVisualTree += (_, _) => box.SelectAll();
        var r = await ShowCard<(string, bool)?>(title, body,
            [("Cancel", false, () => null), (ok, true, () => ((box.Text ?? "").Trim(), cb?.IsChecked == true))], null, box);
        if (r is { } v && box2 is not null) SecondText = box2.Text ?? "";
        return r is { Item1.Length: > 0 } ? r : null;
    }

    // 두 번째 입력 칸 내용 (태그 메시지 등)
    public string SecondText { get; private set; } = "";

    // 고르기 - 설명 붙은 항목 중 하나
    public Task<int> ChooseAsync(string title, string message, IReadOnlyList<(string Name, string Detail)> options, string ok, int selected = 0)
    {
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(Hand(message));
        var radios = new List<RadioButton>();
        for (int i = 0; i < options.Count; i++)
        {
            var rb = new RadioButton { GroupName = "choose", IsChecked = i == selected };
            var t = new StackPanel();
            t.Children.Add(new TextBlock { Text = options[i].Name });
            t.Children.Add(new TextBlock { Text = options[i].Detail, Classes = { "hand" }, FontSize = 14, TextWrapping = TextWrapping.Wrap });
            rb.Content = t;
            radios.Add(rb);
            body.Children.Add(rb);
        }
        return ShowCard(title, body, [("Cancel", false, () => -1), (ok, true, () => radios.FindIndex(r => r.IsChecked == true))], -1);
    }

    public sealed record PickResult(int Index, int Action, bool Check);

    // 목록에서 고르기 - 위 검색 칸으로 거르고, 버튼마다 다른 동작 (예: Apply / Pop / Drop)
    public async Task<PickResult?> PickAsync(string title, string message, IReadOnlyList<(string Name, string Detail)> items,
        IReadOnlyList<string> actions, string? check = null, bool checkDefault = false)
    {
        var filter = new TextBox { Classes = { "sew" }, PlaceholderText = "거르기", IsVisible = items.Count > 8 };
        var list = new ListBox { Height = Math.Min(320, Math.Max(1, items.Count) * 46 + 8), MinWidth = 440, SelectionMode = SelectionMode.Single };
        list.Classes.Add("pick");
        var view = items.Select((x, i) => (x.Name, x.Detail, i)).ToList();
        void Fill()
        {
            var q = filter.Text?.Trim() ?? "";
            list.ItemsSource = view
                .Where(v => q.Length == 0 || v.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || v.Detail.Contains(q, StringComparison.OrdinalIgnoreCase))
                .Select(v =>
                {
                    var sp = new StackPanel { Tag = v.i, Margin = new Thickness(8, 4) };
                    sp.Children.Add(new TextBlock { Text = v.Name, FontSize = 15 });
                    if (v.Detail.Length > 0) sp.Children.Add(new TextBlock { Text = v.Detail, Classes = { "hand" }, FontSize = 14, TextTrimming = Ieummae.App.Theme.DotsTrimming.End });
                    return sp;
                }).ToList();
            list.SelectedIndex = 0;
        }
        filter.TextChanged += (_, _) => Fill();
        Fill();
        var body = new StackPanel { Spacing = 8 };
        if (message.Length > 0) body.Children.Add(Hand(message));
        body.Children.Add(filter);
        body.Children.Add(items.Count == 0 ? Hand("항목 없음") : list);
        CheckBox? cb = check is null ? null : new CheckBox { Content = check, IsChecked = checkDefault };
        if (cb is not null) body.Children.Add(cb);
        int Picked() => list.SelectedItem is StackPanel { Tag: int i } ? i : -1;
        var buttons = new List<(string, bool, Func<PickResult?>)> { ("Cancel", false, () => null) };
        for (int a = 0; a < actions.Count; a++)
        {
            int act = a;
            buttons.Add((actions[a], a == 0, () => Picked() is >= 0 and var i ? new PickResult(i, act, cb?.IsChecked == true) : null));
        }
        list.DoubleTapped += (_, _) => _accept?.Invoke();
        return await ShowCard<PickResult?>(title, body, buttons, null, items.Count > 8 ? filter : list);
    }

    // 진행 상자 - git 진행 줄을 그대로 보여 주고, 끝나면 닫힘. 실패면 오류 상자로 바뀜
    public async Task<GitResult> ProgressAsync(string title, Func<Action<string>, CancellationToken, Task<GitResult>> work)
    {
        using var cts = new CancellationTokenSource();
        var lines = new TextBlock { Classes = { "hand" }, TextWrapping = TextWrapping.Wrap, FontSize = 15, MinHeight = 88, MaxHeight = 160, MaxWidth = 500 };
        var last = new List<string>();
        void OnLine(string line) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // 같은 단계의 진행률 줄(예: "Receiving objects:  45%")은 덮어씀
            var key = line.Split(':')[0];
            if (last.Count > 0 && last[^1].Split(':')[0] == key) last[^1] = line; else last.Add(line);
            if (last.Count > 6) last.RemoveAt(0);
            lines.Text = string.Join("\n", last);
        });
        lines.Text = "시작";
        var card = ShowCard(title, lines, [("Cancel", false, () => false)], false);
        var run = work(OnLine, cts.Token);
        var done = await Task.WhenAny(run, card);
        if (done == card)
        {
            // 취소 - 진행 중인 git 프로세스 종료
            cts.Cancel();
            try { await run; } catch (OperationCanceledException) { }
            return new GitResult(-1, "", "취소됨");
        }
        GitResult result;
        try { result = await run; }
        catch (OperationCanceledException) { result = new GitResult(-1, "", "취소됨"); }
        _cancel?.Invoke();
        if (!result.Ok) await AlertAsync(title + " 실패", result.Message);
        return result;
    }

    // git 실행 결과 확인 - 실패면 메시지 띄우고 false
    public async Task<bool> CheckAsync(string what, Task<GitResult> run)
    {
        var r = await run;
        if (!r.Ok) await AlertAsync(what + " 실패", r.Message);
        return r.Ok;
    }
}
