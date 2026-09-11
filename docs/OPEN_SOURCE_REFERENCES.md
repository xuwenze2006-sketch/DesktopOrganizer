# 开源实现参考

本版本优先调研了 GitHub 上的 WPF 拖拽与重排项目，但没有直接引入第三方包。

## GongSolutions.WPF.DragDrop

- 仓库：`https://github.com/punker76/gong-wpf-dragdrop`
- 许可证：BSD-3-Clause
- 借鉴内容：拖动生命周期分层、拖动中视觉反馈、同一控件内重排、预览与 Drop 提交分离。
- 本项目处理：没有复制其实现，也没有添加 NuGet 引用；仅将这些设计原则用于当前 Canvas 网格布局。

## wpf-drag-animated-panel

- 仓库：`https://github.com/rulyotano/wpf-drag-animated-panel`
- 借鉴内容：拖动过程中实时移动其它元素、使用短动画表达重排结果。
- 许可证注意：仓库页面未明确显示许可证。因此本项目仅参考可观察的交互思路，没有复制源码。

## 本项目独立实现

- 拖动开始时创建自由图标坐标快照。
- 每次进入新目标时，从快照重新规划挤压链，不累计临时偏移。
- 拖离目标时恢复快照。
- 松开鼠标时才把预览结果写入布局模型。
- 分组占用网格会从可用网格序列中排除。
- 意外丢失鼠标捕获时，取消操作并恢复快照。


## justberatt/DesktopOrganizer

- 仓库：`https://github.com/justberatt/DesktopOrganizer`
- 许可证：MIT
- 借鉴内容：按扩展名建立类别、执行前预览、撤销以及新文件自动归类的安全交互原则。
- 本项目处理：只创建虚拟桌面分组，不执行其“移动文件到真实目录”的方案，也未复制源码。

## gregoryngatia/FileOrganizerCLI

- 仓库：`https://github.com/gregoryngatia/FileOrganizerCLI`
- 许可证：MIT
- 借鉴内容：字典式扩展名映射、分类数量汇总、确认后应用、反向恢复的流程。
- 本项目处理：分类规则和 WPF 集成均独立编写；没有新增 NuGet 依赖。


## hardcodet/wpf-notifyicon 与 HavenDV/H.NotifyIcon

- 仓库：`https://github.com/hardcodet/wpf-notifyicon`、`https://github.com/HavenDV/H.NotifyIcon`
- 借鉴内容：通知区域图标生命周期、Explorer 重启后的重新注册、双击与右键菜单分工、退出时释放图标。
- 本项目处理：没有引入对应 NuGet，也没有复制其控件源码；使用 `Shell_NotifyIcon`、`TaskbarCreated` 和原生弹出菜单独立实现轻量通知区域服务。
- 开机启动采用当前用户 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，只写入当前 EXE 路径和 `--startup` 参数。

## DesktopFramesPlus / Portals / coodesker

- 仓库：`https://github.com/limbo666/DesktopFramesPlus`
- 仓库：`https://github.com/Ross-Patterson/Portals-Desktop-Organization`
- 仓库：`https://github.com/coodesker/coodesker-desktop`
- 借鉴内容：桌面专属容器、按需收起、精简控制入口、自动规则、可调整分组和避免桌面长期被大面积遮挡的产品方向。
- 本项目处理：未复制这些项目的窗体、配置或文件管理源码；继续使用 WPF + Win32 独立实现，并保持零第三方 NuGet 依赖。

## Progman / WorkerW 桌面宿主模式

- 公开实现方向可见 GitHub 上多个 WorkerW / Progman 示例，例如 `Decision2016/754ddad2c21f320a41429b9b5c021fe6` gist。
- 借鉴内容：通过 `Progman`、`WorkerW`、`SHELLDLL_DefView` 查找 Explorer 桌面宿主，再以 `WS_CHILD` + `SetParent` 真正嵌入桌面层；普通应用因此不会被整理窗口覆盖。
- 本项目处理：针对“桌面图标替代层”重新编写了宿主查找、窗口样式切换、显示设置适配、Explorer 重启重连和失败回退逻辑。


