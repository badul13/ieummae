using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Ieummae.App.Theme;

// 색 묶음 하나 - 라이트(양털 크림) / 다크(밤 목장)
public sealed record Palette(
    bool Dark, string Bg, string Surface, string Surface2, string Line, string Text, string Muted,
    string Accent, string AccentText, string AccentLine, string AccentInk,
    string AddFg, string DelFg, string AddBg, string DelBg, string[] Lanes,
    string Stitch, string Shadow, string Wool, string WoolLine, string Muzzle, string Pupil, string Nose, string Blush);

// 봉제 양 테마 - 색 자원 등록, 전환 시 직접 그리는 컨트롤에 다시 그리기 알림
public static class Tone
{
    const string Fonts = "avares://ieummae/Assets/Fonts#";

    public static readonly Palette Light = new(false, "#EFE7D9", "#FFFCF6", "#F0E8DA", "#E6DCCB", "#3E393A", "#9B9089",
        "#B07A52", "#FFFFFF", "#B98E6C", "#86563A",
        "#5E8A62", "#BC6A58", "#E7EFE3", "#F6E3DC", ["#C98550", "#E58C9A", "#6FA3D8", "#5BB39C", "#E4B33F", "#9B87D4", "#9CB25A"],
        "#DCCDB6", "#E8DECD", "#FFFDF7", "#E3D6C2", "#6B4B3D", "#2C2420", "#E7A3A1", "#E9B5A8");

    public static readonly Palette Dark = new(true, "#24211F", "#2D2A27", "#36322E", "#423D38", "#EDE6DA", "#A59C90",
        "#D3A27A", "#24211F", "#7A6150", "#E8BD98",
        "#93C29A", "#DC917F", "#2A352B", "#3D2B26", ["#D9A273", "#EBA2AE", "#8FB8E3", "#7EC7B2", "#E8C463", "#B2A2E0", "#B5C878"],
        "#514A43", "#161412", "#46403A", "#5E564F", "#5E4236", "#BFAF98", "#D9948F", "#C98C80");

    public static bool IsDark { get; private set; }

    // 테마 전환 - 그릴 때 자원을 읽는 컨트롤은 이 알림으로 다시 그림
    public static event Action? Changed;

    public static void Apply(Application app, bool dark)
    {
        var p = dark ? Dark : Light;
        IsDark = dark;
        var r = app.Resources;
        void C(string k, string hex) => r[k] = new SolidColorBrush(Color.Parse(hex));

        r["FontJua"] = new FontFamily(Fonts + "Jua");
        r["FontHand"] = new FontFamily(Fonts + "Gaegu");
        r["FontMono"] = new FontFamily(Fonts + "Rec Mono Casual");
        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        C("Bg", p.Bg); C("Surface", p.Surface); C("Surface2", p.Surface2); C("Line", p.Line); C("Text", p.Text); C("Muted", p.Muted);
        C("Accent", p.Accent); C("AccentText", p.AccentText); C("AccentLine", p.AccentLine); C("AccentInk", p.AccentInk);
        C("AddFg", p.AddFg); C("DelFg", p.DelFg); C("AddBg", p.AddBg); C("DelBg", p.DelBg);
        // 선택·호버 - 강조색 반투명이라 뜨개 무늬 비침
        C("Sel", dark ? "#3AD3A27A" : "#26A57A55"); C("Hover", dark ? "#18D3A27A" : "#12A57A55");
        C("Stitch", p.Stitch); C("CardStitch", dark ? "#5A524A" : "#CDBDA3");
        // 원격 버튼 천 - 다크는 바탕과 묻혀서 한 톤 밝게
        C("RemoteBg", dark ? "#3A3531" : p.Surface);
        // 뜨개 패널 실 색 - 흰 양털(로그) / 오트밀(Changes)
        C("KnitBg", dark ? p.Surface : "#FFFFFE"); C("KnitLine", dark ? "#4F4842" : "#ECE7DF");
        C("Knit2Bg", dark ? "#332F2B" : "#FFFCF6"); C("Knit2Line", dark ? "#4E4741" : "#E0D2BC");
        C("Wool", p.Wool); C("WoolLine", p.WoolLine); C("Muzzle", p.Muzzle); C("Pupil", p.Pupil); C("Nose", p.Nose); C("Blush", p.Blush);
        // 단추 실 - 그래프 단추는 밝은 실, 눈 단추는 단추와 반대 명도 (다크는 상아 단추)
        C("Thread", dark ? "#F3ECE0" : "#FFFDF7"); C("EyeThread", dark ? "#4A3C33" : "#FFFDF7");
        r["PatchShadow"] = BoxShadows.Parse($"0 3 0 0 {p.Shadow}");
        r["PatchShadowSm"] = BoxShadows.Parse($"0 2 0 0 {p.Shadow}");
        for (int i = 0; i < p.Lanes.Length; i++)
        {
            var c = Color.Parse(p.Lanes[i]);
            r["G" + i] = c;
            r["GB" + i] = new SolidColorBrush(c);
            r["GS" + i] = new SolidColorBrush(Color.FromArgb(dark ? (byte)0x33 : (byte)0x26, c.R, c.G, c.B));
        }
        r["SystemAccentColor"] = Color.Parse(p.Accent);
        Changed?.Invoke();
    }

    // 직접 그리는 컨트롤 등록 - 화면에 붙어 있는 동안만 전환 알림 받음
    public static void Redraw(Visual v)
    {
        v.AttachedToVisualTree += (_, _) => Changed += v.InvalidateVisual;
        v.DetachedFromVisualTree += (_, _) => Changed -= v.InvalidateVisual;
    }

    // 그리기 코드용 자원 조회
    public static object Res(string key) => Application.Current!.Resources[key]!;
    public static IBrush Brush(string key) => (IBrush)Res(key);
    public static Color ColorOf(string key) => Res(key) is Color c ? c : ((SolidColorBrush)Res(key)).Color;
}
