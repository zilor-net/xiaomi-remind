using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.Configuration;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Xiaomi.Remind.Services;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using MessageBoxButton = System.Windows.MessageBoxButton;

namespace Xiaomi.Remind;

/// <summary>
/// WPF 应用程序入口点。
///
/// 核心职责：
///   1. 单实例控制（Mutex）：确保同时只运行一个程序实例
///   2. 依赖注入：创建 IConfiguration、MqttClientService、AppViewModel、MainWindow
///   3. 系统托盘：创建 NotifyIcon 及其右键菜单（显示 / 开机启动 / 退出）
///   4. 主题管理：检测系统深色模式，动态切换亮色/暗色主题资源
///   5. 事件桥接：将 MQTT 消息事件转发为系统托盘通知和进程关闭
///
/// 启动流程：
///   OnStartup → 互斥锁检查 → 构建配置 → 创建服务 → 创建窗口 → 创建托盘 → 连接 MQTT
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 单实例互斥锁名称。
    /// 命名 Mutex 在全局命名空间中唯一标识，用于检测是否已有实例在运行。
    /// </summary>
    private const string MutexName = "Xiaomi.Remind.SingleInstance";
    private static Mutex? _mutex;

    private MqttClientService? _mqttService;
    private AppViewModel? _viewModel;
    private MainWindow? _mainWindow;
    private NotifyIcon? _notifyIcon;

    #region Win32 API 声明

    /// <summary>将指定窗口置于前台（激活窗口）</summary>
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// 控制窗口的显示状态。
    /// 此处用于将已存在的隐藏/最小化窗口恢复到前台。
    /// </summary>
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>SW_RESTORE = 9：将最小化窗口恢复并激活</summary>
    private const int SW_RESTORE = 9;

    #endregion

    /// <summary>
    /// 全局未捕获异常处理器。
    /// 捕获 WPF Dispatcher 线程上所有未处理的异常，弹出错误对话框避免程序崩溃。
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        System.Windows.MessageBox.Show($"程序发生未处理异常: {e.Exception.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    /// <summary>
    /// 检测 Windows 系统是否启用了深色模式。
    /// 读取注册表 HKCU\...\Themes\Personalize\AppsUseLightTheme。
    /// 值为 0 表示深色模式，值为 1 或不存在表示浅色模式。
    /// </summary>
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
    ///
    /// 原理：WPF 使用 DynamicResource 绑定的资源（如 {DynamicResource ThemeWindowBg}）
    /// 在运行时会被替换为实际的 SolidColorBrush。此方法替换 Resources 字典中的资源值，
    /// 所有使用 DynamicResource 引用这些资源的控件会自动刷新颜色。
    ///
    /// 注意：必须在窗口和控件创建完成后调用，否则控件尚未创建 DynamicResource 绑定，
    /// 替换的资源不会生效。App.xaml.cs 中在 MainWindow 创建后调用此方法。
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
            // ===== 深色主题色板 =====
            setBrush("ThemeWindowBg", Color.FromRgb(32, 32, 32));        // 窗口背景
            setBrush("ThemeSidebarBg", Color.FromRgb(28, 28, 30));       // 侧边栏背景
            setBrush("ThemeCardBg", Color.FromRgb(44, 44, 46));          // 卡片背景
            setBrush("ThemeBorderLight", Color.FromRgb(60, 60, 62));     // 浅色边框
            setBrush("ThemeBorderLighter", Color.FromRgb(55, 55, 58));   // 更浅的边框
            setBrush("ThemeTextPrimary", Color.FromRgb(240, 240, 240));  // 主要文字
            setBrush("ThemeTextSecondary", Color.FromRgb(180, 180, 180)); // 次要文字
            setBrush("ThemeTextTertiary", Color.FromRgb(130, 130, 130));  // 三级文字
            setBrush("ThemeTextDesc", Color.FromRgb(160, 160, 160));     // 描述文字
            setBrush("ThemeTextSubtle", Color.FromRgb(170, 170, 170));   // 柔和文字
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
        }
        else
        {
            // ===== 浅色主题色板 =====
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
        }
    }

    /// <summary>
    /// 应用程序启动入口。
    /// 执行单实例检查、服务初始化、窗口创建、托盘创建和 MQTT 连接。
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        // ===== 单实例控制 =====
        // 尝试获取命名互斥锁，createdNew=false 表示已有实例在运行
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // 激活已运行的实例窗口，然后退出当前进程
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
        // 显式关闭模式：只有调用 Shutdown() 时才会退出（关闭按钮被拦截为隐藏）
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // 检测系统主题
        var systemDark = IsWindowsDarkMode();

        try
        {
            // ===== 构建配置系统 =====
            // 从 appsettings.json 加载配置，reloadOnChange=true 表示文件变化时自动重新加载
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // ===== 创建核心服务 =====
            _mqttService = new MqttClientService();
            _viewModel = new AppViewModel(_mqttService, configuration);
            _viewModel.IsDarkMode = systemDark;

            // ===== 创建主窗口 =====
            _mainWindow = new MainWindow { DataContext = _viewModel };
            _mainWindow.StateChanged += MainWindow_StateChanged;

            // 在窗口创建后应用主题（确保控件已初始化，DynamicResource 绑定生效）
            ApplyTheme(systemDark);

            // ===== 创建系统托盘 =====
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

            // 双击托盘图标 → 显示主窗口
            _notifyIcon.MouseDoubleClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) ShowMainWindow();
            };

            // ===== 注册消息事件处理 =====
            _viewModel.MessageArrived += OnMessageArrived;

            // 后台发起 MQTT 连接（不阻塞 UI 线程）
            _ = ConnectAsync();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    /// <summary>
    /// 窗口状态变化处理。
    /// 当窗口最小化时自动隐藏到系统托盘，
    /// 实现"最小化 = 隐藏到托盘"的行为。
    /// </summary>
    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (_mainWindow?.WindowState == WindowState.Minimized)
            _mainWindow.Hide();
    }

    /// <summary>
    /// 显示主窗口。
    /// 从隐藏状态恢复窗口，将其激活到前台。
    /// </summary>
    private void ShowMainWindow()
    {
        _mainWindow?.Show();
        _mainWindow?.WindowState = WindowState.Normal;
        _mainWindow?.Activate();
    }

    /// <summary>
    /// MQTT 消息到达处理。
    /// 执行两项操作：
    ///   1. 如果启用了"收到消息时关闭进程"，在后台线程关闭指定进程的窗口
    ///   2. 弹出系统托盘气泡通知，显示主题和消息内容（截断到 100 字符）
    ///
    /// 注意：使用 Task.Run 将耗时操作移到后台线程，避免阻塞 UI 线程导致通知延迟。
    /// </summary>
    private void OnMessageArrived(object? sender, MqttMessageReceivedEventArgs e)
    {
        // 在后台线程执行耗时操作，避免阻塞 UI
        Task.Run(() =>
        {
            if (_viewModel?.CloseProcessesOnMessage == true)
                DesktopMinimizer.CloseWindowsByProcess(_viewModel.CloseProcessNames);
        });

        var payload = e.Payload.Length > 100 ? e.Payload[..100] + "..." : e.Payload;
        _notifyIcon?.ShowBalloonTip(5000, "新消息提醒", $"{e.Topic}\n{payload}", ToolTipIcon.Info);
    }

    /// <summary>
    /// 异步发起 MQTT 连接。
    /// 通过 ViewModel 的 ConnectCommand 执行，连接失败时弹出错误对话框。
    /// </summary>
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

    /// <summary>
    /// 退出应用程序。
    /// 清理托盘图标（防止残留），然后调用 WPF Shutdown。
    /// </summary>
    private void ShutdownApp()
    {
        _notifyIcon?.Visible = false;
        Shutdown();
    }

    /// <summary>
    /// 获取开机启动菜单的当前文本。
    /// 已启用时显示"取消开机启动"，未启用时显示"设置开机启动"。
    /// </summary>
    private static string GetStartupMenuText() => StartupManager.IsEnabled() ? "取消开机启动" : "设置开机启动";

    /// <summary>
    /// 切换开机启动状态。
    /// 调用 StartupManager 启用/禁用注册表自启动项，并更新菜单文本。
    /// </summary>
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

    /// <summary>
    /// 应用程序退出回调。
    /// 释放托盘图标、MQTT 客户端和互斥锁资源。
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        _notifyIcon?.Dispose();
        _mqttService?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
