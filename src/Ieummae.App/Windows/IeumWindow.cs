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

    // git 실행 결과 확인 - 실패면 메시지 띄우고 false
    public async Task<bool> CheckAsync(string what, Task<GitResult> run)
    {
        var r = await run;
        if (!r.Ok) await AlertAsync(what + " 실패", r.Message);
        return r.Ok;
    }
}
