using System.ComponentModel;
using System.Windows;
using Application = System.Windows.Application;

namespace Xiaomi.Remind;

/// <summary>
/// 主窗口类。
/// 负责窗口初始化、页面导航切换、主题切换响应、以及最小化到系统托盘的行为。
///
/// 页面导航机制：
///   主页（Home）和设置页（Settings）通过两个面板（HomePagePanel / SettingsPagePanel）
///   的 Visibility 属性进行切换，两个面板重叠在同一个 Grid 单元格中。
///   导航触发方式是：ViewModel 的 SelectedPage 属性变化 → PropertyChanged 事件
///   → MainWindow 监听到变化 → 调用 NavigateToHome() 或 NavigateToSettings()。
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // DataContext 赋值后（由 App.xaml.cs 设置）绑定 ViewModel，监听属性变化以驱动导航和主题
        DataContextChanged += OnDataContextChanged;
        // Loaded 时默认显示主页
        Loaded += OnLoaded;
    }

    /// <summary>
    /// DataContext 变更回调。
    /// 当窗口被赋予 ViewModel 时，订阅其 PropertyChanged 事件，
    /// 用于响应 SelectedPage 和 IsDarkMode 属性的变化。
    /// </summary>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DataContext is AppViewModel vm)
            vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>
    /// 窗口加载完成回调。默认导航到主页。
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        NavigateToHome();
    }

    /// <summary>
    /// ViewModel 属性变更回调。
    /// - SelectedPage 变化 → 切换到对应的页面（主页 / 设置页）
    /// - IsDarkMode 变化 → 调用 App.ApplyTheme() 更新全局主题色资源
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppViewModel.SelectedPage))
        {
            if (DataContext is AppViewModel vm)
            {
                if (vm.SelectedPage == AppPage.Home)
                    NavigateToHome();
                else
                    NavigateToSettings();
            }
        }
        else if (e.PropertyName == nameof(AppViewModel.IsDarkMode) && DataContext is AppViewModel vm)
        {
            ((App)Application.Current).ApplyTheme(vm.IsDarkMode);
        }
    }

    /// <summary>
    /// 窗口关闭事件处理。
    /// 取消默认关闭行为，改为隐藏窗口（而非销毁），
    /// 配合系统托盘实现"关闭窗口 = 最小化到托盘"的效果。
    /// 真正的退出由托盘菜单中的"退出"按钮调用 App.ShutdownApp() 触发。
    /// </summary>
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    /// <summary>
    /// 导航到主页。
    /// 显示 HomePagePanel，隐藏 SettingsPagePanel，
    /// 同时更新左侧导航按钮的高亮状态。
    /// </summary>
    private void NavigateToHome()
    {
        HomePagePanel.Visibility = Visibility.Visible;
        SettingsPagePanel.Visibility = Visibility.Collapsed;
        NavHomeBtn.Style = FindResource("NavButtonActive") as Style;
        NavSettingsBtn.Style = FindResource("NavButton") as Style;
    }

    /// <summary>
    /// 导航到设置页。
    /// 显示 SettingsPagePanel，隐藏 HomePagePanel，
    /// 同时更新左侧导航按钮的高亮状态。
    /// </summary>
    private void NavigateToSettings()
    {
        HomePagePanel.Visibility = Visibility.Collapsed;
        SettingsPagePanel.Visibility = Visibility.Visible;
        NavSettingsBtn.Style = FindResource("NavButtonActive") as Style;
        NavHomeBtn.Style = FindResource("NavButton") as Style;
    }
}
