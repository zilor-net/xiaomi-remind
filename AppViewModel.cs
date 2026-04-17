using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using Xiaomi.Remind.Models;
using Xiaomi.Remind.Services;

namespace Xiaomi.Remind;

/// <summary>
/// 导航页面枚举。
/// 用于 MainWindow 中判断当前应显示哪个页面面板。
/// </summary>
public enum AppPage { Home, Settings }

/// <summary>
/// 应用程序核心 ViewModel。
///
/// 职责：
///   1. 管理 MQTT 连接的生命周期（连接 / 断开 / 订阅）
///   2. 接收 MQTT 消息并转发到 UI 层（消息列表 + 事件通知）
///   3. 管理应用状态（连接状态、主题模式、当前页面）
///   4. 处理配置文件的读取和写入（appsettings.json）
///   5. 管理"收到消息时自动关闭指定进程"功能
///
/// 架构说明：
///   - 继承 ObservableObject（CommunityToolkit.Mvvm），提供属性变更通知
///   - 使用 [ObservableProperty] 源生成器自动产生 PropertyChanged 事件
///   - 使用 [RelayCommand] 源生成器自动产生 ICommand 实现
///   - 通过事件（MessageArrived / Disconnected / Reconnected）与 App.xaml.cs 通信
/// </summary>
public partial class AppViewModel : ObservableObject
{
    private readonly MqttClientService _mqttService;
    private readonly IConfiguration _configuration;
    private readonly string _configPath;

    /// <summary>
    /// MQTT 消息记录集合。
    /// 使用 ObservableCollection 以支持 DataGrid 的自动绑定和实时更新。
    /// 新消息插入到列表头部（最新在前），超过 MaxMessages（100）条时自动淘汰旧消息。
    /// </summary>
    public ObservableCollection<MqttMessageRecord> Messages { get; } = new();

    /// <summary>状态栏连接状态文字，例如 "未连接" / "已连接" / "正在连接..."</summary>
    [ObservableProperty] private string _statusText = "未连接";

    /// <summary>状态栏详细信息，显示 Broker 地址和订阅主题</summary>
    [ObservableProperty] private string _statusInfo = "";

    /// <summary>是否已成功订阅主题（控制状态指示灯颜色）</summary>
    [ObservableProperty] private bool _isSubscribed;

    /// <summary>是否已连接到 MQTT Broker（控制连接/断开按钮的启用状态）</summary>
    [ObservableProperty] private bool _isConnected;

    /// <summary>当前是否为深色模式</summary>
    [ObservableProperty] private bool _isDarkMode;

    /// <summary>当前选中的导航页面</summary>
    [ObservableProperty] private AppPage _selectedPage = AppPage.Home;

    #region 关闭进程设置

    /// <summary>
    /// 是否在收到 MQTT 消息时自动关闭指定进程。
    /// 修改时自动保存到 appsettings.json。
    /// </summary>
    private bool _closeProcessesOnMessage;
    public bool CloseProcessesOnMessage
    {
        get => _closeProcessesOnMessage;
        set
        {
            _closeProcessesOnMessage = value;
            OnPropertyChanged();
            SaveCloseSetting(value);
        }
    }

    /// <summary>要关闭的进程名列表（不含 .exe 后缀）</summary>
    private List<string> _closeProcessNames = new();
    public IReadOnlyList<string> CloseProcessNames => _closeProcessNames.AsReadOnly();

    /// <summary>进程列表文本（每行一个进程名），用于 TextBox 双向绑定</summary>
    [ObservableProperty] private string _processListText = "";

