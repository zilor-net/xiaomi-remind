using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Xiaomi.Remind.Services;

/// <summary>
/// 进程窗口关闭服务。
/// 功能：根据给定的进程名列表，枚举桌面上所有可见窗口，
///       匹配到对应进程的窗口后发送 WM_CLOSE 消息将其关闭。
///
/// 技术原理：
///   1. EnumWindows：枚举所有顶层窗口
///   2. GetWindowThreadProcessId：获取窗口所属的进程 ID
///   3. Process.GetProcessById(pid).ProcessName：根据进程 ID 获取进程名
///   4. PostMessage(hWnd, WM_CLOSE)：向窗口发送关闭消息（模拟用户点击关闭按钮）
///
/// 注意：
///   - WM_CLOSE 是"友好"关闭，窗口会收到关闭通知并有机会弹出确认对话框或保存数据
///   - 如果进程有未保存的数据，可能会弹出保存对话框
///   - 与 CloseWindow（实际是最小化）不同，WM_CLOSE 才是真正关闭窗口的消息
/// </summary>
public static class DesktopMinimizer
{
    /// <summary>
    /// WM_CLOSE 消息常量。
    /// 发送到窗口时，窗口会执行正常的关闭流程（触发 Closing 事件等）。
    /// </summary>
    private const int WM_CLOSE = 0x0010;

    /// <summary>
    /// EnumWindows 回调委托。
    /// 枚举窗口时，系统会为每个窗口调用此委托。
    /// 返回 true 继续枚举，返回 false 停止枚举。
    /// </summary>
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    #region Win32 API 声明

    /// <summary>
    /// 枚举所有顶层窗口。
    /// 参数 lpEnumFunc：回调函数，每个窗口调用一次
    /// 参数 lParam：传递给回调函数的自定义参数
    /// </summary>
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    /// <summary>
    /// 判断窗口是否可见。不可见的窗口（如隐藏窗口）不参与关闭操作。
    /// </summary>
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    /// <summary>
    /// 获取窗口所属的进程 ID 和线程 ID。
    /// 返回值：创建该窗口的线程 ID
    /// 输出参数 lpdwProcessId：进程 ID
    /// </summary>
    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

    /// <summary>
    /// 判断窗口是否处于最小化状态（图标化）。已最小化的窗口不需要关闭。
    /// </summary>
    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    /// <summary>
    /// 获取窗口标题的字符长度。
    /// 返回值：标题长度，0 表示无标题。
    /// </summary>
    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    /// <summary>
    /// 获取窗口标题文本。
    /// 参数 hWnd：窗口句柄
    /// 参数 lpString：接收标题的 StringBuilder
    /// 参数 nMaxCount：最大字符数
    /// 返回值：实际复制的字符数
    /// </summary>
    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    /// <summary>
    /// 异步向窗口发送消息。
    /// 与 SendMessage 不同，PostMessage 将消息放入消息队列后立即返回，不等待处理。
    /// 这使得关闭窗口的操作不会阻塞当前线程。
    /// </summary>
    [DllImport("user32.dll")]
    private static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    #endregion

    /// <summary>
    /// 关闭指定进程的所有可见窗口。
    ///
    /// 执行流程：
    ///   1. 将进程名列表转为 HashSet（忽略大小写，提高查找效率）
    ///   2. 枚举所有顶层窗口，过滤出：可见 + 非最小化 + 非自身进程
    ///   3. 对每个窗口获取其进程 ID，查进程名是否在目标列表中
    ///   4. 收集所有匹配的窗口句柄
    ///   5. 逐个发送 WM_CLOSE 消息关闭窗口
    ///
    /// 参数 processNames：目标进程名列表（不含 .exe 后缀），例如 ["douyin", "Weixin", "QQ"]
    /// </summary>
    public static void CloseWindowsByProcess(IEnumerable<string> processNames)
    {
        // 转为 HashSet，忽略大小写，提升查找效率
        var targets = new HashSet<string>(processNames, StringComparer.OrdinalIgnoreCase);
        if (targets.Count == 0)
            return;

        var currentProcessId = Process.GetCurrentProcess().Id;  // 自身进程 ID，用于排除
        var windows = new List<IntPtr>();                        // 存储匹配的窗口句柄

        // 枚举所有顶层窗口
        EnumWindows((hWnd, _) =>
        {
            // 跳过不可见或已最小化的窗口
            if (!IsWindowVisible(hWnd) || IsIconic(hWnd))
                return true;

            // 获取窗口所属的进程 ID
            GetWindowThreadProcessId(hWnd, out var pid);

            // 跳过自身进程的窗口（不要关闭自己的窗口）
            if (pid == currentProcessId)
                return true;

            try
            {
                // 根据进程 ID 获取进程名
                var process = Process.GetProcessById(pid);
                if (targets.Contains(process.ProcessName))
                {
                    windows.Add(hWnd);  // 匹配成功，加入列表
                }
            }
            catch
            {
                // 进程可能已退出，或无权限访问，跳过即可
            }

            return true;  // 继续枚举下一个窗口
        }, IntPtr.Zero);

        // 逐个发送 WM_CLOSE 消息关闭窗口
        foreach (var hWnd in windows)
        {
            PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