## v1.6.1 输入与渲染修复

- 参考 Microsoft Win32 `WM_NCHITTEST`、`WM_MOUSEACTIVATE` 的公开语义：交互区域返回 `HTCLIENT`，桌面空白区域返回 `HTTRANSPARENT`；无激活窗口返回 `MA_NOACTIVATE`，保留鼠标消息但不抢前台焦点。
- 参考 `dotnet/wpf` 与 Microsoft WPF 动画文档中“减少无意义属性动画、清除旧动画时使用 `BeginAnimation(property, null)`”的实现原则。
- 本项目未复制第三方源代码；命中区域缓存、拖动阈值、轻量标签背景和增量挤压预览均为针对当前桌面整理层的独立实现。


## v1.8.0 轻量视觉设计参考

- `lepoco/wpfui`：参考 Fluent 卡片的层级、圆角、强调色与交互状态组织方式；MIT 许可证。
- `Kinnara/ModernWpf`：参考 WPF 中接近 WinUI 的控件状态、主题资源和高对比设计方向。
- 本项目没有引入这些组件，也没有复制其控件源码；仅使用 WPF 原生渐变、Border、ControlTemplate 和静态悬停状态独立实现。
- 为避免全屏透明窗口再次出现性能问题，本版本没有使用 Acrylic 模糊、循环动画或为每个分类框添加 DropShadowEffect。

## v1.8.2 Dispatcher 与文件监听稳定性

- 参考 Microsoft 官方 `dotnet/wpf` 的 Dispatcher 线程模型：UI 线程上的工作项应保持短小，耗时扫描在后台完成，再通过 Dispatcher 应用结果。
- 参考 Microsoft 官方 `dotnet/runtime` 中 `FileSystemWatcher` / `PhysicalFilesWatcher` 的事件合并与错误恢复方向：文件系统通知可能重复或发生缓冲区错误，消费端需要防抖、合并和重建监听器。
- 本项目没有复制上述仓库源码；独立实现了 850 ms 防抖、single-flight 刷新、未变化快照跳过、后台分类扫描和 watcher 错误重启。
- 鼠标捕获修复遵循 WPF routed input 的控件边界原则：按钮、拖动区与动态视觉树分别管理捕获，MouseUp 后清除异常遗留状态。


## v1.9.0 编辑模式与瀑布流布局

- `CodingConnected.WPF.TileCanvas`：参考其将浏览状态与 `IsEditMode` 分开的产品结构，以及拖动、缩放和布局持久化职责分离的方向；本项目未引入该 NuGet，也未复制源码。
- `EleCho.WpfSuite` 的 `MasonryPanel`：参考最短列瀑布流的布局概念；本项目针对可持久化 Canvas 分组独立实现位置计算和单步撤销。
- `dotnet/wpf` 与 `microsoft/WPF-Samples`：继续遵循 WPF 原生布局、输入路由和 Dispatcher 的公开实现模式。
- 本版本仍保持零第三方 NuGet，所有交互与布局代码均位于当前项目内。

## v1.9.4 真实文件夹投放

- `punker76/gong-wpf-dragdrop`：参考其将“拖动源、目标检测、目标高亮、松手提交”分离的交互结构，以及明确区分无效目标与有效投放目标的做法；BSD-3-Clause。
- 本项目没有引入该 NuGet，也没有复制其 `IDropTarget` 实现；真实文件夹命中、路径安全校验、同名冲突保护和布局回滚均基于现有 Canvas 拖动系统独立编写。
- 真实文件移动只在鼠标松开且目标文件夹明确高亮时执行，拖动预览阶段不会修改磁盘内容。


## v1.10.0 安全、撤销与多选

