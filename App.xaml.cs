using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using Microsoft.Extensions.Configuration;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Xiaomi.Remind.Services;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using MessageBoxButton = System.Windows.MessageBoxButton;

namespace Xiaomi.Remind;

public partial class App : Application
{
    private const string MutexName = "Xiaomi.Remind.SingleInstance";
    private static Mutex? _mutex;

    private MqttClientService? _mqttService;
    private AppViewModel? _viewModel;
    private MainWindow? _mainWindow;
    private NotifyIcon? _notifyIcon;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        System.Windows.MessageBox.Show($"程序发生未处理异常: {e.Exception.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static bool IsWindowsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int v && v == 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// 更新所有主题色资源。
    /// </summary>
    public void ApplyTheme(bool isDark)
    {
        var resources = Current.Resources;

        var setBrush = (string key, Color color) =>
        {
            resources[key] = new SolidColorBrush(color);
        };

        if (isDark)
        {
            setBrush("ThemeWindowBg", Color.FromRgb(32, 32, 32));
            setBrush("ThemeSidebarBg", Color.FromRgb(28, 28, 30));
            setBrush("ThemeCardBg", Color.FromRgb(44, 44, 46));
            setBrush("ThemeBorderLight", Color.FromRgb(60, 60, 62));
            setBrush("ThemeBorderLighter", Color.FromRgb(55, 55, 58));
            setBrush("ThemeTextPrimary", Color.FromRgb(240, 240, 240));
            setBrush("ThemeTextSecondary", Color.FromRgb(180, 180, 180));
            setBrush("ThemeTextTertiary", Color.FromRgb(130, 130, 130));
            setBrush("ThemeTextDesc", Color.FromRgb(160, 160, 160));
            setBrush("ThemeTextSubtle", Color.FromRgb(170, 170, 170));
            setBrush("ThemeDataGridHeaderBorder", Color.FromRgb(55, 55, 58));
            setBrush("ThemeDataGridAltRow", Color.FromRgb(38, 38, 40));
            setBrush("ThemeDataGridCellFg", Color.FromRgb(220, 220, 220));
            setBrush("ThemeDataGridHeaderFg", Color.FromRgb(210, 210, 210));
            setBrush("ThemeDataGridSelBg", Color.FromRgb(50, 60, 80));
            setBrush("ThemeNavHover", Color.FromRgb(50, 50, 52));
            setBrush("ThemeNavText", Color.FromRgb(190, 190, 190));
            setBrush("ThemeNavActiveBg", Color.FromRgb(40, 55, 80));
            setBrush("ThemeNavActiveFg", Color.FromRgb(100, 180, 255));
            setBrush("ThemeStatusBarBg", Color.FromRgb(36, 36, 38));
            setBrush("ThemeInputBg", Color.FromRgb(50, 50, 52));
            setBrush("ThemeInputBorder", Color.FromRgb(70, 70, 72));
            setBrush("ThemeInputFg", Color.FromRgb(220, 220, 220));
            setBrush("ThemeToggleThemeIcon", Color.FromRgb(180, 180, 180));
            setBrush("ThemeBtnBg", Color.FromRgb(60, 60, 62));
            setBrush("ThemeBtnHover", Color.FromRgb(70, 70, 72));
            setBrush("ThemeBtnPressed", Color.FromRgb(50, 50, 52));
            setBrush("ThemeBtnDisabledBg", Color.FromRgb(45, 45, 45));
            setBrush("ThemeBtnDisabledFg", Color.FromRgb(100, 100, 100));
            setBrush("ThemeBtnDisabledBorder", Color.FromRgb(55, 55, 58));

            // 同步 WPF-UI 控件主题
            ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.None);
        }
        else
        {
            setBrush("ThemeWindowBg", Color.FromRgb(250, 250, 250));
            setBrush("ThemeSidebarBg", Color.FromRgb(247, 247, 247));
            setBrush("ThemeCardBg", Color.FromRgb(255, 255, 255));
            setBrush("ThemeBorderLight", Color.FromRgb(232, 232, 232));
            setBrush("ThemeBorderLighter", Color.FromRgb(240, 240, 240));
            setBrush("ThemeTextPrimary", Color.FromRgb(32, 32, 32));
            setBrush("ThemeTextSecondary", Color.FromRgb(102, 102, 102));
            setBrush("ThemeTextTertiary", Color.FromRgb(153, 153, 153));
            setBrush("ThemeTextDesc", Color.FromRgb(136, 136, 136));
            setBrush("ThemeTextSubtle", Color.FromRgb(85, 85, 85));
            setBrush("ThemeDataGridHeaderBorder", Color.FromRgb(224, 224, 224));
            setBrush("ThemeDataGridAltRow", Color.FromRgb(250, 250, 250));
            setBrush("ThemeDataGridCellFg", Color.FromRgb(51, 51, 51));
            setBrush("ThemeDataGridHeaderFg", Color.FromRgb(51, 51, 51));
            setBrush("ThemeDataGridSelBg", Color.FromRgb(229, 241, 251));
            setBrush("ThemeNavHover", Color.FromRgb(234, 234, 234));
            setBrush("ThemeNavText", Color.FromRgb(85, 85, 85));
            setBrush("ThemeNavActiveBg", Color.FromRgb(232, 240, 254));
            setBrush("ThemeNavActiveFg", Color.FromRgb(0, 120, 212));
            setBrush("ThemeStatusBarBg", Color.FromRgb(255, 255, 255));
            setBrush("ThemeInputBg", Color.FromRgb(255, 255, 255));
            setBrush("ThemeInputBorder", Color.FromRgb(208, 208, 208));
            setBrush("ThemeInputFg", Color.FromRgb(51, 51, 51));
            setBrush("ThemeToggleThemeIcon", Color.FromRgb(85, 85, 85));
            setBrush("ThemeBtnBg", Color.FromRgb(249, 249, 249));
            setBrush("ThemeBtnHover", Color.FromRgb(240, 240, 240));
            setBrush("ThemeBtnPressed", Color.FromRgb(229, 229, 229));
            setBrush("ThemeBtnDisabledBg", Color.FromRgb(243, 243, 243));
            setBrush("ThemeBtnDisabledFg", Color.FromRgb(160, 160, 160));
            setBrush("ThemeBtnDisabledBorder", Color.FromRgb(224, 224, 224));

            ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.None);
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            var currentProcess = Process.GetCurrentProcess();
            var existingProcess = Process.GetProcessesByName(currentProcess.ProcessName)
                .FirstOrDefault(p => p.Id != currentProcess.Id);

            if (existingProcess != null)
            {
                var handle = existingProcess.MainWindowHandle;
                if (handle != IntPtr.Zero)
                {
                    ShowWindow(handle, SW_RESTORE);
                    SetForegroundWindow(handle);
                }
            }

            Environment.Exit(0);
            return;
        }

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // 检测系统主题
        var systemDark = IsWindowsDarkMode();

