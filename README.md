# Xiaomi.Remind

> [English](README.en.md) | 中文

> 基于 MQTT 的桌面消息提醒工具，与 Home Assistant 自动化完美集成。

## 简介

Xiaomi.Remind 是一个运行在 Windows 桌面上的轻量级 MQTT 客户端，专门用于接收 Home Assistant（HA）自动化发布的 MQTT 消息，并在本地弹出系统托盘通知。

虽然项目命名与小米设备相关（最初用于接收小米人体传感器、门窗传感器等 IoT 设备的事件通知），但由于基于标准 MQTT 协议，**任何能通过 MQTT 发布消息的设备或服务均可使用**。

## 功能特性

- **MQTT 消息订阅** — 连接任意 MQTT Broker，订阅指定主题接收实时消息
- **系统托盘通知** — 收到消息时弹出 Windows 气泡通知，显示主题和内容
- **消息记录** — 在主窗口 DataGrid 中展示最近 100 条消息（时间/主题/内容）
- **自动关闭进程** — 收到消息时可选自动关闭指定进程（如抖音、微信、QQ 等），减少打扰
- **深色/浅色主题** — 跟随 Windows 系统主题自动切换，支持手动切换
- **开机自启动** — 一键设置/取消开机启动，静默运行于系统托盘
- **单实例运行** — 同时只允许运行一个实例，重复启动时自动激活已有窗口
- **自动重连** — 连接断开后使用指数退避策略自动重连，重连后自动恢复订阅
- **配置持久化** — 所有设置实时保存到 `appsettings.json`，重启后自动恢复

## 技术栈

| 组件 | 说明 |
|------|------|
| .NET 10.0 + WPF | 桌面应用框架 |
| MVVM (CommunityToolkit.Mvvm) | 视图模型分离 |
| MQTTnet | MQTT 客户端库 |
| WPF-UI | 现代化 UI 组件 |
| H.NotifyIcon | 系统托盘图标 |
| Microsoft.Toolkit.Uwp.Notifications | Windows 原生通知 |

## 快速开始

### 环境要求

- Windows 10/11
- [.NET 10.0 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)（桌面运行时）

### 编译运行

```bash
git clone https://github.com/<your-username>/Xiaomi.Remind.git
cd Xiaomi.Remind
dotnet restore
dotnet run
```

### 发布独立可执行文件

```bash
dotnet publish -c Release -r win-x64 --self-contained
```

发布后的文件位于 `bin/Release/net10.0-windows/win-x64/publish/`。

## 配置说明

首次运行前需编辑 `appsettings.json`：

```json
{
  "Mqtt": {
    "Broker": "192.168.2.5",
    "Port": 1883,
    "ClientId": "XiaomiRemind",
    "UserName": "ha",
    "Password": "123456",
    "Topics": "ha/xiaomi/#"
  },
  "UI": {
    "CloseProcessesOnMessage": {
      "Enabled": true,
      "Processes": [
        "douyin",
        "Weixin",
        "QQ"
      ]
    }
  }
}
```

| 配置项 | 说明 |
|--------|------|
| `Mqtt.Broker` | MQTT Broker 服务器地址 |
| `Mqtt.Port` | 端口号，默认 1883（非加密）/ 8883（TLS） |
| `Mqtt.ClientId` | 客户端唯一标识 |
| `Mqtt.UserName` / `Password` | 认证凭据（可选） |
| `Mqtt.Topics` | 订阅主题，多个主题用逗号分隔，支持 `+` 和 `#` 通配符 |
| `UI.CloseProcessesOnMessage.Enabled` | 是否启用收到消息时关闭进程 |
| `UI.CloseProcessesOnMessage.Processes` | 要关闭的进程名列表（不含 `.exe`） |

也可以在程序运行后通过设置页面修改 MQTT 配置和进程列表，修改后自动保存。

## Home Assistant 集成示例

在 HA 的 `configuration.yaml` 或自动化中发布 MQTT 消息：

```yaml
# 自动化示例：小米人体传感器检测到有人移动时发送通知
automation:
  - alias: "人体传感器检测到人"
    trigger:
      - platform: state
        entity_id: binary_sensor.xiaomi_body_sensor
        to: "on"
    action:
      - service: mqtt.publish
        data:
          topic: "ha/xiaomi/body"
          payload: "检测到有人移动"
```

程序收到该消息后会在系统托盘弹出通知。

## 界面预览

主窗口展示消息记录列表，支持一键清空。最小化后隐藏到系统托盘，双击托盘图标恢复窗口。

右侧导航栏可切换至设置页面，编辑 MQTT 连接参数和进程关闭列表。

## 架构说明

```
App.xaml.cs (入口 + 事件桥接 + 托盘管理)
    │
    ├── MqttClientService (MQTT 连接/订阅/重连)
    │
    └── AppViewModel (状态管理 + 配置读写 + MVVM)
            │
            ├── MqttMessageRecord (消息数据模型)
            ├── DesktopMinimizer (进程窗口关闭)
            └── StartupManager (开机自启动管理)
```

- **MqttClientService**：封装 MQTTnet，负责连接、订阅、指数退避重连
- **AppViewModel**：继承 `ObservableObject`，管理 UI 状态和配置持久化
- **DesktopMinimizer**：通过 `EnumWindows` + `WM_CLOSE` 关闭目标进程窗口
- **StartupManager**：通过注册表 `HKCU\...\Run` 实现开机自启

## 常见问题

**Q: 收不到通知？**

检查 MQTT Broker 地址/端口是否正确，用户名密码是否匹配，订阅主题是否与 HA 发布的主题一致。

**Q: 关闭进程没有生效？**

确认进程名是否正确（不含 `.exe` 后缀），可通过任务管理器查看准确的进程名。注意 `WM_CLOSE` 是友好关闭，如果目标进程有未保存数据可能弹出确认对话框。

**Q: 可以订阅多个主题吗？**

可以，在 `Mqtt.Topics` 中用逗号分隔多个主题，例如 `"ha/xiaomi/door,ha/xiaomi/body,home/alarm"`。
