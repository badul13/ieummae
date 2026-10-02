using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Ieummae.Core.Diff;

namespace Ieummae.App.Views;

// 코드 한 줄 - 바뀐 단어 구간만 진한 배경, 탭은 공백 4칸
public sealed class CodeLine : TextBlock
{
    public static readonly StyledProperty<DiffLine?> LineProperty = AvaloniaProperty.Register<CodeLine, DiffLine?>(nameof(Line));
    public DiffLine? Line { get => GetValue(LineProperty); set => SetValue(LineProperty, value); }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    // 코드 글꼴 합자 끔 - => 가 ⇒ 로 붙으면 실제 글자와 달라 보임
    static readonly FontFeatureCollection NoLigatures = [FontFeature.Parse("-liga"), FontFeature.Parse("-calt")];

    public CodeLine() => FontFeatures = NoLigatures;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LineProperty) Build();
    }

    void Build()
    {
        Inlines?.Clear();
        if (Line is not { } l) { Text = ""; return; }
        if (l.Changes.Count == 0) { Text = Expand(l.Text); return; }

        Inlines ??= [];
        var hi = l.Kind == LineKind.Add ? "AddHi" : "DelHi";
        int pos = 0;
        foreach (var (start, len) in l.Changes)
        {
            if (start > pos) Inlines.Add(new Run(Expand(l.Text[pos..start])));
            var run = new Run(Expand(l.Text.Substring(start, len)));
            // 테마 전환 따라가게 자원 연결
            run.Bind(TextElement.BackgroundProperty, this.GetResourceObservable(hi));
            Inlines.Add(run);
            pos = start + len;
        }
        if (pos < l.Text.Length) Inlines.Add(new Run(Expand(l.Text[pos..])));
    }

    static string Expand(string s) => s.Replace("\t", "    ");
}
