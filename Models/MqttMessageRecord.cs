namespace Xiaomi.Remind.Models;

/// <summary>
/// MQTT 消息记录数据模型。
/// 用于在 DataGrid 中展示每条收到的 MQTT 消息。
///
/// 此类是 POCO（Plain Old CLR Object），
/// 不包含业务逻辑，仅作为数据传输和 UI 绑定的载体。
/// DataGrid 的每一行对应一个 MqttMessageRecord 实例。
/// </summary>
public class MqttMessageRecord
{
    /// <summary>
    /// 消息到达时间。
    /// 记录格式为本地时间（DateTime.Now），例如 2026-04-14 10:30:45。
    /// UI 中格式化为 "yyyy-MM-dd HH:mm:ss" 显示。
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// MQTT 消息主题（Topic）。
    /// 主题标识了消息的来源和分类，例如 "ha/xiaomi/body"。
    /// 默认值为空字符串，避免 null 在 UI 绑定中产生异常。
    /// </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// MQTT 消息内容（Payload）。
    /// 实际传输的数据，具体内容取决于发布者的实现。
    /// 例如传感器事件可能包含 "detected"、"clear" 等状态文字。
    /// 默认值为空字符串，避免 null 在 UI 绑定中产生异常。
    /// </summary>
    public string Payload { get; set; } = string.Empty;
}
