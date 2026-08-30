# DesktopOrganizer 架构说明

## 重构原则

1. v1.12.0 是完整功能基线；v1.12.1 做保守清理；v1.12.2 增加稳定身份；v1.12.3 将安全模式改为运行时覆盖层；v1.12.4 修复不激活窗口快捷键；v1.12.5 引入虚拟桌面、多显示器工作区和 DPI 坐标迁移；v1.12.6 接入 Windows Shell 桌面命名空间；v1.12.7 将桌面视觉刷新改为按键值差异复用；v1.12.8 将 Shell 图标提取迁移到后台优先队列；v1.12.9 对分类框子项实施可视区域虚拟化；v1.12.10 将真实文件移动、回收站删除与撤销迁移到后台 STA 串行队列；v1.12.11 修复该版本首次真实编译发现的 C# 作用域错误和可空性警告；v1.12.12 建立 Windows 持续集成和可自动执行的核心逻辑测试；v1.12.13 将 Shell 桌面项目收敛为只显示回收站的 CLSID 白名单；v1.12.14 收紧显示消息、Z 序巡检和扫描快照提交条件，消除全屏 layered window 与全部图标反复重绘；v1.12.15 将回收站从普通图标体系剥离为独立状态小组件；当前主线继续加入命名工作区、待整理收件箱/搜索、可解释规则/标签、只读 Portal 和跨会话操作账本。
2. 保留既有 XAML 事件入口，新能力使用独立事件入口；布局文件当前为 Version 18，并保持旧版格式向后兼容。
3. 不通过复制旧逻辑建立第二套实现；每项行为只有一个实现入口。
4. UI 事件仍由 `MainWindow` partial class 接收，避免 WPF 事件绑定和状态迁移风险。
5. 纯 Win32 互操作继续集中在 `NativeMethods` partial class，业务代码不直接新增 P/Invoke。

## MainWindow 模块

| 文件 | 职责 |
|---|---|
| `MainWindow.xaml.cs` | 常量、状态、会话模型、构造函数 |
| `MainWindow.DesktopHost.cs` | 桌面宿主、Z 序、窗口生命周期、WindowProc |
| `MainWindow.DesktopGeometry.cs` | 虚拟桌面转换、显示拓扑迁移和工作区约束 |
| `MainWindow.Settings.cs` | 常用命令、设置、安全模式、面板显示 |
| `MainWindow.RecycleBinWidget.cs` | 独立回收站组件的状态、拖动、打开、清空和布局持久化 |
| `MainWindow.ControlPanelInteraction.cs` | 面板拖动、命中与异常输入恢复 |
| `MainWindow.AutoClassification.cs` | 本地识别分类与自动归类 |
| `MainWindow.DesktopRefresh.cs` | 桌面扫描、FileSystemWatcher、防抖刷新、视觉差异协调与缓存回收 |
| `MainWindow.DesktopIdentity.cs` | 真实文件 ID 与 Shell parsing name 身份迁移 |
| `MainWindow.IconVisuals.cs` | 图标视觉、菜单、多选 |
| `MainWindow.AsyncIcons.cs` | 占位图、异步等待者、缓存代次与 Dispatcher 回填 |
| `MainWindow.GroupVisuals.cs` | 分类框视觉、尺寸、排序与虚拟化面板装配 |
| `MainWindow.GroupPeek.cs` | 收起分类框的延迟悬停预览、运行时状态和虚拟化释放 |
| `VirtualizingGroupPanel.cs` | 固定网格分类子项的可视行实现、回收、滚动范围和拖动取出 |
| `MainWindow.FreeIconDrag.cs` | 自由图标拖动、分类投放、真实文件夹移动的 UI 验证与排队入口 |
| `MainWindow.FileOperations.cs` | 真实文件任务预留、后台执行、结果汇总、撤销与布局回填 |
| `MainWindow.Workspaces.cs` | 命名工作区的创建、复制、预览、切换、覆盖、重命名和删除 |
| `MainWindow.Inbox.cs` | 待整理收件箱入口、建议审阅和布局应用 |
| `MainWindow.Search.cs` | 当前桌面搜索、智能视图、定位和分组展开 |
| `MainWindow.Rules.cs` | 用户规则、标签、预览、执行门禁和 JSON 导入导出 |
| `MainWindow.FolderPortals.cs` | 只读 Portal 视觉、异步枚举、面包屑和写操作屏障 |
| `MainWindow.OperationJournal.cs` | 写前账本接线、启动恢复、跨会话撤销历史和操作中心入口 |
| `MainWindow.PushReflow.cs` | 挤压排列预览、回滚与提交 |
| `MainWindow.GroupItemActions.cs` | 分类内拖动、回收站与打开操作 |
| `MainWindow.SmartLayout.cs` | 智能布局、临时区、布局撤销 |
| `MainWindow.GroupInteraction.cs` | 分类框拖动、缩放、删除与重命名 |
| `MainWindow.GridLayout.cs` | 网格对齐、边界限制、布局迁移 |
| `MainWindow.Persistence.cs` | 防抖保存、原子写入、退出恢复 |

