# 程序维护指南

本项目是用户个人使用的 Windows 桌面工具。用户负责提出想要的体验并反馈结果；Codex 负责把需求转为可实现的改动，判断影响、验证和完成本地提交。日常优先可靠、简单、容易恢复，具体协作边界以 [AGENTS.md](../AGENTS.md) 为准。

本指南依据 2026-09-19 的源码梳理。它和 understand 图谱是导航资料，不能替代修改时读取当前代码，也不能证明实际桌面交互已通过验证。

## 程序结构

程序是一个 .NET 10 WPF 应用，测试项目独立。入口 `App.xaml.cs` 负责单实例与异常退出恢复；主窗口是保持顶级窗口身份、不抢焦点的桌面覆盖层，通过 Win32 调整与 Explorer 和其他应用的层级关系。

大部分界面协调代码属于同一个 `MainWindow` 分部类，分散到多个文件；`NativeMethods` 也采用分部类。目录表达职责，不能据此认为状态已经彼此隔离。`_desktopItems` 是已接受的文件快照，`_appLayout` 是持久化状态，图标与分组控件字典属于界面缓存。

| 要处理的需求 | 主要入口 | 关联逻辑与验证入口 |
|---|---|---|
| 启动、桌面层级、系统图标恢复 | `App.xaml.cs`、`MainWindow.DesktopHost.cs` | `NativeMethods.DesktopLayer.cs`、`DesktopIconVisibilityTests`、`DesktopCompanionWindowPolicyTests` |
| 桌面路径迁移、刷新、项目改名 | `MainWindow.DesktopPaths.cs`、`MainWindow.DesktopRefresh.cs`、`MainWindow.DesktopIdentity.cs` | `DesktopPathResolver.cs`、`DesktopPathRefreshTests`、`DesktopRenameSelectionTests` |
| 图标、分组、拖动、多屏布局 | `MainWindow.AsyncIcons.cs`、`GroupVisuals`、`FreeIconDrag`、`DesktopGeometry` 分部 | `ShellIconLoadService.cs`、`VirtualizingGroupPanel.cs`、`src/Layout/` 中的纯逻辑策略与对应测试 |
| 文件移动、回收站、撤销 | `MainWindow.FileOperations.cs`、`MainWindow.OperationJournal.cs`、`MainWindow.RecycleBinWidget.cs` | `FileOperationService.cs`、`FileOperationJournal.cs`、`FileOperationJournalStore.cs`、身份及账本测试 |
| 文件剪贴板 | `MainWindow.Clipboard.cs` | `DesktopFileClipboard.cs`、`NativeMethods.FileClipboard.cs`、`DesktopFileClipboardTests` |
| 布局保存、退出恢复、工作区 | `src/Persistence/MainWindow.Persistence.cs`、`MainWindow.Workspaces.cs` | `PendingExitLayoutRecovery.cs`、`WorkspaceLayoutManager.cs`、布局读保护/后台写入/恢复测试 |
| 规则、收件箱、标签、搜索 | `MainWindow.Rules.cs`、`Inbox`、`Search` 分部 | `src/Rules/`、`src/Inbox/`、`DesktopSearchIndex.cs` 及相应测试 |
| 只读文件夹入口 | `MainWindow.FolderPortals.cs` | `FolderPortalService.cs`、`FolderPortalWatcherCoordinator.cs`、入口服务与快捷键测试 |
| 桌宠、场景、个人素材库 | `MainWindow.DesktopPet.cs`、`DesktopPetPicker.xaml.cs` | `src/Pet/`、`DesktopPetLibraryTests`、`DesktopPetTests` |

除单独注明者外，`MainWindow.*` 位于 `src/App/`。完整路径分类见 [SOURCE_LAYOUT.md](SOURCE_LAYOUT.md)，详细设计见 [ARCHITECTURE.md](ARCHITECTURE.md)。测试名称只表示定位入口，不代表某一轮已经执行。

## 需要一直掌握的流程

### 启动与桌面刷新

启动先恢复异常退出时的系统图标状态，再读取布局、核验账本、初始化组件、完成首次扫描并绑定监听。只有桌面快照与宿主都可靠后才隐藏 Explorer 图标。关闭时停止监听和后台任务，保存退出布局并恢复系统图标。

每次扫描重新查询 Windows 的用户桌面和公共桌面位置。两处目录都完整读取才接受新快照；失败保留旧布局，不把不可访问当作空目录。后台扫描结果回到 UI 线程后还要检查路径版本、关闭状态和正在进行的交互，再协调身份、收件箱、规则与视觉。当前普通快照只包含真实文件，回收站是独立组件。

