# DesktopOrganizer v1.12.8：Shell 图标后台加载报告

## 目标

v1.12.7 已将桌面视觉刷新改为增量更新，但新建图标控件时仍在 WPF Dispatcher 上同步调用 `SHGetFileInfo`、解析 Shell PIDL、创建 `HICON` 并转换 `BitmapSource`。桌面项目较多、快捷方式目标较慢或第三方 Shell 扩展响应异常时，刷新仍可能卡住整个界面。

Microsoft 的 `SHGetFileInfo` 文档明确建议从后台线程调用，否则 UI 可能停止响应；返回的 `HICON` 必须由调用方执行 `DestroyIcon`。WPF `Freezable` 文档说明冻结后的对象可跨线程共享。

- https://learn.microsoft.com/windows/win32/api/shellapi/nf-shellapi-shgetfileinfow
- https://learn.microsoft.com/dotnet/desktop/wpf/advanced/freezable-objects-overview

## 实现结果

### 1. 独立后台 Shell 线程

新增 `ShellIconLoadService.cs`：

- 单独的后台 STA 线程；
- 线程优先级为 `BelowNormal`；
- 在线程内显式初始化 COM apartment；
- 所有真实文件图标、Shell 命名空间 PIDL 图标和 HICON 转换均在该线程执行；
- 图标线程不直接访问 WPF 视觉树。

采用单线程串行调用是为了避免同时激活大量 Shell 扩展，并统一 HICON 生命周期。单个异常扩展可能延迟后续图标，但不会再冻结 UI。

### 2. 占位图优先显示

`CreateIconVisual` 不再同步提取图标。创建视觉时立即显示固定 42×42 DIP 占位图：

- Shell 系统项目：电脑占位符；
- 文件夹：文件夹占位符；
- 普通文件：文件占位符。

图标标签、拖动、双击、右键菜单、多选和分类功能不等待真实图标，可以立即使用。

### 3. 三级优先队列

后台队列按以下顺序处理：

1. 自由桌面图标；
2. 已展开分类框中的图标；
3. 已收起分类框中的图标。

同一缓存键如果先以低优先级排队，之后出现在更高优先级区域，会提升尚未执行的队列项。已经进入原生调用的项目不会重复排队。

### 4. 去重与弱引用等待者

请求身份由“大小写不敏感缓存键 + 图标缓存代次”组成：

- 相同扩展名普通文件共享一个扩展图标请求；
- 文件夹、EXE、快捷方式、URL、ICO 和 Shell 项目按路径或 parsing name 缓存；
- 多个视觉等待同一结果时，只调用一次 Shell；
- 等待列表只保存 `Image` 和占位文字的弱引用，视觉被删除后不会被异步队列强制保留。

普通扩展名图标使用 `SHGFI_USEFILEATTRIBUTES`，不依赖最先排队的实际文件在异步执行时仍然存在。

### 5. 跨线程安全回填

后台线程将 HICON 转换为 `BitmapSource` 后：

1. 检查 `CanFreeze`；
2. 执行 `Freeze()`；
3. 在 `finally` 中执行 `DestroyIcon`；
4. 将冻结结果排队到 WPF Dispatcher；
5. Dispatcher 只更新仍然存活且属于当前缓存代次的控件。

提取失败时缓存空结果并保留占位图，不影响项目的其他操作。

### 6. 缓存失效与迟到结果

手动刷新图标缓存或缓存超过回收阈值时：

- 清空当前缓存；
- 推进 `_iconVisualGeneration`；
- 删除旧代次等待者；
- 取消尚未开始的旧队列项；
- 无法中断的原生调用完成后，其旧结果会被代次检查丢弃。

因此，旧图标不会回填到重命名后的项目、重建后的视觉或新一代缓存。

### 7. 关闭保护

程序关闭时：

- 取消所有尚未执行的图标请求；
- 清空弱引用等待者；
- 唤醒后台线程退出；
- 最多等待 300 ms，不会因第三方 Shell 扩展卡住而无限阻塞窗口关闭；
- 如果原生调用尚未返回，线程保持后台线程，返回后自行完成 COM 清理并退出。

### 8. 关联修复

重命名迁移此前直接用完整路径删除 `_iconCache`，但缓存实际使用 `path:`、`folder:`、`ext:` 等派生键，导致部分旧缓存无法清除。现统一通过 `GetIconCacheKey` 删除旧路径和新路径对应缓存。

## 修改文件

- 新增 `ShellIconLoadService.cs`
- 新增 `MainWindow.AsyncIcons.cs`
- 修改 `MainWindow.IconVisuals.cs`
- 修改 `MainWindow.GroupVisuals.cs`
- 修改 `MainWindow.DesktopRefresh.cs`
- 修改 `MainWindow.DesktopIdentity.cs`
- 修改 `MainWindow.DesktopHost.cs`
- 修改 `NativeMethods.ShellIcons.cs`
- 修改 `NativeMethods.ShellNamespace.cs`
- 更新项目版本、README、架构、变更记录、测试清单与开源参考

## 持久化兼容性

- 程序版本：1.12.8
- 布局版本：仍为 Version 14
- 没有新增、删除或重命名 JSON 字段
- v1.12.7 和更早布局可直接使用
- 没有新增第三方 NuGet 依赖

## 已完成验证

- 38 个 C# 文件词法括号、字符串、字符和注释平衡检查；
- 5 个 XAML、项目和 manifest XML 解析；
- 35 个 XAML 事件处理器存在性检查；
- 17 个 StaticResource 键引用检查；
- 56 个 P/Invoke 声明重复签名检查；
- 全工程 `CreateBitmapSourceFromHIcon` 唯一调用者检查；
- 两个 Shell 图标句柄入口仅由后台服务调用检查；
- 优先级提升、大小写不敏感去重和缓存代次失效算法模拟；
- HICON `finally` 释放、冻结、弱引用、Dispatcher 回填和关闭释放路径源码断言；
- 布局 Version 14 不变和程序集 Version 1.12.8 检查。

## 尚需 Windows 实机验证

当前容器没有 .NET 10 SDK，也不是 Windows WPF/Explorer 桌面会话；尝试获取官方 SDK 安装脚本失败。因此本阶段无法执行真实 `dotnet build`、Shell 扩展调用和 UI 性能测量。

应在 Windows 10/11 上重点验证：

- 100、500、1000 个桌面项目的冷启动响应；
- 自由、展开分组、收起分组的图标出现顺序；
- 快捷方式、EXE、ICO、URL 和 Shell 系统项目图标；
- 高频刷新时队列与内存是否稳定；
- 图标加载期间删除、重命名、切屏、重启 Explorer 和退出；
- GDI/HICON 句柄是否持续增长。

完整步骤已加入 `TEST_CHECKLIST.md`。