- `punker76/gong-wpf-dragdrop`：继续参考拖动源、目标反馈、提交与回滚分离的结构；BSD-3-Clause。本项目未引入对应 NuGet。
- `dotnet/wpf` 与 `microsoft/WPF-Samples`：参考 Dispatcher 输入优先级、路由输入和控件状态管理方向。
- `dotnet/runtime` 的 FileSystemWatcher 实现与测试：参考事件重复、错误恢复和消费者防抖原则。
- 文件移动撤销、冲突自动重命名、分类排序、多选状态和安全模式均针对当前 Canvas 布局独立实现，没有复制第三方源码。


## v1.11.0 布局与滚动细节

- `microsoft/PowerToys` FancyZones：参考“编辑布局与日常使用分离”、布局持久化和显示器工作区约束。
- `sbaeumlisberger/VirtualizingWrapPanel`（MIT）：参考 WPF 多列内容面板的宽度、滚动和大项目集合处理思路；本项目未引入该 NuGet，仍使用原生控件。
- `dotnet/wpf`（MIT）：参考 ScrollViewer、ScrollBar 模板部件和 WPF 输入布局行为。

## v1.12.2 稳定文件身份与重命名迁移

- `dotnet/runtime`：参考 `FileSystemWatcher` 对重命名事件、重复通知和缓冲区错误的公开实现与测试方向；仓库采用 MIT 许可证。
- Microsoft Windows SDK 文档：使用 `CreateFileW`、`GetFileInformationByHandleEx(FileIdInfo)` 与 `FILE_ID_INFO` 获取卷序列号和 128 位文件 ID。
- 本项目没有复制运行库源码；只通过系统 API 读取文件元数据，并独立实现布局身份表、批量名称映射、大小写改名、连续改名和名称交换保护。
- 身份数据只写入 `%AppData%\DesktopOrganizer\layout.json`，不读取文件内容，也不发送网络请求。


## v1.12.5 多显示器参考

- `microsoft/PowerToys` FancyZones：参考其以显示器设备为布局边界、在显示配置变化时重建/迁移工作区的架构原则；MIT 许可证。
- Microsoft Win32 多显示器文档：使用 `EnumDisplayMonitors`、`GetMonitorInfo`、`rcMonitor` 与 `rcWork`，避免把主屏 `SystemParameters.WorkArea` 当成完整桌面。
- 本项目没有复制 FancyZones 源码，也未引入 PowerToys 组件；显示器枚举、DPI 转换、Version 13 数据迁移和 WPF Canvas 约束均为独立实现。

## v1.12.6 Shell 命名空间参考

- Microsoft Windows Shell 文档：采用 `SHGetDesktopFolder`、`IShellFolder.EnumObjects`、`IEnumIDList`、`SFGAO`、`SHGetNameFromIDList`、`SHGetFileInfo(SHGFI_PIDL)` 与 `ShellExecuteEx(SEE_MASK_INVOKEIDLIST)` 的公开契约。
- `dahall/Vanara`（MIT）：核对 Windows Shell 类型封装、PIDL 生命周期和 Shell API 调用边界；项目未引入 Vanara NuGet，也未复制其封装源码。
- `files-community/Files`（MIT）：参考现代文件管理器将 Shell 项目身份、展示名称、图标和动作分层处理的方向；本项目保持独立的轻量 WPF + P/Invoke 实现。
- `microsoft/PowerToys`（MIT）：继续参考大型 Windows 工具对原生资源生命周期、诊断与失败降级的工程原则。
- 本版本没有新增第三方运行依赖；所有互操作声明和业务集成均针对当前项目独立编写。



## v1.12.8 异步 Shell 图标参考

- Microsoft `SHGetFileInfo` 文档明确建议从后台线程调用，并要求调用方对返回的 `HICON` 执行 `DestroyIcon`。
- Microsoft WPF `Freezable` 文档说明冻结对象可跨线程共享；本项目仅把已经 `Freeze()` 的 `BitmapSource` 回填到 Dispatcher。
- `files-community/Files`（MIT）：参考现代文件管理器将项目枚举、图标/缩略图获取、缓存与视图生命周期分开的工程方向；未复制其 WinUI 实现。
- `microsoft/PowerToys`（MIT）：参考后台工作与 UI Dispatcher 分离、取消与关闭流程不无限等待外部组件的工程原则。
- 本项目未新增第三方 NuGet；优先级去重队列、弱引用等待者、缓存代次和后台 STA 生命周期均为独立实现。