图标提取在后台 STA 线程完成，冻结位图后回填 UI；请求同时有全局代次与单键缓存版本，过期结果必须丢弃。分组按可见行虚拟化，已有视觉尽量复用。多屏坐标采用同一个 WPF 客户区，放置范围使用各显示器工作区，不能把屏幕之间的空隙当成可用桌面。

### 虚拟整理与真实文件操作

虚拟归组、规则应用、标签、搜索和工作区切换主要修改应用布局，不移动用户文件。拖放在最终鼠标位置重新命中目标，再分流到虚拟布局、真实文件夹或回收站；Portal 会阻止写入型拖放。

应用内移入真实文件夹的典型顺序是：捕获文件身份及布局恢复快照 → 预留相关路径 → `Queued` 落盘 → 后台 STA 排队 → `Running` 落盘 → 再核验身份与目标占用 → 真实移动 → 终态落盘 → UI 回填布局。回收站及撤销同样接入写前协调，但恢复证据与可撤销条件按操作类型判断。

磁盘成功与账本成功是两个结果：文件已移动而终态保存失败时，要如实更新界面并进入账本保护态，不能虚构完整撤销能力。启动只核验遗留操作，不自动重做用户文件操作；核验结果会保存到应用账本，已证实完成的撤销可恢复布局。

文件剪贴板是独立路径：复制与资源管理器兼容；粘贴经预留和 STA 队列交给 Windows Shell，系统处理复制/移动及冲突提示。它不进入应用账本与跨会话撤销历史，不能套用上面的恢复保证。

### 持久化与工作区

用户数据在 `%AppData%\DesktopOrganizer`，与仓库构建输出分开。当前 `layout.json` 根模型版本为 **19**，命名工作区视觉快照版本为 **3**；版本号是不同契约，不可混用。独立文件操作账本保存在 `operation-journal.json`。

布局先在 UI 线程捕获序列化快照，再通过后台写锁合并未开始的写入，临时文件完成后原子替换。退出最多等待写锁两秒，超时保存 `layout.pending-exit.json`。恢复比较保存代次和内容，同代冲突或主文件不可安全读取时保留证据；读取失败不能以空数据覆盖原文件。

工作区只捕获指定视觉字段；标签、规则和收件箱属于根布局共享状态，桌宠配置也是全局状态。工作区切换不能描述为文件集合备份，也不会切换整套桌宠配置。

### 规则、入口与桌宠

规则需要依次经过草稿、确认预览、成功执行一次、启用自动应用。手工执行一次前重新计算并比较已确认的计划；自动应用每次根据当前桌面构建计划。执行异常会恢复当前规则造成的布局变化；自动应用失败时停用该规则，不自动重试。首次完整扫描只给收件箱建立基线，可靠建议也必须尊重手工归组。

Portal 是只读目录查看器：复核根身份，拒绝越界与重解析点导航，只枚举当前目录直属项且最多 500 项。异步结果要核对代次，失败保留上次成功内容并显示过期；监听不递归。

个人桌宠素材库位于 `%AppData%\DesktopOrganizer\PetLibrary`。导入通过 STA 队列进行复制与解码验证，暂存完整后提交目录和索引，索引成功落盘后发布不可变快照；失败只回收本次自有副本，不删除导入源文件。角色目录、场景和动画按快照缓存，桌宠用单计时器绘制背景、角色和前景，并按透明像素命中。测试必须使用隔离素材库。

## 改动与验证

开始任务时先核对主线和 Git 状态，再按需求读取上表入口、相关状态模型和测试。涉及分部类共享状态时检查对应写入和调用点，不仅检查当前文件。根目录 WPF 入口与源码位置约定不能随意移动。

代码验证使用 AGENTS.md 中的 Release 构建及测试命令；文档和图谱维护做路径、结构、引用及源码事实核对。桌面层级、系统快捷键、Shell 操作、多屏热插拔及可见交互仍需用户授权后按 [TEST_CHECKLIST.md](TEST_CHECKLIST.md) 实机验证。不能用静态图谱或测试数量替代这些证据。

`KNOWN_ISSUES.md` 只按现有协作规则维护；本次全局理解不等同于全项目未解决问题审计。

## understand 图谱

本地数据位于 `.ua/`，由 Git 忽略。图谱保留源码、测试、工程配置、当前说明和素材清单的文本关系，排除生成物、运行数据、二进制素材及历史报告；后者仍保留在原目录。输出为中文，自动更新关闭。

使用 `understand-dashboard` 可查看本地图谱。后续只在需要时用 `understand` 更新；先检查 `meta.json` 的提交基线和当前差异。C# 同命名空间及分部类不一定产生 import 边，本图另外保留实际调用与明确依赖，不能把缺少 import 边解释为模块无联系。方法级未做语义复核的节点标明仅有结构定位；新增行为仍须读源码和验证。
