using Avalonia;
using Ieummae.App.Platform;
using Ieummae.App.Theme;
using Ieummae.Core.Git;

namespace Ieummae.App.Windows;

// 설정 화면 - 저장하거나 취소하면 앞 화면으로
public partial class SettingsPage : Page
{
    public SettingsPage() : this(null) { }

    public override string PageTitle => "설정";

    // repo - 저장소 안에서 열었으면 그 저장소 설정 칸도
    public SettingsPage(Repository? repo)
    {
        InitializeComponent();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        LocalCard.IsVisible = repo is not null;
        if (repo is not null) LocalTitle.Text = $"커밋 작성자 · {repo.Name}";
        (Settings.Theme switch { "light" => ThemeLight, "dark" => ThemeDark, _ => ThemeSystem }).IsChecked = true;
        DiffSplit.IsChecked = Settings.DiffSplit;
        DiffWs.IsChecked = Settings.DiffIgnoreWhitespace;

        Shown += async first =>
        {
            if (!first) return;
            GlobalName.Text = await GitTools.GetConfigAsync(home, "--global", "user.name");
            GlobalEmail.Text = await GitTools.GetConfigAsync(home, "--global", "user.email");
            if (repo is null) return;
            LocalName.Text = await GitTools.GetConfigAsync(repo.Root, "--local", "user.name");
            LocalEmail.Text = await GitTools.GetConfigAsync(repo.Root, "--local", "user.email");
            LocalName.PlaceholderText = GlobalName.Text is { Length: > 0 } g ? $"이름 - 비우면 {g}" : "이름";
            LocalEmail.PlaceholderText = GlobalEmail.Text is { Length: > 0 } m ? $"메일 - 비우면 {m}" : "메일";
        };
        CancelButton.Click += (_, _) => GoBack();
        SaveButton.Click += async (_, _) =>
        {
            // git 설정 - 바뀐 칸만
            async Task<bool> Save(string dir, string scope, string key, string? value)
            {
                var now = await GitTools.GetConfigAsync(dir, scope, key) ?? "";
                var v = (value ?? "").Trim();
                return v == now || await CheckAsync("설정 저장", GitTools.SetConfigAsync(dir, scope, key, v));
            }
            if (!await Save(home, "--global", "user.name", GlobalName.Text)) return;
            if (!await Save(home, "--global", "user.email", GlobalEmail.Text)) return;
            if (repo is not null)
            {
                if (!await Save(repo.Root, "--local", "user.name", LocalName.Text)) return;
                if (!await Save(repo.Root, "--local", "user.email", LocalEmail.Text)) return;
            }
            Settings.Theme = ThemeLight.IsChecked == true ? "light" : ThemeDark.IsChecked == true ? "dark" : "system";
            Settings.DiffSplit = DiffSplit.IsChecked == true;
            Settings.DiffIgnoreWhitespace = DiffWs.IsChecked == true;
            // 테마 바로 적용
            var app = Application.Current!;
            bool system = app.PlatformSettings?.GetColorValues().ThemeVariant == Avalonia.Platform.PlatformThemeVariant.Dark;
            Tone.Apply(app, Settings.Theme switch { "light" => false, "dark" => true, _ => system });
            GoBack();
        };
    }
}
