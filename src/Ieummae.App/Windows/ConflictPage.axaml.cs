using Avalonia.Controls;
using Avalonia.Interactivity;
using Ieummae.Core.Conflict;
using Ieummae.Core.Git;

namespace Ieummae.App.Windows;

// 충돌 해결 화면 - 파일마다 덩어리별 Ours·Theirs·Both·직접 고치기, 다 풀면 Continue
public partial class ConflictPage : Page
{
    public ConflictModel? Model { get; }

    public ConflictPage() : this(null) { }

    public override string PageTitle => "Conflicts";

    public ConflictPage(ConflictModel? model)
    {
        InitializeComponent();
        DataContext = Model = model;
        if (model is null) return;
        Shown += async _ => await Task.WhenAll(model.LoadAsync(), model.Repo.LoadAsync());
        SaveButton.Click += async (_, _) => await Step("Resolve", model.SaveAsync());
        MarkButton.Click += async (_, _) => await Step("Resolve", model.SaveAsync());
        WholeOurs.Click += async (_, _) => await Step("Ours", model.TakeWholeAsync(ours: true));
        WholeTheirs.Click += async (_, _) => await Step("Theirs", model.TakeWholeAsync(ours: false));
        AllOurs.Click += (_, _) => model.ChooseAll(Choice.Ours);
        AllTheirs.Click += (_, _) => model.ChooseAll(Choice.Theirs);
        AllBoth.Click += (_, _) => model.ChooseAll(Choice.Both);
        ContinueButton.Click += async (_, _) => await FinishAsync(model.ContinueAsync(), "Continue");
        SkipButton.Click += async (_, _) =>
        {
            if (await ConfirmAsync("Skip", "지금 커밋을 빼고 다음 커밋으로 - 이 커밋 변경은 사라짐", "Skip"))
                await FinishAsync(model.SkipAsync(), "Skip");
        };
        AbortButton.Click += async (_, _) =>
        {
            if (await ConfirmAsync("Abort", $"{model.OperationText.Replace(" 중", "")} 전체 취소 - 시작 전 상태로 되돌림 (고친 내용 사라짐)", "Abort"))
                await FinishAsync(model.AbortAsync(), "Abort");
        };
    }

    // 덩어리 고르기 버튼 - Tag 가 고를 쪽
    void OnPick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag, DataContext: ConflictBlockView b }) b.Choice = Enum.Parse<Choice>(tag);
    }

    async Task Step(string what, Task<GitResult> run)
    {
        await CheckAsync(what, run);
        await Model!.LoadAsync();
    }

    // 계속·건너뛰기·취소 - 리베이스는 다음 커밋에서 또 충돌할 수 있으니 목록 다시 확인, 끝나면 앞 화면으로
    async Task FinishAsync(Task<GitResult> run, string what)
    {
        var r = await run;
        await Model!.LoadAsync();
        await Model.Repo.LoadAsync();
        if (Model.Operation == Operation.None && Model.Files.Count == 0)
        {
            if (!r.Ok) await AlertAsync(what + " 실패", r.Message);
            // 앞 화면 있으면 돌아가고, 탐색기에서 바로 열었으면 로그로
            if (Host.Previous is not null) GoBack();
            else Host.Replace(new LogPage(new LogModel(Model.Repo, new Ieummae.Core.Log.LogQuery())));
            return;
        }
        if (!r.Ok && Model.Files.Count == 0) await AlertAsync(what + " 실패", r.Message);
    }
}
