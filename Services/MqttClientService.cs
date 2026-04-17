using MQTTnet;

namespace Xiaomi.Remind.Services;

/// <summary>
/// MQTT 客户端服务。
/// 封装 MQTTnet 库，提供连接、订阅、断开和消息接收功能。
///
/// MQTT（Message Queuing Telemetry Transport）是一种轻量级的消息发布/订阅协议，
/// 常用于物联网（IoT）场景。本程序通过 MQTT 连接到 Broker（消息服务器），
/// 订阅特定主题后接收其他设备/程序发布的消息。
/// </summary>
public class MqttClientService : IDisposable
{
    /// <summary>
    /// MQTTnet 库的客户端实例。
    /// IMqttClient 是接口类型，由 MqttClientFactory.CreateMqttClient() 创建。
    /// </summary>
    private readonly IMqttClient _mqttClient;

    /// <summary>
    /// 已订阅的主题列表，用于记录当前订阅状态。
    /// </summary>
    private readonly List<string> _topics = new();
    private readonly object _topicsLock = new();

    /// <summary>
    /// 连接选项，用于断线重连时复用。
    /// </summary>
    private MqttClientOptions? _connectOptions;

    /// <summary>
    /// 是否正在尝试重连，防止重复启动重连任务。
    /// </summary>
    private int _reconnecting;

    /// <summary>
    /// 是否已连接到 MQTT Broker。
    /// 直接代理 _mqttClient.IsConnected 属性。
    /// </summary>
    public bool IsConnected => _mqttClient.IsConnected;

    /// <summary>
    /// 构造函数：创建 MQTT 客户端实例。
    /// MqttClientFactory 是 MQTTnet 提供的工厂类，用于创建客户端和连接选项。
    /// </summary>
    public MqttClientService()
    {
        var factory = new MqttClientFactory();
        _mqttClient = factory.CreateMqttClient();
    }

    /// <summary>
    /// 消息接收事件。
    /// 当收到订阅主题的 MQTT 消息时触发，携带 MqttMessageReceivedEventArgs（包含主题和消息体）。
    /// ViewModel 订阅此事件并将消息插入到 UI 列表中。
    /// </summary>
    public event EventHandler<MqttMessageReceivedEventArgs>? MessageReceived;

    /// <summary>
    /// 连接断开事件。
    /// 当 MQTT 连接意外断开时触发（包括网络中断、Broker 宕机等）。
    /// </summary>
    public event EventHandler? Disconnected;

    /// <summary>
    /// 重连成功事件。
    /// 当自动重连成功时触发，通知 UI 层更新连接状态。
    /// </summary>
    public event EventHandler? Reconnected;

    private CancellationTokenSource? _reconnectCts;

    /// <summary>
    /// 连接到 MQTT Broker。
    ///
    /// 参数说明：
    ///   broker   : Broker 服务器地址，例如 "192.168.2.5"
    ///   port     : Broker 端口号，默认 1883（非加密）或 8883（TLS 加密）
    ///   clientId : 客户端唯一标识，Broker 用它区分不同连接
    ///   userName : 认证用户名（可选，null 表示无需认证）
    ///   password : 认证密码（可选）
    ///
    /// 连接配置：
    ///   - WithTcpServer：使用 TCP 连接到指定地址和端口
    ///   - WithClientId：设置客户端 ID
    ///   - WithCleanSession：清理会话，不保留上次连接的订阅和消息
    ///   - WithKeepAlivePeriod：心跳间隔 60 秒，用于检测连接是否断开
    ///   - WithCredentials：如果提供了用户名，则附加认证信息
    /// </summary>
    public async Task ConnectAsync(string broker, int port, string clientId, string? userName = null, string? password = null)
    {
        // 构建连接选项
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(broker, port)              // TCP 服务器地址
            .WithClientId(clientId)                   // 客户端 ID
            .WithCleanSession()                       // 清理会话
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(60));  // 心跳间隔

        // 如果配置了用户名，则附加认证信息
        if (!string.IsNullOrEmpty(userName))
        {
            options.WithCredentials(userName, password);
        }

        // 保存连接选项供重连时使用
        _connectOptions = options.Build();

        // 注册消息接收回调（异步方法，收到消息时自动调用）
        _mqttClient.ApplicationMessageReceivedAsync -= OnMessageReceivedAsync;
        _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;

        // 注册连接断开回调，用于检测 Broker 宕机/网络中断
        _mqttClient.DisconnectedAsync -= OnDisconnectedAsync;
        _mqttClient.DisconnectedAsync += OnDisconnectedAsync;

