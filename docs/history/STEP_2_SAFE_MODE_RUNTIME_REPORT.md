# DesktopOrganizer v1.12.3 第二步修复报告

## 修复目标

解决安全模式会永久关闭用户“挤压排列”和“新项目归类”偏好的问题，并保证手动触发与健康监测自动触发具有相同的非持久化语义。

## 根因

v1.12.2 将 `SafeMode`、`PushReflowEnabled` 和 `AutoClassifyNewItems` 都保存在 `AppLayoutData`。进入安全模式时直接把后两项改成 `false`，随后调用 `SaveLayout()`；因此关闭安全模式或重启程序后，原偏好无法恢复。

## 实施内容

1. 删除 `AppLayoutData.SafeMode` 持久化字段，新增当前进程内的 `_isSafeModeActive`。
2. 新增 `IsPushReflowActive` 与 `IsAutoClassificationActive`，区分“用户偏好”与“当前是否允许执行”。
3. 进入安全模式时只停止 Watcher、取消防抖刷新和挤压预览，不修改任何用户偏好。
4. 安全模式界面保留原开关勾选状态，仅临时禁用控件，并用提示说明退出后恢复。
5. 健康监测自动进入安全模式时不再保存布局。
6. 退出安全模式时重新启动 Watcher，并请求一次桌面刷新以同步暂停期间的变化。
7. `ScheduleDesktopRefresh` 在排队前再次检查安全模式，关闭切换瞬间的 Watcher 并发窗口；交互结束后的手动刷新可显式绕过此限制。
8. 旧布局中的 `SafeMode` 属性由 `System.Text.Json` 作为未知字段忽略；布局版本保持 Version 12。

## 行为结果

- 用户偏好开启：安全模式中显示开启但暂停，退出后继续开启。
- 用户偏好关闭：安全模式前后均保持关闭。
- 安全模式不会跨程序重启保留。
- 任何其他布局保存都只保存真实偏好，不保存运行时安全状态。
- 旧版本已经把偏好写成 `false` 的布局无法可靠推断此前值，因此不会擅自改回 `true`。

## 验证范围

- 全工程 `_appLayout.SafeMode` 引用归零。
- 安全模式路径中不存在对两个偏好字段的赋值，也不存在 `SaveLayout()`。
- Watcher 创建、停止、事件回调、错误重启均统一读取运行时状态。
- 挤压预览、动画和新项目自动归类均使用有效状态门控。
- XAML/XML 可解析，事件处理器均存在，StaticResource 引用完整。
- C# 文件完成词法括号、字符串和注释平衡检查。
- 旧 Version 12 JSON 含 `SafeMode` 字段的兼容路径完成静态验证。

## 尚需 Windows 实机验证

当前执行环境没有 .NET 10 SDK，也没有 Windows WPF/Explorer 会话，因此不能完成真实编译、UI 自动化和 FileSystemWatcher 时序测试。对应步骤已加入 `TEST_CHECKLIST.md`。

## 本次自动检查结果

- C# 文件：30 个；XAML/项目/manifest XML：5 个。
- XAML 事件处理器：34 个，均能在 C# 中找到对应方法。
- StaticResource 键：17 个，引用未发现缺失。
- 安全模式语义断言：11 项全部通过。
- 未发现未闭合字符串、注释或括号。
- 未执行 `dotnet build`：当前容器没有 .NET 10 SDK，且无法运行 Windows WPF 桌面会话。
