using System.ComponentModel;
using System.Windows;
using Application = System.Windows.Application;

namespace Xiaomi.Remind;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DataContext is AppViewModel vm)
            vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        NavigateToHome();
    }

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

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private void NavigateToHome()
    {
        HomePagePanel.Visibility = Visibility.Visible;
        SettingsPagePanel.Visibility = Visibility.Collapsed;
        NavHomeBtn.Style = FindResource("NavButtonActive") as Style;
        NavSettingsBtn.Style = FindResource("NavButton") as Style;
    }

    private void NavigateToSettings()
    {
        HomePagePanel.Visibility = Visibility.Collapsed;
        SettingsPagePanel.Visibility = Visibility.Visible;
        NavSettingsBtn.Style = FindResource("NavButtonActive") as Style;
        NavHomeBtn.Style = FindResource("NavButton") as Style;
    }
}
