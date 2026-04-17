using System.Windows;
using System.Windows.Controls;

namespace Xiaomi.Remind.Helpers;

/// <summary>
/// PasswordBox 双向绑定辅助类。
///
/// 问题背景：
///   WPF 的 PasswordBox 出于安全考虑，没有将 Password 属性设计为依赖属性，
///   因此不能直接使用 {Binding} 进行双向绑定。
///
/// 解决方案：
///   使用 Attached Property（附加属性）作为中介：
///   1. 定义附加属性 BoundPassword
///   2. 当 BoundPassword 变化时 → 同步到 PasswordBox.Password
///   3. 当 PasswordBox.Password 变化时（用户输入） → 同步回 BoundPassword
///
/// 使用方式（XAML）：
///   <PasswordBox helpers:PasswordBoxHelper.BoundPassword="{Binding MqttPassword}" />
///
/// 注意：PasswordBox.Password 本身不参与数据绑定链，
/// 而是通过附加属性的 PropertyChangedCallback 和 PasswordChanged 事件实现双向同步。
/// </summary>
public static class PasswordBoxHelper
{
    /// <summary>
    /// 附加属性定义。
    /// 注册了一个名为 "BoundPassword" 的附加属性，类型为 string，所有者为 PasswordBoxHelper。
    /// 附带 PropertyChangedCallback（OnBoundPasswordChanged），当属性值变化时触发。
    /// </summary>
    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached("BoundPassword", typeof(string), typeof(PasswordBoxHelper),
            new FrameworkPropertyMetadata("", OnBoundPasswordChanged));

    /// <summary>获取附加属性的值（用于 XAML 绑定读取）</summary>
    public static string GetBoundPassword(DependencyObject d) => (string)d.GetValue(BoundPasswordProperty);

    /// <summary>设置附加属性的值（用于 XAML 绑定写入）</summary>
    public static void SetBoundPassword(DependencyObject d, string value) => d.SetValue(BoundPasswordProperty, value);

    /// <summary>
    /// 附加属性值变化回调。
    /// 当 ViewModel 中的 MqttPassword 属性变化时，此方法被调用，
    /// 将新值同步到 PasswordBox.Password。
    ///
    /// 注意：先取消 PasswordChanged 事件再设置 Password，
    /// 防止设置 Password 时触发事件导致循环回调。
    /// </summary>
    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PasswordBox box && box.Password != (string)e.NewValue)
        {
            // 先取消事件订阅，避免下面设置 Password 时触发 OnPasswordChanged
            box.PasswordChanged -= OnPasswordChanged;
            box.Password = (string)e.NewValue;
            box.PasswordChanged += OnPasswordChanged;
        }
    }

    /// <summary>
    /// 用户输入密码时的回调。
    /// 当用户在 PasswordBox 中键入内容时，PasswordChanged 事件触发，
    /// 将 PasswordBox.Password 的值写回到附加属性，从而更新 ViewModel 中的 MqttPassword。
    /// </summary>
    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box)
            SetBoundPassword(box, box.Password);
    }
}