## 独立运行服务

| 文件 | 职责 |
|---|---|
| `ShellIconLoadService.cs` | 后台 STA 优先队列、请求去重、缓存代次取消、HICON 转换与释放 |
| `FileOperationService.cs` | 后台 STA FIFO 文件任务队列、取消尚未开始任务与进程退出收尾 |
| `WorkspaceLayoutManager.cs` | 版本化视觉快照、深复制、激活和工作区预览 |
| `InboxQueueManager.cs` / `DesktopSearchIndex.cs` | 新项目收件箱协调与内存桌面索引 |
| `OrganizationRuleEngine.cs` / `UserOrganizationRules.cs` | 无副作用预览、规则生命周期、冲突与动作计划 |
| `FolderPortalService.cs` | 根身份和路径边界核验、TopDirectoryOnly 只读枚举 |
| `FileOperationJournal.cs` / `FileOperationJournalStore.cs` | 逐项状态机、写前协调、原子独立存储和只读恢复核验 |
| `TrayIconService.cs` | 通知区域图标、原生菜单与 Explorer 重启恢复 |
| `AppDiagnostics.cs` | 慢操作和健康状态日志 |

## NativeMethods 模块

| 文件 | 职责 |
|---|---|
| `NativeMethods.cs` | P/Invoke 声明、结构、常量与基础指针封装 |
| `NativeMethods.WindowEvents.cs` | WinEvent、Shell 菜单与临时窗口识别 |
| `NativeMethods.DesktopLayer.cs` | Progman 桌面层、外部窗口和 Z 序 |
| `NativeMethods.ShellIcons.cs` | 真实文件的 Shell 图标句柄提取（仅由后台服务调用） |
| `NativeMethods.ShellNamespace.cs` | 桌面命名空间枚举、PIDL 图标及 Shell 动作 |
| `ShellDesktopItemPolicy.cs` | Shell 桌面项目白名单；当前仅允许回收站 CLSID |
| `NativeMethods.Tray.cs` | 原生通知区域图标和菜单 |
| `NativeMethods.DesktopInput.cs` | 不激活桌面的键盘命令、鼠标语境与前台切换监听 |
| `NativeMethods.Display.cs` | 显示器枚举、工作区、DPI 和物理窗口范围 |
| `NativeMethods.RecycleBin.cs` | 回收站状态查询与清空 Shell API 封装 |



## 本地工作台能力边界

- 工作区只保存视觉布局、显示器拓扑和 Portal 配置。复制会深拷贝所选快照但不激活或应用；切换前先捕获当前快照，真实文件任务、拖动或规则执行期间禁止切换，恢复过程不调用文件系统写 API。
- 收件箱首次完整扫描只建立基线；后续新项目按稳定身份维护建议。显式批量接受只处理待处理、可靠且未受手工分组保护的项目，并只提交一次布局保存；低可靠度和稍后处理结果保持原位。搜索只投影 `_desktopItems` 及本地布局元数据，不做全盘/正文索引。
- 用户规则固定为草稿、预览、执行一次、启用自动应用四阶段；动作仅为虚拟分组、标签或收件箱。预览和取消无副作用，执行失败回滚并停止，不自动重试。
- Portal 根路径先核验稳定身份，只枚举当前目录直属项且最多 500 项。普通目录可在根范围内导航，重解析点仅交给 Shell 外部打开；非递归 watcher 对当前目录变化防抖刷新，导航、移除、工作区切换和安全模式会解绑监听。读取失败保留上次成功内容并标为过期，不自动重试，也没有拖放写入、删除、移动或重命名入口。
- 操作账本与 `layout.json` 独立。`Queued` 在进入后台队列前落盘，`Running` 在调用真实操作前落盘；终态保存失败只展示最后持久化状态并进入保护态。启动时只读核验，不自动重试。真实移动快照覆盖全部命名工作区，成功撤销后恢复分组、顺序、坐标、标签和收件箱元数据。

