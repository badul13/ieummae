using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace IeumMock;

public sealed record Palette(
    string Name, bool Dark, string Bg, string Surface, string Surface2, string Line, string Text, string Muted,
    string Accent, string AccentText, string AccentSoft, string AccentLine, string AccentInk,
    string Sel, string AddFg, string DelFg, string AddBg, string DelBg, string[] G,
    string Stitch, string Shadow, string Wool, string WoolLine, string Muzzle, string EyeRim, string Pupil, string Nose, string Blush);

// 봉제 양 테마 - 라이트(양털 크림) / 다크(밤 목장)
public static class Tones
{
    const string F = "avares://ieum-mock/Assets/Fonts#";

    public static readonly List<Palette> All = new()
    {
        new("Light", false, "#EFE7D9", "#FFFCF6", "#F0E8DA", "#E6DCCB", "#3E393A", "#9B9089",
            "#B07A52", "#FFFFFF", "#EEDCCB", "#B98E6C", "#86563A",
            "#F1E6D8", "#5E8A62", "#BC6A58", "#E7EFE3", "#F6E3DC", new[] { "#C98550", "#E58C9A", "#6FA3D8", "#5BB39C", "#E4B33F", "#9B87D4", "#9CB25A" },
            "#DCCDB6", "#E8DECD", "#FFFDF7", "#E3D6C2", "#6B4B3D", "#CDBF86", "#2C2420", "#E7A3A1", "#E9B5A8"),
        new("Dark", true, "#24211F", "#2D2A27", "#36322E", "#423D38", "#EDE6DA", "#A59C90",
            "#D3A27A", "#24211F", "#3F342B", "#7A6150", "#E8BD98",
            "#3A342F", "#93C29A", "#DC917F", "#2A352B", "#3D2B26", new[] { "#D9A273", "#EBA2AE", "#8FB8E3", "#7EC7B2", "#E8C463", "#B2A2E0", "#B5C878" },
            "#514A43", "#161412", "#46403A", "#5E564F", "#5E4236", "#C2B47C", "#BFAF98", "#D9948F", "#C98C80"),
    };

    public static List<Palette> For(string tone) => All;
    public static int Current;

    public static void Apply(Application app, string tone, int index)
    {
        var p = All[index];
        Current = index;
        var r = app.Resources;
        void C(string k, string hex) => r[k] = new SolidColorBrush(Color.Parse(hex));
        r["FontJua"] = new FontFamily(F + "Jua");
        r["FontHand"] = new FontFamily(F + "Gaegu");
        r["FontMono"] = new FontFamily(F + "Rec Mono Casual");
        app.RequestedThemeVariant = p.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        C("Bg", p.Bg); C("Surface", p.Surface); C("Surface2", p.Surface2); C("Line", p.Line); C("Text", p.Text); C("Muted", p.Muted);
        C("Accent", p.Accent); C("AccentText", p.AccentText); C("AccentSoft", p.AccentSoft); C("AccentLine", p.AccentLine); C("AccentInk", p.AccentInk);
        C("Sel", p.Dark ? "#3AD3A27A" : "#26A57A55"); C("Hover", p.Dark ? "#18D3A27A" : "#12A57A55"); C("AddFg", p.AddFg); C("DelFg", p.DelFg); C("AddBg", p.AddBg); C("DelBg", p.DelBg);
        C("Stitch", p.Stitch);
        // 원격 버튼 천 - 다크는 바탕과 묻혀서 한 톤 밝게, 라이트는 기존 천 색
        C("RemoteBg", p.Dark ? "#3A3531" : p.Surface); C("CardStitch", p.Dark ? "#5A524A" : "#CDBDA3");
        // 뜨개 패널 실 색 - 깃 로그(흰 양털) / Changes(오트밀)
        C("KnitBg", p.Dark ? p.Surface : "#FFFFFE"); C("KnitLine", p.Dark ? "#4F4842" : "#ECE7DF");
        C("Knit2Bg", p.Dark ? "#332F2B" : "#FFFCF6"); C("Knit2Line", p.Dark ? "#4E4741" : "#E0D2BC"); C("Wool", p.Wool); C("WoolLine", p.WoolLine);
        // 단추 실 - 그래프 단추는 밝은 실, 눈 단추는 단추와 반대 명도 (다크는 상아 단추)
        C("Thread", p.Dark ? "#F3ECE0" : "#FFFDF7"); C("EyeThread", p.Dark ? "#4A3C33" : "#FFFDF7");
        C("Muzzle", p.Muzzle); C("EyeRim", p.EyeRim); C("Pupil", p.Pupil); C("Nose", p.Nose); C("Blush", p.Blush);
        r["PatchShadow"] = BoxShadows.Parse($"0 3 0 0 {p.Shadow}");
        r["PatchShadowSm"] = BoxShadows.Parse($"0 2 0 0 {p.Shadow}");
        for (int i = 0; i < p.G.Length; i++)
        {
            var c = Color.Parse(p.G[i]);
            r["G" + i] = c;
            r["GB" + i] = new SolidColorBrush(c);
            r["GS" + i] = new SolidColorBrush(Color.FromArgb(p.Dark ? (byte)0x33 : (byte)0x26, c.R, c.G, c.B));
        }
        r["IsDark"] = p.Dark;
        r["GraphW"] = 2.4;
        r["LaneGap"] = 22.0;
        r["DotKind"] = "button";
        r["GraphDash"] = true;
        r["SystemAccentColor"] = Color.Parse(p.Accent);
    }
}
