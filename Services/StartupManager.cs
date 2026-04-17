using Microsoft.Win32;

namespace Xiaomi.Remind.Services;

/// <summary>
/// 开机启动管理器。
/// 通过读写 Windows 注册表的 Run 键来实现程序的开机自启/取消自启。
///
/// 注册表路径：HKCU\Software\Microsoft\Windows\CurrentVersion\Run
///   - HKCU（HKEY_CURRENT_USER）表示仅对当前登录用户生效
///   - Run 键下的每个值代表一个开机自动运行的程序
///   - 值名称 = 程序标识（本程序使用 "XiaomiRemind"）
///   - 值数据 = 可执行文件的完整路径
///
/// 使用 HKCU 而非 HKLM 的原因：
///   - HKCU 不需要管理员权限即可写入
///   - 仅影响当前用户，不影响其他用户
/// </summary>
public static class StartupManager
{
    /// <summary>
    /// Windows 自启动注册表路径。
    /// 此路径下的程序会在用户登录时自动启动。
    /// </summary>
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// 本程序在注册表中的值名称标识。
    /// </summary>
    private const string AppName = "XiaomiRemind";

    /// <summary>
    /// 启用开机启动。
    /// 将程序的可执行文件路径写入注册表 Run 键。
    ///
    /// Environment.ProcessPath 返回当前进程的可执行文件完整路径，
    /// 例如："D:\Apps\Xiaomi.Remind\Xiaomi.Remind.exe"
    /// </summary>
    public static void EnableStartup()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            throw new InvalidOperationException("无法获取应用程序路径");
        }

        // 打开 Run 键（可写模式），设置值
        // 值数据格式：带双引号的路径，例如 "\"D:\Apps\Xiaomi.Remind\Xiaomi.Remind.exe\""
        // 引号用于处理路径中包含空格的情况
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.SetValue(AppName, $"\"{exePath}\"");
    }

    /// <summary>
    /// 禁用开机启动。
    /// 从注册表 Run 键中删除本程序的值。
    /// throwOnMissingValue: false 表示值不存在时不抛出异常（静默处理）。
    /// </summary>
    public static void DisableStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(AppName, throwOnMissingValue: false);
    }

    /// <summary>
    /// 检查当前是否已启用开机启动。
    /// 如果注册表中存在本程序的值且不为空，则返回 true。
    /// </summary>
    public static bool IsEnabled()
    {
        // 以只读模式打开 Run 键
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        var value = key?.GetValue(AppName) as string;
        return !string.IsNullOrEmpty(value);
    }
}
