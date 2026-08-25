# DesktopOrganizer v1.12.1 精简报告

## 清理原则

本次采用保守策略：只删除能够通过全局文本引用、XAML 资源引用和入口关系确认无效或重复的内容，不删除现有用户功能，也不重写高风险的 Shell、拖放和持久化流程。

## 源码清理

- 删除 `NativeMethods.HideWindowWithoutActivation`。
- 删除 `NativeMethods.ShowWindowWithoutActivation`。
- 删除 `NativeMethods.IsWindowHandleValid`。
- 删除只服务于上述废弃包装的 `SW_SHOWNOACTIVATE` 常量。
- 将 `RebuildDesktopIconsAndSaveLayout` 移入 `MainWindow.DesktopRefresh.cs`，删除单方法文件 `MainWindow.Common.cs`。
- 删除 XAML 中没有任何引用的 `AccentSoftBrush`。
- 删除没有代码引用的 `ControlPanelHeader` 名称，但保留对应 Grid 和布局。
- 删除由 `GlobalUsings.cs` 已提供的重复 using 声明。
- 将 `IconPosition`、`GroupInfo`、`AppLayoutData`、`IconTag` 和 `GroupSortMode` 收紧为程序集内部类型；公开 JSON 属性保持不变。
- 删除 `App.xaml` 中空的 `Application.Resources` 节点。

## 文档清理

- 将分散的版本文档合并为 `CHANGELOG.md`。
- 将架构说明统一为 `ARCHITECTURE.md`。
- README 仅保留当前功能、运行、发布、安全和限制说明。
- 删除旧版 `CHANGELOG_v*`、`VALIDATION_v*`、`LAYERING_AUDIT_v*`、`STABILITY_AUDIT_v*` 和 `UX_OPTIMIZATION_v*` 文件。

## 保留内容

- 所有 XAML 事件处理器。
- 自动分类、拖放、挤压排列、智能布局、文件移动与撤销。
- Shell 桌面层、右键菜单监听、托盘、单实例和开机启动。
- 布局 Version 11 与 `%AppData%\DesktopOrganizer\layout.json` 路径。
- 三个运行/发布脚本以及发布配置。

## 验证

- 全量扫描所有 `.cs`、`.xaml`、`.xml`、`.cmd` 和 `.md` 文件。
- 清理后重新检查低引用方法，没有剩余“仅声明、无调用”的普通 C# 方法。
- 检查 XAML 资源键，所有普通资源均有静态、动态或代码引用；模板 `PART_*` 名称按 WPF 约定保留。
- 检查所有 XAML 事件处理器均能在 C# 中找到对应方法。
- 解析 `App.xaml`、`MainWindow.xaml`、`SimpleInputDialog.xaml`、项目文件、manifest 和发布配置 XML。
- 检查 C# 括号、字符串和注释词法平衡。
- 检查压缩包 CRC。

当前容器没有 .NET 10 SDK 和 Windows WPF/Explorer 会话，因此没有执行真实 `dotnet build`、窗口层级或拖放运行测试。请在 Windows 上运行 `publish-win-x64.cmd` 并按 `TEST_CHECKLIST.md` 做最终回归。
