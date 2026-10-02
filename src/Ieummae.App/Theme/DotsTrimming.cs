using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Ieummae.App.Theme;

// 줄임표 - Jua 에 … 글자가 없어 □ 로 보임, 마침표 셋으로 대체
public sealed class DotsTrimming : TextTrimming
{
    public static readonly DotsTrimming End = new(false);
    public static readonly DotsTrimming Start = new(true);
    readonly bool _leading;
    DotsTrimming(bool leading) => _leading = leading;

    public override TextCollapsingProperties CreateCollapsingProperties(TextCollapsingCreateInfo info) => _leading
        ? new TextLeadingPrefixCharacterEllipsis("...", 0, info.Width, info.TextRunProperties, info.FlowDirection)
        : new TextTrailingCharacterEllipsis("...", info.Width, info.TextRunProperties, info.FlowDirection);
}