## v1.12.15 回收站组件边界

- 回收站不进入 `_desktopItems`、自由图标、分类框、自动分类、挤压排列或批量选择，避免系统功能入口与用户内容混杂。
- `MainWindow.RecycleBinWidget.cs` 独立管理卡片视觉状态、四秒后台状态刷新、拖动位置、显示开关、打开和清空操作。
- 状态查询通过 `SHQueryRecycleBin` 在后台线程执行；清空通过既有 `FileOperationService` 的 STA 串行队列执行，WPF Dispatcher 不直接运行 Shell 文件操作。
- 组件位置保存在 `RecycleBinWidgetLayoutInfo`；Version 14 及更早布局升级时按固定回收站 CLSID 清理旧自由坐标、分组成员和身份记录。
- 网格、智能布局和自动分组定位只在组件实际可见时把其边界视为障碍物；初始化前的隐藏占位不会错误挤开左上角内容。
- 手动清空图标缓存会同步重建组件图标请求，旧缓存代次不能让组件永久停留在占位图。

## v1.12.14 防闪烁边界

- `DesktopWindowMessagePolicy` 只接受 `WM_DISPLAYCHANGE`、`WM_DPICHANGED` 与 `WM_SETTINGCHANGE/SPI_SETWORKAREA`；壁纸、主题、颜色和辅助功能广播不重算全屏窗口。
- 一次显示配置变化的多条消息在 180 ms 内合并；只有窗口边界或显示器拓扑确实变化时才更新现有视觉坐标，不重新扫描桌面或清空图标缓存。
- 普通 `EVENT_OBJECT_SHOW` 不再触发桌面层校正；前台窗口事件只提升外部应用，运行期桌面层健康检查允许 Progman 与整理层之间存在临时 Shell 窗口。
- 用户桌面目录采用“完整枚举后一次性提交”。目录读取失败时保留上一份快照并退避重试，绝不提交半份目录内容。
- Shell 命名空间枚举失败时保留上一轮回收站项目；后台图标空结果不会覆盖已显示的有效图标。
- 原生桌面图标只有在桌面层连续三次确认失效后才安全恢复，避免一次瞬态查询失败造成原生图标与整理图标交替显示。

## v1.12.12 构建与测试边界

- `DesktopOrganizer.slnx` 只包含 WPF 应用和 `DesktopOrganizer.Tests`；CI 不启动桌面宿主，不隐藏 Explorer 图标，也不执行真实文件移动或回收站操作。
- 测试通过 `InternalsVisibleTo` 访问内部纯逻辑类型，避免为了测试把实现细节改成公共 API。
- 自动测试覆盖分类扩展映射、开发项目识别、工作区快照、收件箱、桌面搜索、规则/标签门禁、Portal 路径边界、操作账本状态机/存储/恢复、虚拟桌面几何、布局 JSON 契约和后台 STA FIFO 队列。
- 需要真实 Explorer、Shell 扩展、全局输入钩子、多显示器热插拔、Portal 拖放屏障、崩溃中的原生文件操作或回收站 UI 的场景仍保留在 `TEST_CHECKLIST.md` 进行 Windows 实机回归。
- `Directory.Build.props` 仅在 `CI=true` 时将编译警告提升为错误；本地开发仍能查看警告，但 `scripts/ci.ps1` 会显式使用 `-warnaserror` 复现 CI 门禁。
- 发布任务在测试通过后才生成自包含 win-x64 单文件，并同时输出 EXE 与 ZIP 的 SHA-256。

## 保持不变的兼容边界

- `MainWindow.xaml` 的 `x:Class` 和所有事件处理器名称保持不变；仅移除未使用资源和无引用名称。
- 布局模型 `AppLayoutData.Version` 当前为 18；Version 17 及更早文件可继续升级。
- `%AppData%\DesktopOrganizer\layout.json` 可直接继续使用；旧 `SafeMode` 字段会被忽略并在后续保存时移除。
- `%AppData%\DesktopOrganizer\operation-journal.json` 使用独立 Version 1；损坏或不可写时不会用空数据覆盖，并阻止新的真实文件操作。
- 发布脚本、程序名称、开机启动注册项和单实例名称保持不变。
- 桌面右键无闪烁、Windows Terminal 启动层级修复及崩溃恢复逻辑保持不变。


