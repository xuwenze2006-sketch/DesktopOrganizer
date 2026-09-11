# 源码目录结构

本文件记录仓库的源码目录约定，以及 2026-09 源码整理前后的路径映射。

## 目录约定

- 源码按职责分组存放在 `src/` 子目录中；**所有文件保持同一个 `DesktopOrganizer` 命名空间**，因此目录只表达职责，不改变类型名。
- 应用与测试项目继续使用 SDK 默认通配符递归包含源码，无需在 `.csproj` 中逐个登记文件。
- `DesktopOrganizer.csproj`、`DesktopOrganizer.slnx`、`App.xaml`、`App.xaml.cs` 必须留在仓库根目录：WPF 默认 `ApplicationDefinition` 只在项目根目录生效，把 `App.xaml` 移入子目录会导致不生成入口点 `Main`。
- `Assets/`、`ThirdParty/`、`tests/` 沿用原有结构。

| 目录 | 内容 |
|---|---|
| `src/App/` | 应用启动、`MainWindow` 全部分部类、各个窗口与主题；`app.manifest` 与发布配置也在其中 |
| `src/Desktop/` | 桌面宿主、图标布局与几何、搜索索引与窗口、回收站与剪贴板、`NativeMethods.DesktopView` |
| `src/Layout/` | 工作区布局、布局序列化与恢复、网格与分组放置策略 |
| `src/Pet/` | 桌宠目录、素材库、场景与动画 |
| `src/Inbox/` | 待整理收件箱队列、窗口与标签策略 |
| `src/Rules/` | 整理规则引擎、用户规则与自动分类 |
| `src/Files/` | 文件操作、操作日志与文件夹门户 |
| `src/Persistence/` | 布局持久化、桌面路径解析、启动项、托盘与诊断 |
| `src/ShellInterop/` | `NativeMethods` 系列 P/Invoke 与 Shell 图标/命名空间互操作 |
| `docs/` | 架构、变更日志、实机清单、开源参考与历史报告 |
| `scripts/` | 构建、发布与测试脚本 |

## 整理前后的路径映射

下表列出本次整理涉及的目录级映射；其余文件保持在原位置（`scripts/ci.ps1` 原本就在 `scripts/` 中，未移动）。

| 整理前 | 整理后 |
|---|---|
| `AppDiagnostics.cs`、`MainWindow*.cs`、`*.xaml` 窗口、`WarmPaperTheme.cs`、`Properties/`、`app.manifest` | `src/App/` |
| `MainWindow.Persistence.cs`、`DesktopPathResolver.cs` | `src/Persistence/` |
| `Desktop*.cs`、`IconLayout.cs`、`IconCacheInvalidation.cs`、`GridCellPlacement.cs`、`FreeIconPlacementPlanner.cs`、`VirtualizingGroupPanel.cs`、`WheelScrollPolicy.cs`、`NativeMethods.DesktopView.cs` | `src/Desktop/` |
| `NativeMethods*.cs` | `src/ShellInterop/` |
| `WorkspaceLayoutManager.cs`、`LayoutJsonSerializer.cs`、`LightDesktopLayoutPolicy.cs`、`PushReflowPolicy.cs`、`RefreshCancellationEpoch.cs`、`SmartLayout*.cs`、`CompactGroupGridPlanner.cs`、`Group*Policy.cs`、`GroupLayoutCollisionDetector.cs`、`ControlPanelPlacementPolicy.cs`、`QuietStartupVisibilityPolicy.cs` | `src/Layout/` |
| `DesktopPet*.cs`、`VPetAnimation.cs` | `src/Pet/` |
| `InboxQueueManager.cs`、`ItemTagPolicy.cs`、`AutoCategoryMembershipPlanner.cs`、`AutoClassificationCategoryPolicy.cs`、`InboxWindow.xaml(.cs)` | `src/Inbox/` |
| `OrganizationRuleEngine.cs`、`UserOrganizationRules.cs`、`DesktopCategoryClassifier.cs` | `src/Rules/` |
| `FileOperation*.cs`、`FolderPortal*.cs`、`ShellDesktopItemPolicy.cs` | `src/Files/` |
| `ARCHITECTURE.md`、`CHANGELOG.md`、`TEST_CHECKLIST.md`、`OPEN_SOURCE_REFERENCES.md` | `docs/` |
| `build-and-start.cmd`、`ci-local.cmd`、`publish-win-x64.cmd`、`run-source.cmd`、`run-tests.cmd` | `scripts/` |

## 契约测试的源码定位

单元测试中的源码契约测试需要读取仓库内的 `.cs` 与 `.xaml` 文件。源码目录调整后，这些路径统一由 `tests/DesktopOrganizer.Tests/TestProjectFiles.cs` 的 `SourcePath`/`ReadSource` 解析，并通过 `TestProjectFilesTests` 校验登记的文件真实存在。

**移动源码文件后必须同步该定位表**，否则契约测试会读取到不存在的路径而失败。
