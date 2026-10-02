using Avalonia.Controls;

namespace Ieummae.App.Windows;

public partial class BranchChip : UserControl
{
    public BranchChip() => InitializeComponent();
}

public partial class LogWindow : IeumWindow
{
    public LogWindow() : this(null) { }

    public LogWindow(RepoModel? model)
    {
        InitializeComponent();
        DataContext = model;
    }
}

public partial class CommitWindow : IeumWindow
{
    public CommitWindow() : this(null) { }

    public CommitWindow(RepoModel? model)
    {
        InitializeComponent();
        DataContext = model;
    }
}

// 안내 창 - 저장소 아님, 모르는 명령 등
public partial class MessageWindow : IeumWindow
{
    public MessageWindow() : this("", "") { }

    public MessageWindow(string title, string detail)
    {
        InitializeComponent();
        Title = "ieummae · " + title;
        this.FindControl<TextBlock>("TitleText")!.Text = title;
        this.FindControl<TextBlock>("DetailText")!.Text = detail;
    }
}