        // 发起异步连接
        await _mqttClient.ConnectAsync(_connectOptions, CancellationToken.None);
    }

    /// <summary>
    /// 连接断开回调。
    /// 当 MQTT 连接意外丢失时自动触发，启动后台重连任务。
    /// </summary>
    private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        // 触发断开事件，通知 UI 层更新状态
        Disconnected?.Invoke(this, EventArgs.Empty);

        // 取消之前的重连任务
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _reconnectCts = new CancellationTokenSource();

        // 防止并发重连
        if (Interlocked.Exchange(ref _reconnecting, 1) != 0)
            return;

        try
        {
            await ReconnectAsync(_reconnectCts.Token);
        }
        catch (OperationCanceledException)
        {
            // 取消是正常行为，忽略
        }
        finally
        {
            Interlocked.Exchange(ref _reconnecting, 0);
        }
    }

    /// <summary>
    /// 自动重连逻辑。
    /// 使用指数退避策略（2s → 4s → 8s → 16s → 30s），最多重试到 30 秒间隔。
    /// 重连成功后自动重新订阅之前的主题。
    /// </summary>
    private async Task ReconnectAsync(CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(2);
        var maxDelay = TimeSpan.FromSeconds(30);
        var attempt = 0;

        while (!_mqttClient.IsConnected)
        {
            attempt++;
            await Task.Delay(delay, ct);

            try
            {
                if (_connectOptions != null)
                    await _mqttClient.ConnectAsync(_connectOptions, ct);

                // 重连成功后重新订阅
                if (_mqttClient.IsConnected)
                {
                    List<string> topicsSnapshot;
                    lock (_topicsLock)
                    {
                        topicsSnapshot = new List<string>(_topics);
                    }
                    if (topicsSnapshot.Count > 0)
                        await SubscribeAsync(topicsSnapshot);

                    Reconnected?.Invoke(this, EventArgs.Empty);
                }

                return; // 重连成功，退出循环
            }
            catch
            {
                // 重连失败，指数退避
                delay = TimeSpan.FromMilliseconds(
                    Math.Min(delay.TotalMilliseconds * 2, maxDelay.TotalMilliseconds));
            }
        }
    }

    /// <summary>
    /// 订阅一个或多个 MQTT 主题。
    ///
    /// MQTT 主题支持通配符：
    ///   - "+"  匹配单层，例如 "home/+/temperature" 匹配 "home/living/temperature"
    ///   - "#"  匹配多层，例如 "home/#" 匹配所有以 "home/" 开头的主题
    ///
    /// 参数 topics：要订阅的主题列表
    /// </summary>
    public async Task SubscribeAsync(IEnumerable<string> topics)
    {
        lock (_topicsLock)
        {
            _topics.Clear();
            _topics.AddRange(topics);
        }

        // 构建订阅选项
        var subscribeOptions = new MqttClientSubscribeOptionsBuilder();
        foreach (var topic in topics)
        {
            subscribeOptions.WithTopicFilter(topic);
        }

        // 发起异步订阅
        await _mqttClient.SubscribeAsync(subscribeOptions.Build(), CancellationToken.None);
    }

    /// <summary>
    /// 断开 MQTT 连接。
    /// 仅在已连接状态下执行断开操作。
    /// 手动断开时不触发重连（通过清空连接选项标记）。
    /// </summary>
    public async Task DisconnectAsync()
    {
        // 取消自动重连：清空连接选项，重连循环检测到 _connectOptions == null 时不会重连
        _connectOptions = null;

        // 取消消息和断开回调的注册，避免手动断开触发回调
        _mqttClient.ApplicationMessageReceivedAsync -= OnMessageReceivedAsync;
        _mqttClient.DisconnectedAsync -= OnDisconnectedAsync;

        if (_mqttClient.IsConnected)
        {
            await _mqttClient.DisconnectAsync(new MqttClientDisconnectOptions());
        }
    }

    /// <summary>
    /// 消息接收回调函数。
    /// 当 MQTTnet 收到订阅主题的消息时自动调用。
    ///
    /// 执行流程：
    ///   1. 将消息 payload（二进制数据）转为字符串
    ///   2. 触发 MessageReceived 事件，将消息传递给订阅者（ViewModel）
    /// </summary>
    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var payload = e.ApplicationMessage.ConvertPayloadToString() ?? string.Empty;
        MessageReceived?.Invoke(this, new MqttMessageReceivedEventArgs(e.ApplicationMessage.Topic, payload));
        return Task.CompletedTask;
    }

    /// <summary>
    /// 释放 MQTT 客户端资源。
    /// IDisposable 接口的实现，由 App.OnExit 调用。
    /// </summary>
    public void Dispose()
    {
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _mqttClient.Dispose();
    }
}

/// <summary>
/// MQTT 消息接收事件参数。
/// 包含消息的主题（Topic）和内容（Payload）。
///
/// 继承关系：EventArgs 是 .NET 事件参数的基类。
/// 使用 C# 12 的主构造函数语法（primary constructor）简化声明。
/// </summary>
public class MqttMessageReceivedEventArgs(string topic, string payload) : EventArgs
{
    /// <summary>
    /// MQTT 消息主题，标识消息的来源/分类。
    /// 例如："ha/xiaomi/body" 表示来自 Home Assistant 的小米人体传感器事件。
    /// </summary>
    public string Topic { get; } = topic;

    /// <summary>
    /// MQTT 消息内容（负载），实际传输的数据字符串。
    /// </summary>
    public string Payload { get; } = payload;
}
