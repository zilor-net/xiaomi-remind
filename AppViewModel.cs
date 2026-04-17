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

/// <summary>导航页面枚举</summary>
public enum AppPage { Home, Settings }

public partial class AppViewModel : ObservableObject
{
    private readonly MqttClientService _mqttService;
    private readonly IConfiguration _configuration;
    private readonly string _configPath;

    private ObservableCollection<MqttMessageRecord> Messages { get; } = new();

    [ObservableProperty] private string _statusText = "未连接";
    [ObservableProperty] private string _statusInfo = "";
    [ObservableProperty] private bool _isSubscribed;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isDarkMode;
    [ObservableProperty] private AppPage _selectedPage = AppPage.Home;

    #region 关闭进程设置

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

    private List<string> _closeProcessNames = new();
    public IReadOnlyList<string> CloseProcessNames => _closeProcessNames.AsReadOnly();

    /// <summary>进程列表文本（每行一个），用于 TextBox 双向绑定</summary>
    [ObservableProperty] private string _processListText = "";

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
            // ignored
        }
    }

    #endregion

    #region MQTT 配置属性（用于设置页编辑）

    [ObservableProperty] private string _mqttBroker = "";
    [ObservableProperty] private string _mqttPort = "";
    [ObservableProperty] private string _mqttClientId = "";
    [ObservableProperty] private string _mqttUserName = "";
    [ObservableProperty] private string _mqttPassword = "";
    [ObservableProperty] private string _mqttTopics = "";

    /// <summary>从配置文件读取设置</summary>
    private void LoadConfig()
    {
        MqttBroker = _configuration["Mqtt:Broker"] ?? "localhost";
        MqttPort = _configuration["Mqtt:Port"] ?? "1883";
        MqttClientId = _configuration["Mqtt:ClientId"] ?? "XiaomiRemind";
        MqttUserName = _configuration["Mqtt:UserName"] ?? "";
        MqttPassword = _configuration["Mqtt:Password"] ?? "";
        MqttTopics = _configuration["Mqtt:Topics"] ?? "";

        var procs = _configuration.GetSection("UI:CloseProcessesOnMessage:Processes")
            .GetChildren().Select(c => c.Value!).Where(v => !string.IsNullOrEmpty(v)).ToList();
        _closeProcessNames = procs;
        ProcessListText = string.Join("\n", _closeProcessNames);
    }

    /// <summary>将进程列表写回配置文件</summary>
    [RelayCommand]
    private void SaveProcessList()
    {
        try
        {
            var json = File.ReadAllText(_configPath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

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
            // ignored
        }
    }

    /// <summary>将 MQTT 连接信息写回配置文件</summary>
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
            if (root.TryGetProperty("UI", out var uiSection))
                dict["UI"] = uiSection.Clone();

            var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            File.WriteAllText(_configPath, JsonSerializer.Serialize(dict, options));
        }
        catch
        {
            // ignored
        }
    }

    #endregion

    #region 事件

    public event EventHandler<MqttMessageReceivedEventArgs>? MessageArrived;

    public AppViewModel(MqttClientService mqttService, IConfiguration configuration)
    {
        _mqttService = mqttService;
        _configuration = configuration;
        _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        LoadConfig();

        _closeProcessesOnMessage = bool.Parse(configuration["UI:CloseProcessesOnMessage:Enabled"] ?? "true");

        _mqttService.MessageReceived += OnMessageReceived;
        _mqttService.Disconnected += OnDisconnected;
        _mqttService.Reconnected += OnReconnected;
    }

    private const int MaxMessages = 100;

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
            await SubscribeAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"连接失败: {ex.Message}";
            IsConnected = false;
        }
    }

    [RelayCommand]
    private async Task SubscribeAsync()
    {
        try
        {
            var topicsStr = _configuration["Mqtt:Topics"];
            if (string.IsNullOrEmpty(topicsStr)) return;

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

    [RelayCommand]
    private void ClearMessages()
    {
        Messages.Clear();
    }

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

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkMode = !IsDarkMode;
    }

    #endregion
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool and false;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}
