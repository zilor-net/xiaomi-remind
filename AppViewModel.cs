using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
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

    public ObservableCollection<MqttMessageRecord> Messages { get; } = new();

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
            var newValue = value ? "true" : "false";
            var pattern = @"(""\s*Enabled\s*""\s*:\s*)(true|false)";
            var newJson = System.Text.RegularExpressions.Regex.Replace(json, pattern, $"$1{newValue}");
            if (newJson != json)
                File.WriteAllText(_configPath, newJson);
        }
        catch { }
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
        MqttPort = _configuration["Mqtt:Port"]?.ToString() ?? "1883";
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
            var lines = ProcessListText.Split('\n')
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l))
                .Distinct()
                .ToList();

            _closeProcessNames = lines;

            var procJson = JsonSerializer.Serialize(lines);
            var pattern = @"(\s*""Processes""\s*:\s*)\[.*?\]";
            var newJson = System.Text.RegularExpressions.Regex.Replace(json, pattern, $"$1{procJson}",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            if (newJson != json)
                File.WriteAllText(_configPath, newJson);
        }
        catch { }
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
            var obj = new Dictionary<string, object>();

            // 保留原有 Mqtt 结构
            if (root.TryGetProperty("Mqtt", out var mqttSection))
            {
                foreach (var prop in mqttSection.EnumerateObject())
                    obj[prop.Name] = prop.Value.Clone();
            }

            obj["Broker"] = MqttBroker;
            if (int.TryParse(MqttPort, out var port)) obj["Port"] = port;
            obj["ClientId"] = MqttClientId;
            obj["UserName"] = MqttUserName;
            obj["Password"] = MqttPassword;
            obj["Topics"] = MqttTopics;

            // 保留 UI 部分
            var uiJson = "";
            if (root.TryGetProperty("UI", out var uiSection))
                uiJson = uiSection.GetRawText();

            var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            var newDoc = new Dictionary<string, object>
            {
                ["Mqtt"] = obj,
            };
            // 手动构建 JSON 以保留 UI 部分
            var mqttJson = JsonSerializer.Serialize(obj, options);
            var finalJson = $"{{\n  \"Mqtt\": {mqttJson},\n  \"UI\": {uiJson}\n}}";
            File.WriteAllText(_configPath, finalJson);
        }
        catch { }
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

    #endregion

    #region 命令

    [RelayCommand]
    public async Task ConnectAsync()
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
            var port = int.Parse(_configuration["Mqtt:Port"] ?? "1883");
            var clientId = _configuration["Mqtt:ClientId"] ?? "XiaomiRemind";
            var userName = _configuration["Mqtt:UserName"];
            var password = _configuration["Mqtt:Password"];

            await _mqttService.ConnectAsync(broker, port, clientId, userName, password);
            await SubscribeAsync();
            StatusText = "已连接";
            IsConnected = true;
        }
        catch (Exception ex)
        {
            StatusText = $"连接失败: {ex.Message}";
            IsConnected = false;
        }
    }

    [RelayCommand]
    public async Task SubscribeAsync()
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
    public async Task UnsubscribeAsync()
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
    public void ClearMessages()
    {
        Messages.Clear();
    }

    [RelayCommand]
    public void Navigate(string page)
    {
        SelectedPage = page switch
        {
            "Home" => AppPage.Home,
            "Settings" => AppPage.Settings,
            _ => AppPage.Home
        };
    }

    [RelayCommand]
    public void ToggleTheme()
    {
        IsDarkMode = !IsDarkMode;
    }

    #endregion
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is bool b && !b;
}
