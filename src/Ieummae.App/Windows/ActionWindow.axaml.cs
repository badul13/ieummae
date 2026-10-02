namespace Ieummae.App.Windows;

// 동작 하나 실행 후 닫힘 - 성공하면 짧게 알리고, 취소·실패면 (실패 메시지는 동작 안에서 이미 보임) 바로 닫음
public partial class ActionWindow : IeumWindow
{
    public ActionWindow() : this(null, "", _ => Task.FromResult(false)) { }

    public ActionWindow(RepoModel? model, string title, Func<Actions, Task<bool>> run)
    {
        InitializeComponent();
        DataContext = model;
        TitleText.Text = title;
        if (model is null) return;
        var act = new Actions(this, model);
        act.OnConflicts = () => { new ConflictWindow(new ConflictModel(model)).Show(); return Task.CompletedTask; };
        Opened += async (_, _) =>
        {
            await model.LoadAsync();
            bool ok = await run(act);
            // 충돌로 멈춘 경우 - 완료 알림 없이 (충돌 창이 따로 뜸)
            if (ok && await model.Repo.OperationAsync() == Ieummae.Core.Git.Operation.None)
            {
                await model.LoadAsync();
                await AlertAsync(title, $"완료 · {model.Branch} {model.SyncText}".Trim());
            }
            Close();
        };
    }
}