## v1.12.3 安全模式状态边界

- `_isSafeModeActive` 是当前进程的运行时状态，Watcher 后台回调通过 `volatile` 读取。
- `PushReflowEnabled` 与 `AutoClassifyNewItems` 始终表示用户偏好；安全模式只通过有效状态属性暂时屏蔽执行。
- 手动或健康监测进入安全模式都不会调用 `SaveLayout()`。
- 退出安全模式会先恢复 Watcher，再请求一次桌面快照刷新；手动刷新在安全模式下仍然可用。
## v1.12.4 键盘命令边界

- 主窗口继续使用 `WS_EX_NOACTIVATE`，不会为了接收 Ctrl+Z/Esc 改成可激活窗口。
- 键盘钩子只识别 `UndoFileMove` 与 `ClearSelection`；命令可执行时消费对应按键，避免未激活整理层与原前台应用同时处理，其他输入继续传递。
- 鼠标钩子只记录最后一次按钮按下是否命中主整理窗口；任何外部窗口点击都会清除该临时语境。
- 前台切换监听覆盖 Alt+Tab/Win+Tab 等无鼠标路径；普通资源管理器窗口不会因与桌面共用 explorer.exe 而被误判为桌面。
- 钩子回调不执行文件 I/O 或 WPF 更新，只通过 Dispatcher 异步调用统一命令入口。
- 主窗口关闭时先释放输入监听，再移除 HwndSource hook，防止退出后残留回调。



## v1.12.5 显示器与坐标边界

- `NativeMethods.Display.cs` 只负责枚举物理显示器、工作区、设备名和有效 DPI，并按物理像素设置桌面宿主窗口。
- `DesktopGeometry.cs` 保存当前虚拟桌面的统一客户区坐标，提供按点/矩形选择显示器和拓扑等价判断。
- `MainWindow.DesktopGeometry.cs` 负责屏幕像素到 WPF DIP 的转换、Version 12 到 13 迁移、显示器拓扑持久化及运行时重映射。
- 所有持久化坐标均相对虚拟桌面窗口左上角；可用放置区域是每块显示器的 `rcWork` 并集，而不是包围所有显示器的单一矩形。
- 单个 WPF HWND 只能采用一个客户区缩放变换，因此 v1.12.5 保证物理边界、命中和工作区映射正确，并让图标在各屏保持统一视觉大小；未来若要让每块屏幕拥有独立视觉缩放，应改为每显示器一个宿主窗口。

## v1.12.6 Shell 命名空间边界

- 真实文件系统项目仍以用户桌面和公共桌面目录扫描为主；Shell 桌面根虽然会枚举多种系统项目，但 v1.12.13 起只允许回收站 CLSID 进入桌面快照。
- `ShellNamespaceItem.FileSystemPath` 仅用于与两个真实桌面目录去重；项目一旦进入布局模型，统一以 desktop-absolute parsing name 编码保存，绝不把 `::{CLSID}` 传给普通路径 API。
- `DesktopItemKind` 明确区分 `FileSystem` 与 `ShellNamespace`。只有前者可进入真实文件夹移动、回收站删除和文件时间排序；后者只参与虚拟布局。
- Shell 项目的稳定身份是 `ShellParsingName`。显示名称因系统语言、第三方扩展或同名冲突而变化时，批量身份迁移会保留自由坐标、分组顺序与自动分类状态。
- 命名空间枚举在后台 COM apartment 中执行；每个 PIDL、COM 枚举器、桌面文件夹接口和 Shell 返回字符串均在确定的 `finally` 路径释放。
- 默认打开和属性页使用 `ShellExecuteEx` 的 PIDL 模式，不依赖 `Process.Start` 对 parsing name 的非标准解析。第三方扩展不提供动词时只显示错误并写入诊断，不影响刷新与布局。



## v1.12.7 增量视觉边界