    /// <summary>
    /// 保存"收到消息时关闭进程"的开关状态到配置文件。
    /// 仅更新 UI.CloseProcessesOnMessage.Enabled 字段，保留其他配置不变。
    /// </summary>
    private void SaveCloseSetting(bool value)
    {
        try
        {
            var json = File.ReadAllText(_configPath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var dict = new Dictionary<string, object>();
            if (root.TryGetProperty("Mqtt", out var mqtt))
                dict["Mqtt"] = mqtt.Clone();
            dict["UI"] = new Dictionary<string, object>
            {
                ["CloseProcessesOnMessage"] = new Dictionary<string, object>
                {
                    ["Enabled"] = value,
                    ["Processes"] = root.TryGetProperty("UI", out var uiSection) &&
                        uiSection.TryGetProperty("CloseProcessesOnMessage", out var cpm) &&
                        cpm.TryGetProperty("Processes", out var procs)
                        ? procs.Clone()
                        : new List<string>()
                }
            };

            var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            File.WriteAllText(_configPath, JsonSerializer.Serialize(dict, options));
        }
        catch
        {
            // 保存失败时静默忽略，避免影响主流程
        }
    }

    #endregion

    #region MQTT 配置属性（用于设置页编辑）

    /// <summary>MQTT Broker 服务器地址</summary>
    [ObservableProperty] private string _mqttBroker = "";

    /// <summary>MQTT Broker 端口号</summary>
    [ObservableProperty] private string _mqttPort = "";

    /// <summary>MQTT 客户端唯一标识</summary>
    [ObservableProperty] private string _mqttClientId = "";

    /// <summary>MQTT 认证用户名</summary>
    [ObservableProperty] private string _mqttUserName = "";

    /// <summary>MQTT 认证密码</summary>
    [ObservableProperty] private string _mqttPassword = "";

    /// <summary>要订阅的 MQTT 主题，多个主题以逗号分隔</summary>
    [ObservableProperty] private string _mqttTopics = "";

    /// <summary>
    /// 从配置文件（appsettings.json）读取所有设置到 ViewModel 属性。
    /// 使用 IConfiguration 的分层键访问（冒号分隔），例如 "Mqtt:Broker"。
    /// </summary>
    private void LoadConfig()
    {
        MqttBroker = _configuration["Mqtt:Broker"] ?? "localhost";
        MqttPort = _configuration["Mqtt:Port"] ?? "1883";
        MqttClientId = _configuration["Mqtt:ClientId"] ?? "XiaomiRemind";
        MqttUserName = _configuration["Mqtt:UserName"] ?? "";
        MqttPassword = _configuration["Mqtt:Password"] ?? "";
        MqttTopics = _configuration["Mqtt:Topics"] ?? "";

        // 读取进程列表：配置节是数组形式，GetChildren() 获取所有数组元素
        var procs = _configuration.GetSection("UI:CloseProcessesOnMessage:Processes")
            .GetChildren().Select(c => c.Value!).Where(v => !string.IsNullOrEmpty(v)).ToList();
        _closeProcessNames = procs;
        ProcessListText = string.Join("\n", _closeProcessNames);
    }

    /// <summary>
    /// 将进程列表（从设置页 TextBox）写回配置文件。
    /// 按换行分割、去空、去重后保存。
    /// </summary>
    [RelayCommand]
    private void SaveProcessList()
    {
        try
        {
            var json = File.ReadAllText(_configPath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // 将多行文本解析为进程名列表
            var lines = ProcessListText.Split('\n')
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l))
                .Distinct()
                .ToList();
            _closeProcessNames = lines;

            var dict = new Dictionary<string, object>();
            if (root.TryGetProperty("Mqtt", out var mqtt))
                dict["Mqtt"] = mqtt.Clone();
            dict["UI"] = new Dictionary<string, object>
            {
                ["CloseProcessesOnMessage"] = new Dictionary<string, object>
                {
                    // 保留现有的 Enabled 值
                    ["Enabled"] = !root.TryGetProperty("UI", out var ui) ||
                                  !ui.TryGetProperty("CloseProcessesOnMessage", out var cpm) ||
                                  !cpm.TryGetProperty("Enabled", out var enabled) || enabled.GetBoolean(),
                    ["Processes"] = lines
                }
            };

            var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            File.WriteAllText(_configPath, JsonSerializer.Serialize(dict, options));
        }
        catch
        {
            // 保存失败时静默忽略
        }
    }

