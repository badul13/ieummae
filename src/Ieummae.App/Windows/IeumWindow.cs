using Avalonia.Controls;

namespace Ieummae.App.Windows;

// 창 공통 - 제목 표시줄을 내용 영역으로 확장 (양털 머리가 맨 위까지), 기본 창 스타일 사용
public class IeumWindow : Window
{
    protected override Type StyleKeyOverride => typeof(Window);

    public IeumWindow()
    {
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = 40;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Bind(BackgroundProperty, this.GetResourceObservable("Bg"));
        Bind(FontFamilyProperty, this.GetResourceObservable("FontJua"));
        FontSize = 14.5;
    }
}
