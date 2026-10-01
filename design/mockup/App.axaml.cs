using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

namespace IeumMock;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Tones.Apply(this, Program.Tone, Program.Pal);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Window w = (Program.Tone, Program.View) switch
            {
                ("b", "commit") => new CommitB(),
                ("b", _) => new LogB(),
                (_, "commit") => new CommitB(),
                _ => new LogB(),
            };
            w.Title = "ieummae-" + Program.Id;
            desktop.MainWindow = w;
        }
        base.OnFrameworkInitializationCompleted();
    }
}

