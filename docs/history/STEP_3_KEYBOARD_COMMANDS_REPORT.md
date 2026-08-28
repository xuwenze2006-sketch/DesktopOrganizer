# DesktopOrganizer v1.12.4 第三步修复报告

## 修复目标

解决主窗口采用 `ShowActivated=False`、`Focusable=False`、`WS_EX_NOACTIVATE` 后，WPF `PreviewKeyDown` 无法稳定收到 Ctrl+Z 与 Esc 的问题，同时保持桌面整理层点击不抢焦点。

## 根因

旧实现只在 `MainWindow.PreviewKeyDown` 中处理快捷键。主窗口为了充当桌面层明确禁止激活，因此绝大多数正常使用路径下不存在 WPF 键盘焦点，事件处理器虽然存在但无法触发。

## 实现

1. 新增 `NativeMethods.DesktopInput.cs`，集中管理 Win32 低级键盘、鼠标和前台切换监听。
2. 键盘监听只识别严格的 Ctrl+Z 与无修饰键 Esc；命令可执行时消费对应按键，其他输入不拦截。
3. 鼠标监听记录最后一次按钮按下是否命中整理窗口；外部点击立即清除。
4. 前台切换监听覆盖 Alt+Tab/Win+Tab，防止整理语境残留到其他应用。
5. 桌面判断比较具体 HWND：主整理窗口、Progman/Shell 桌面宿主和 Explorer 桌面列表根窗口，不以 explorer.exe 进程号粗略判断。
6. 原生回调只把命令排队到 Dispatcher；文件撤销、选择清理和界面更新仍由现有统一逻辑执行。
7. 主窗口关闭时释放三个原生监听；创建失败时保留按钮和 WPF 后备路径。

## 行为边界

- 点击整理层仍不会激活主窗口。
- Ctrl+Z/Esc 不会通过 `RegisterHotKey` 被系统级永久独占；仅在桌面语境且命令可执行时消费单次按键。
- 点击其他应用或切换前台后，DesktopOrganizer 不响应其中的 Ctrl+Z/Esc。
- 按住按键只执行一次，松开后再次按下才会再次执行。
- 注入按键不会触发真实文件操作。
- 未增加网络、遥测、第三方包或布局字段，布局继续使用 Version 12。

## 静态验证

- XAML、csproj 与 manifest XML 解析。
- XAML 事件处理器与 StaticResource 引用核对。
- C# 注释、字符串、圆/方/花括号词法平衡检查。
- 输入监听安装/释放配对和 Dispatcher 边界断言。
- 快捷键组合、自动重复与桌面/外部应用语境状态机模拟。
- ZIP CRC 与 SHA-256 完整性检查。

## 仍需 Windows 实机验证

当前执行环境无法启动 WPF/Explorer 桌面会话。需按 `TEST_CHECKLIST.md` 验证 Windows 10/11、不同输入法、辅助工具、安全软件及 Explorer 重启后的原生钩子行为。
