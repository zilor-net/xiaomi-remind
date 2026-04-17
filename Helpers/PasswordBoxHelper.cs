using System.Windows;
using System.Windows.Controls;

namespace Xiaomi.Remind.Helpers;

/// <summary>PasswordBox 双向绑定辅助类</summary>
public static class PasswordBoxHelper
{
    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached("BoundPassword", typeof(string), typeof(PasswordBoxHelper),
            new FrameworkPropertyMetadata("", OnBoundPasswordChanged));

    public static string GetBoundPassword(DependencyObject d) => (string)d.GetValue(BoundPasswordProperty);
    public static void SetBoundPassword(DependencyObject d, string value) => d.SetValue(BoundPasswordProperty, value);

    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PasswordBox box && box.Password != (string)e.NewValue)
        {
            box.PasswordChanged -= OnPasswordChanged;
            box.Password = (string)e.NewValue;
            box.PasswordChanged += OnPasswordChanged;
        }
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box)
            SetBoundPassword(box, box.Password);
    }
}
