# Xiaomi.Remind

> [中文](README.md) | English

> A lightweight desktop MQTT notification tool, seamlessly integrated with Home Assistant automations.

## Overview

Xiaomi.Remind is a lightweight MQTT client that runs on the Windows desktop, designed to receive MQTT messages published by Home Assistant (HA) automations and display them as system tray notifications.

Although the project name references Xiaomi devices (it was originally built for Xiaomi body sensors, door/window sensors, and other IoT device event notifications), **any device or service that can publish MQTT messages is supported**, thanks to the standard MQTT protocol.

## Features

- **MQTT Subscription** — Connect to any MQTT Broker and receive real-time messages on subscribed topics
- **System Tray Notifications** — Pop up Windows balloon notifications showing topic and payload
- **Message Log** — Display the latest 100 messages (timestamp/topic/payload) in a DataGrid
- **Auto-Close Processes** — Optionally close specified processes (e.g. Douyin, WeChat, QQ) on incoming messages to reduce distractions
- **Dark/Light Theme** — Auto-follows Windows system theme, with manual toggle support
- **Auto-Start on Login** — One-click to enable/disable startup, runs silently in the system tray
- **Single Instance** — Only one instance runs at a time; launching again activates the existing window
- **Auto-Reconnect** — Exponential backoff reconnection strategy with automatic re-subscription after reconnect
- **Config Persistence** — All settings are saved to `appsettings.json` in real time and restored on restart

## Tech Stack

| Component | Description |
|-----------|-------------|
| .NET 10.0 + WPF | Desktop application framework |
| MVVM (CommunityToolkit.Mvvm) | View-model separation |
| MQTTnet | MQTT client library |
| WPF-UI | Modern UI components |
| H.NotifyIcon | System tray icon |
| Microsoft.Toolkit.Uwp.Notifications | Native Windows notifications |

## Quick Start

### Requirements

- Windows 10/11
- [.NET 10.0 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (Desktop Runtime)

### Build & Run

```bash
git clone https://github.com/<your-username>/Xiaomi.Remind.git
cd Xiaomi.Remind
dotnet restore
dotnet run
```

### Publish Self-Contained Executable

```bash
dotnet publish -c Release -r win-x64 --self-contained
```

Published files are located in `bin/Release/net10.0-windows/win-x64/publish/`.

## Configuration

Edit `appsettings.json` before first run:

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

| Config Key | Description |
|------------|-------------|
| `Mqtt.Broker` | MQTT Broker server address |
| `Mqtt.Port` | Port number, default 1883 (plaintext) / 8883 (TLS) |
| `Mqtt.ClientId` | Unique client identifier |
| `Mqtt.UserName` / `Password` | Authentication credentials (optional) |
| `Mqtt.Topics` | Topics to subscribe, comma-separated, supports `+` and `#` wildcards |
| `UI.CloseProcessesOnMessage.Enabled` | Toggle process-closing on incoming messages |
| `UI.CloseProcessesOnMessage.Processes` | List of process names to close (without `.exe`) |

You can also modify MQTT configuration and process list from the Settings page while the app is running; changes are saved automatically.

## Home Assistant Integration Example

Publish MQTT messages in HA's `configuration.yaml` or automations:

```yaml
# Automation example: send notification when Xiaomi body sensor detects movement
automation:
  - alias: "Body Sensor Detects Movement"
    trigger:
      - platform: state
        entity_id: binary_sensor.xiaomi_body_sensor
        to: "on"
    action:
      - service: mqtt.publish
        data:
          topic: "ha/xiaomi/body"
          payload: "Movement detected"
```

The app will pop up a system tray notification upon receiving this message.

## Architecture

```
App.xaml.cs (entry point + event bridge + tray management)
    │
    ├── MqttClientService (MQTT connect/subscribe/reconnect)
    │
    └── AppViewModel (state management + config read/write + MVVM)
            │
            ├── MqttMessageRecord (message data model)
            ├── DesktopMinimizer (process window closing)
            └── StartupManager (login auto-start management)
```

- **MqttClientService**: Wraps MQTTnet, handles connection, subscription, and exponential backoff reconnection
- **AppViewModel**: Inherits `ObservableObject`, manages UI state and config persistence
- **DesktopMinimizer**: Closes target process windows via `EnumWindows` + `WM_CLOSE`
- **StartupManager**: Manages auto-start through registry `HKCU\...\Run`

## FAQ

**Q: Not receiving notifications?**

Check that the MQTT Broker address/port is correct, credentials match, and the subscribed topic matches what HA publishes.

**Q: Process closing not working?**

Verify the process name is correct (without `.exe` extension). Use Task Manager to check the exact process name. Note that `WM_CLOSE` is a graceful close — if the target process has unsaved data, it may prompt a confirmation dialog.

**Q: Can I subscribe to multiple topics?**

Yes, separate multiple topics with commas in `Mqtt.Topics`, e.g. `"ha/xiaomi/door,ha/xiaomi/body,home/alarm"`.

## License

MIT
