# DesktopOrganizer v1.12.9 第八阶段报告

## 目标

解决分类框包含数百至上千项目时，原生 `WrapPanel` 一次性创建所有 WPF 图标、标签、右键菜单和事件处理器造成的启动、布局与内存压力。

本阶段只修改运行时视觉与滚动管线，不改变真实文件操作、分类数据、项目身份、多显示器坐标、Shell 项目或布局 JSON。

## 根因

v1.12.8 的分类框在 `CreateGroupVisual` 中遍历完整 `ItemNames`，对每个项目立即调用 `CreateIconVisual`。即使分类框只显示三行，1000 个项目仍会创建 1000 个 `Border`、`StackPanel`、`Image`、`TextBlock`、`ContextMenu` 及事件闭包。

异步 Shell 图标已经避免原生图标提取阻塞 UI，但无法消除这些 WPF 控件本身的创建、Measure/Arrange、命中测试和内存开销。

## 实现

### 1. 固定网格虚拟化面板

新增 `VirtualizingGroupPanel.cs`：

- 实现 `Panel` 与 `IScrollInfo`；
- 分类项目完整保存在轻量 `GroupVirtualItem` 快照中；
- 根据 `VerticalOffset`、`ViewportHeight`、固定行高和固定列数计算可见索引；
- 只创建视口前一行、当前可见行和视口后两行；
- 滚出缓存范围的控件立即从面板移除并注销运行时视觉索引；
- Extent、Viewport 和 Offset 使用 WPF DIP 像素；
- 支持滚轮、逐行、翻页、滚动条拖动与 `MakeVisible`。

默认三列、三行视口的理论实现数量：

- 顶部：约 15 个；
- 中部：约 18 个；
- 底部：最多约 18 个，最后一行不足时更少。

项目总数从 100 增加到 1000 时，可见控件数量基本不变。

### 2. 与增量刷新兼容

v1.12.7 的复用验证原本递归统计分类框视觉树中的所有 `IconTag`。虚拟化后不可见项目本来就不应存在视觉，因此改为：

- `_groupItemPanels` 保存分组 ID 到虚拟化面板的映射；
- 面板保存完整且已排序的项目快照；
- 分类框复用时比较完整快照的显示名称和稳定位置；
- 面板存在拖动抑制状态时标记为不稳定，下一次刷新强制重建。

### 3. 与拖动兼容

分类内图标开始拖动时：

1. `VirtualizingGroupPanel.DetachForDrag` 找到已实现索引；
2. 从面板移除原控件；
3. 将索引加入抑制集合；
4. 原控件提升到顶层 `IconCanvas`；
5. 面板后续 Measure 不会在原位置生成重复图标；
6. 放置、取消或丢失鼠标捕获后，现有增量刷新链路恢复完整稳定状态。

### 4. 与异步 Shell 图标兼容

- 只有已实现的图标才登记异步图标请求；
- 滚出视口后，`Image` 和占位文字只保留在弱引用中，可被回收；
- 再次滚入时优先命中 `_iconCache`，通常无需重复 Shell 调用；
- 收起分类不会创建子图标，也不会提前排队图标任务；
- 图标缓存代次、迟到结果丢弃和 HICON 释放逻辑保持不变。

### 5. 运行时滚动位置

`_groupScrollOffsets` 记录每个分类框当前进程内的垂直偏移。分类框因排序、缩放、紧凑模式或增量内容更新而重建时，会尝试恢复原偏移，并在项目减少后自动约束到合法范围。

该数据不写入布局文件，重启程序后分类框从顶部开始。

## 修改文件

- 新增 `VirtualizingGroupPanel.cs`
- 修改 `MainWindow.GroupVisuals.cs`
- 修改 `MainWindow.DesktopRefresh.cs`
- 修改 `MainWindow.GroupItemActions.cs`
- 修改 `MainWindow.xaml.cs`
- 更新 `DesktopOrganizer.csproj`、`README.md`、`CHANGELOG.md`、`ARCHITECTURE.md`、`TEST_CHECKLIST.md` 和 `OPEN_SOURCE_REFERENCES.md`

## 兼容性

- 程序版本：1.12.9
- 目标框架：`net10.0-windows`
- 布局格式：仍为 Version 14
- 无新增 JSON 字段
- 无新增第三方 NuGet 依赖
- 旧布局无需迁移

## 验证

已完成：

- 39 个 C# 文件的注释、字符串、括号和结构平衡检查；
- XAML、项目文件和 manifest XML 解析；
- 38 个 XAML 事件处理器存在性检查；
- 确认分类框不再创建原生 `WrapPanel`；
- 确认 `ScrollViewer.CanContentScroll=true` 并委托给 `IScrollInfo`；
- 确认分类拖动使用 `DetachForDrag`；
- 确认布局版本保持 14；
- 1、12、1000 项在顶部、中部、底部的可见范围算法模拟；
- 1000 项、三列、三行视口顶部只实现 15 个项目，中部只实现 18 个项目；
- 源码压缩包 CRC 与 SHA-256 完整性检查。

当前执行环境没有 .NET 10 SDK，也不是 Windows WPF/Explorer 桌面会话，因此无法执行真实 `dotnet build`、WPF Measure/Arrange、滚轮和拖动回归。完整实机清单已加入 `TEST_CHECKLIST.md`。

## 参考

- Microsoft WPF `VirtualizingPanel`、`VirtualizingStackPanel`、`IScrollInfo` 与 `ScrollViewer` 文档。
- `sbaeumlisberger/VirtualizingWrapPanel`（MIT）关于视口范围、缓存范围与容器回收的架构方向。

本项目没有引入该库或复制其通用 ItemsControl 实现；当前面板针对本项目固定尺寸图标和手写拖动机制独立实现。

## 仍存在的性能边界

自由桌面图标仍全部常驻 `IconCanvas`。若用户把 1000 个项目全部放在自由桌面而不使用分类框，WPF 控件数量仍然较高。下一阶段可考虑：

- 自由图标按显示器工作区分区；
- 命中检测空间索引；
- 文件移动、回收站删除和撤销操作后台化；
- 将 `MainWindow` 的扫描、布局和文件操作进一步拆为服务。