    /// <summary>
    /// 将 MQTT 连接信息（Broker / 端口 / 客户端 ID / 用户名 / 密码 / 主题）写回配置文件。
    /// 保留现有的 UI 配置不变。
    /// </summary>
    [RelayCommand]
    private void SaveMqttConfig()
    {
        try
        {
            var json = File.ReadAllText(_configPath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var mqttDict = new Dictionary<string, object>
            {
                ["Broker"] = MqttBroker,
                ["ClientId"] = MqttClientId,
                ["UserName"] = MqttUserName,
                ["Password"] = MqttPassword,
                ["Topics"] = MqttTopics
            };
            if (int.TryParse(MqttPort, out var port)) mqttDict["Port"] = port;

            var dict = new Dictionary<string, object>
            {
                ["Mqtt"] = mqttDict
            };
            // 保留现有的 UI 配置
            if (root.TryGetProperty("UI", out var uiSection))
                dict["UI"] = uiSection.Clone();

            var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            File.WriteAllText(_configPath, JsonSerializer.Serialize(dict, options));
        }
        catch
        {
            // 保存失败时静默忽略
        }
    }

    #endregion

    #region 事件

    /// <summary>
    /// MQTT 消息到达事件。
    /// App.xaml.cs 订阅此事件以弹出系统通知和执行进程关闭操作。
    /// </summary>
    public event EventHandler<MqttMessageReceivedEventArgs>? MessageArrived;

    /// <summary>
    /// 构造函数。
    /// 加载配置、初始化事件订阅。
    /// 注意：构造函数中不发起连接，连接由 App.OnStartup 异步触发。
    /// </summary>
    public AppViewModel(MqttClientService mqttService, IConfiguration configuration)
    {
        _mqttService = mqttService;
        _configuration = configuration;
        _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        LoadConfig();

        _closeProcessesOnMessage = bool.Parse(configuration["UI:CloseProcessesOnMessage:Enabled"] ?? "true");

        // 订阅 MQTT 服务的三个核心事件
        _mqttService.MessageReceived += OnMessageReceived;   // 收到消息
        _mqttService.Disconnected += OnDisconnected;          // 连接断开
        _mqttService.Reconnected += OnReconnected;            // 重连成功
    }

    /// <summary>消息列表最大保留条数，超过此数量时淘汰最旧的记录</summary>
    private const int MaxMessages = 100;

    /// <summary>
    /// MQTT 消息到达回调。
    /// 在 UI 线程将新消息插入到列表头部，并淘汰超过 MaxMessages 的旧消息。
    /// 同时触发 MessageArrived 事件通知 App.xaml.cs 弹出系统通知。
    /// </summary>
    private void OnMessageReceived(object? sender, MqttMessageReceivedEventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            Messages.Insert(0, new MqttMessageRecord
            {
                Timestamp = DateTime.Now,
                Topic = e.Topic,
                Payload = e.Payload
            });
            while (Messages.Count > MaxMessages)
                Messages.RemoveAt(Messages.Count - 1);
        });
        MessageArrived?.Invoke(this, e);
    }

    /// <summary>
    /// MQTT 连接断开回调。
    /// 更新状态文字为"正在重连"，清除订阅状态和连接信息。
    /// </summary>
    private void OnDisconnected(object? sender, EventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            StatusText = "连接已断开，正在重连...";
            IsSubscribed = false;
            StatusInfo = "";
            IsConnected = false;
        });
    }

    /// <summary>
    /// MQTT 重连成功回调。
    /// 恢复状态文字和连接信息，标记为已订阅。
    /// </summary>
    private void OnReconnected(object? sender, EventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            IsSubscribed = true;
            StatusText = "已连接";
            StatusInfo = $"{MqttBroker}:{MqttPort}  |  {MqttTopics}";
            IsConnected = true;
        });
    }

    #endregion

    #region 命令

    /// <summary>
    /// 连接 MQTT Broker 命令。
    /// 从配置中读取连接参数，发起连接后自动订阅主题。
    /// 如果已经处于连接状态则跳过。
    /// </summary>
    [RelayCommand]
    private async Task ConnectAsync()
    {
        try
        {
            if (_mqttService.IsConnected)
            {
                StatusText = "已连接，无需重复连接";
                return;
            }

            StatusText = "正在连接...";
            var broker = _configuration["Mqtt:Broker"] ?? "localhost";
            if (!int.TryParse(_configuration["Mqtt:Port"], out var port) || port <= 0 || port > 65535)
            {
                StatusText = "连接失败: 端口号无效";
                return;
            }
            var clientId = _configuration["Mqtt:ClientId"] ?? "XiaomiRemind";
            var userName = _configuration["Mqtt:UserName"];
            var password = _configuration["Mqtt:Password"];

            await _mqttService.ConnectAsync(broker, port, clientId, userName, password);
            // 连接成功后立即订阅主题
            await SubscribeAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"连接失败: {ex.Message}";
            IsConnected = false;
        }
    }

    /// <summary>
    /// 订阅 MQTT 主题命令。
    /// 从配置中读取主题字符串（逗号分隔），按逗号分割后发起订阅。
    /// </summary>
    [RelayCommand]
    private async Task SubscribeAsync()
    {
        try
        {
            var topicsStr = _configuration["Mqtt:Topics"];
            if (string.IsNullOrEmpty(topicsStr)) return;

            // 按逗号分割主题，去除空白和前后空格
            var topics = topicsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            await _mqttService.SubscribeAsync(topics);

            IsSubscribed = true;
            StatusText = "已订阅";
            StatusInfo = $"{MqttBroker}:{MqttPort}  |  {MqttTopics}";
            IsConnected = true;
        }
        catch (Exception ex)
        {
            StatusText = $"订阅失败: {ex.Message}";
            IsConnected = false;
        }
    }

    /// <summary>
    /// 断开 MQTT 连接命令。
    /// 调用 MqttClientService.DisconnectAsync()，清除所有连接状态标记。
    /// </summary>
    [RelayCommand]
    private async Task DisconnectAsync()
    {
        try
        {
            if (_mqttService.IsConnected)
                await _mqttService.DisconnectAsync();

            IsSubscribed = false;
            StatusText = "已断开";
            StatusInfo = "";
            IsConnected = false;
        }
        catch (Exception ex)
        {
            StatusText = $"断开失败: {ex.Message}";
        }
    }

    /// <summary>清空消息列表</summary>
    [RelayCommand]
    private void ClearMessages()
    {
        Messages.Clear();
    }

    /// <summary>
    /// 页面导航命令。
    /// 参数为 "Home" 或 "Settings"，设置 SelectedPage 属性。
    /// MainWindow 监听到 SelectedPage 变化后执行实际的面板切换。
    /// </summary>
    [RelayCommand]
    private void Navigate(string page)
    {
        SelectedPage = page switch
        {
            "Home" => AppPage.Home,
            "Settings" => AppPage.Settings,
            _ => AppPage.Home
        };
    }

    /// <summary>
    /// 切换深色 / 浅色主题命令。
    /// MainWindow 监听到 IsDarkMode 变化后调用 App.ApplyTheme() 更新全局资源。
    /// </summary>
    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkMode = !IsDarkMode;
    }

    #endregion
}

/// <summary>
/// 布尔取反值转换器。
/// 用于 XAML 中按钮的 IsEnabled 反向绑定。
/// 例如：连接按钮在 IsConnected=false 时启用，即 IsEnabled="{Binding IsConnected, Converter={StaticResource InverseBoolConverter}}"。
/// 仅实现 Convert 方向（bool → bool），ConvertBack 未实现。
/// </summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool and false;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}