        try
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            _mqttService = new MqttClientService();
            _viewModel = new AppViewModel(_mqttService, configuration);
            _viewModel.IsDarkMode = systemDark;

            _mainWindow = new MainWindow { DataContext = _viewModel };
            _mainWindow.StateChanged += MainWindow_StateChanged;

            // 在窗口创建后应用主题（确保控件已初始化）
            ApplyTheme(systemDark);

            // 系统托盘
            var contextMenu = new ContextMenuStrip();
            var showItem = contextMenu.Items.Add("显示主窗口");
            showItem.Click += (s, e) => ShowMainWindow();

            var startupItem = contextMenu.Items.Add(GetStartupMenuText());
            startupItem.Click += (s, e) => ToggleStartup(startupItem);

            var exitItem = contextMenu.Items.Add("退出");
            exitItem.Click += (s, e) => ShutdownApp();

            _notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Information,
                Text = "Xiaomi Remind - MQTT 消息提醒",
                Visible = true,
                ContextMenuStrip = contextMenu
            };

            _notifyIcon.MouseDoubleClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) ShowMainWindow();
            };

            _viewModel.MessageArrived += OnMessageArrived;
            _ = ConnectAsync();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (_mainWindow?.WindowState == WindowState.Minimized)
            _mainWindow.Hide();
    }

    private void ShowMainWindow()
    {
        _mainWindow?.Show();
        _mainWindow?.WindowState = WindowState.Normal;
        _mainWindow?.Activate();
    }

    private void OnMessageArrived(object? sender, MqttMessageReceivedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_viewModel?.CloseProcessesOnMessage == true)
                DesktopMinimizer.CloseWindowsByProcess(_viewModel.CloseProcessNames);

            var payload = e.Payload.Length > 100 ? e.Payload[..100] + "..." : e.Payload;
            _notifyIcon?.ShowBalloonTip(5000, "新消息提醒", $"{e.Topic}\n{payload}", ToolTipIcon.Info);
        });
    }

    private async Task ConnectAsync()
    {
        try
        {
            if (_viewModel != null)
                await _viewModel.ConnectCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"连接失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShutdownApp()
    {
        _notifyIcon?.Visible = false;
        Shutdown();
    }

    private static string GetStartupMenuText() => StartupManager.IsEnabled() ? "取消开机启动" : "设置开机启动";

    private static void ToggleStartup(ToolStripItem menuItem)
    {
        try
        {
            if (StartupManager.IsEnabled())
            {
                StartupManager.DisableStartup();
                menuItem.Text = "设置开机启动";
            }
            else
            {
                StartupManager.EnableStartup();
                menuItem.Text = "取消开机启动";
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"操作失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notifyIcon?.Dispose();
        _mqttService?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