- `_freeIconVisuals` 和 `_groupVisuals` 保存当前 Canvas 顶层视觉，刷新时不再整体清空。
- 自由图标状态只包含会改变控件结构的因素；坐标和选择状态通过属性更新，不触发控件重建。
- 分类框内容指纹使用实际排序后的项目顺序，因此名称、类型、创建时间和修改时间排序变化均会重建对应分类框。
- 分组位置不进入内容指纹，拖动分类框只更新 `Canvas.Left/Top`；宽高、折叠、紧凑模式和编辑模式变化才重建分类框。
- 收起分类框的悬停 Peek 只临时显示既有正文并提升视觉层级，不修改 `IsCollapsed`、尺寸、指纹、碰撞占位或工作区快照；关闭后释放已实现图标但保留滚动位置。
- 手动清理图标缓存会增加 `_iconVisualGeneration`，强制所有包含图标的视觉重新提取图标，不会复用旧图像。
- 删除或替换视觉时递归注销 `_allIconVisuals` 与真实文件夹投放目标，避免保留已脱离视觉树的事件目标。
- 每次刷新会验证分类框的完整排序快照；拖拽抑制使面板进入不稳定状态时放弃复用并重建该分类框。
- 该阶段未引入持久化字段；当前总布局版本由后续工作区、组织元数据和 Portal 演进到 Version 18。

## v1.12.8 异步图标边界

- `ShellIconLoadService` 拥有一个低优先级后台 STA 线程，所有 `SHGetFileInfo`、Shell PIDL 图标查询和 HICON 到 `BitmapSource` 的转换均在该线程执行。
- WPF 视觉树只创建固定尺寸占位图，并把 `Image`/占位文字作为弱引用登记；视觉被增量刷新移除后，不会因等待队列而被强引用保留。
- `IconLoadRequestKey` 由大小写不敏感缓存键和图标代次组成。缓存清理会推进代次，旧请求即使无法取消原生调用，其结果也不会写回新视觉。
- 自由图标优先于展开分组，展开分组优先于收起分组；同一缓存键的更高优先级请求可以提升尚未执行的队列项。
- 后台结果必须先冻结，只有冻结的 `BitmapSource` 才会通过 Dispatcher 交给 UI 线程。
- 退出时最多等待后台线程 300 ms；第三方 Shell 扩展阻塞时不延长窗口关闭，线程保持后台状态并在原生调用返回后自行结束。

## v1.12.9 分类框虚拟化边界

- `VirtualizingGroupPanel` 针对本项目固定尺寸图标与固定列数设计，不是通用的可变尺寸 ItemsControl 面板。
- 面板保存完整 `GroupVirtualItem` 快照，但 `_allIconVisuals` 和真实文件夹投放目标只登记当前已实现的可见/缓冲图标；不可见项目仍由布局模型与选择集合管理。
- 视口前保留一行、视口后保留两行，减少滚轮滚动时的控件抖动；滚出缓冲区的控件立即注销并允许垃圾回收。
- `IScrollInfo` 的 Extent、Viewport 和 VerticalOffset 使用 WPF DIP 像素；ScrollViewer 通过 `CanContentScroll=true` 委托滚动。
- 分类图标开始拖动后，对应虚拟索引进入抑制状态，原分组视觉会在下一次增量刷新中判定为不稳定并重建，避免重复容器。
- 分类框滚动位置仅保存在当前进程内，不写入布局；重启后从顶部开始，以避免新增持久化迁移。


## v1.12.10 真实文件操作线程边界

- WPF Dispatcher 只负责用户确认、路径快照、等待状态、操作完成后的布局更新和消息提示；`File.Move`、`Directory.Move` 与回收站调用只在 `FileOperationService` 的 STA 工作线程中执行。
- 所有真实文件任务串行化，避免两个任务同时修改同一路径或互相覆盖撤销历史；UI 通过规范化路径集合标记正在处理的项目。
- 移动任务采用“磁盘成功后提交布局”：失败、取消或目标状态变化时，不删除原布局项。批量回收站任务采用逐项提交，只清理实际成功的项目。
- 任务完成回调返回 Dispatcher 后才修改 `_desktopItems`、`_appLayout`、选择状态和视觉缓存；后台线程不访问 WPF 控件。
- 关闭程序时只取消尚未开始的任务。`File.Move`、`Directory.Move` 或 Windows 回收站内部调用一旦进入系统 API，不能通过线程中止安全取消；工作线程设置为后台线程，退出不会无限等待。
- 文件任务仍由单一 STA 队列串行执行；当前主线在此基础上增加独立写前账本，并从中重建最近 20 次跨会话撤销候选。