## v1.12.9 WPF 分类框虚拟化

- `sbaeumlisberger/VirtualizingWrapPanel`（MIT）：参考其“只实现视口与缓存范围、回收离屏容器、通过 IScrollInfo 协调 ScrollViewer”的架构方向。
- Microsoft WPF `VirtualizingPanel`、`VirtualizingStackPanel` 与 `IScrollInfo` 文档：参考 Extent、Viewport、Offset、ScrollOwner 失效通知和可见范围生成原则。
- 本项目没有引入该 NuGet 包，也没有复制其通用 ItemsControl/ItemContainerGenerator 实现；`VirtualizingGroupPanel` 针对固定图标尺寸、固定列数、现有手写拖拽和增量视觉注册机制独立实现。


## v1.12.10 后台文件操作参考

- Microsoft WPF threading model：参考“Dispatcher 线程只执行短小 UI 工作、耗时工作移出 UI 线程、完成后回到 Dispatcher 更新界面”的官方线程边界。
- Microsoft Windows Shell `IFileOperation` / `IFileOperation::PerformOperations` 文档：核对文件操作对象的 STA 使用约束、回收站语义、取消与完成结果边界。当前版本继续复用既有 `Microsoft.VisualBasic.FileIO` 回收站入口，但将其放入独立 STA 串行线程。
- `microsoft/PowerToys`（MIT）与 `files-community/Files`（MIT）：参考大型 Windows 工具将文件系统工作、UI 状态和操作完成回填分层，以及关闭时不无限等待外部 Shell 组件的工程方向。
- 本项目没有复制上述项目源码，也未引入第三方 NuGet；`FileOperationService`、路径预留、逐项结果汇总和布局提交策略均针对当前 WPF Canvas 架构独立实现。

## v1.12.12 构建与测试工具

- GitHub Actions 官方 .NET 工作流文档：使用 `actions/setup-dotnet` 固定 SDK，通过 `dotnet restore/build/test` 执行验证并使用 artifact 保存测试结果。
  - https://docs.github.com/actions/tutorials/build-and-test-code/net
- `actions/setup-dotnet`：MIT 许可证；工作流使用 v5，并由 `global.json` 选择 .NET 10 SDK。
  - https://github.com/actions/setup-dotnet
- MSTest：MIT 许可证；测试项目使用 Microsoft 维护的 TestFramework 与 TestAdapter。
  - https://github.com/microsoft/testfx
- Coverlet：MIT 许可证；只作为测试项目的数据收集器生成 Cobertura 覆盖率，不进入桌面程序发布产物。
  - https://github.com/coverlet-coverage/coverlet

本版本未复制上述项目源码。运行时项目仍不引用第三方 NuGet；NuGet 依赖仅存在于测试项目。

## v1.12.15 回收站专属组件

- Microsoft Windows Shell `SHQueryRecycleBin` / `SHQUERYRBINFO` 文档：依据公开契约读取全部磁盘回收站的项目数量与占用字节数，不枚举或读取被删除文件内容。
- Microsoft Windows Shell `SHEmptyRecycleBin` 文档：使用应用自己的确认界面后调用无二次确认、无进度窗口、无声音标志，并在既有 STA 文件任务队列中执行。
- `files-community/Files`（MIT）中的 `StorageTrashBinService`：参考现代文件管理器把回收站状态、清空动作与普通目录项目分离为专门服务的工程方向；本项目未复制其 WinUI、CsWin32 或存储实现。
- 本版本未新增第三方 NuGet；圆角卡片、状态轮询、布局迁移、拖动和 WPF 交互均针对当前桌面宿主独立实现。
