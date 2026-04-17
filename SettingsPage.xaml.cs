namespace Xiaomi.Remind;

/// <summary>
/// 设置页用户控件。
/// 仅负责 InitializeComponent()，UI 内容完全在 SettingsPage.xaml 中定义。
/// DataContext 通过 MainWindow.xaml 中的 RelativeSource 绑定继承自 Window 的 DataContext（AppViewModel）。
/// </summary>
public partial class SettingsPage
{
    public SettingsPage()
    {
        InitializeComponent();
    }
}
