# 当前已知问题

## 排队后的真实文件操作可能作用于同路径替换物

- 现象：移动、回收站删除和移动撤销任务只记录显示名称与路径；后台实际执行前仅检查路径是否存在，不复核排队时项目的稳定文件身份。原项目若在等待期间被外部移走，同路径的新项目会被当成原项目处理。
- 影响：可能移动或删除无关文件；撤销还会把替换物移回桌面，并按旧项目记录恢复布局。
- 证据或复现方式：`PendingPhysicalMove`、`RecycleRequest` 和 `FileMoveUndoRecord` 均没有身份字段（`MainWindow.FileOperations.cs:6-31`、`MainWindow.xaml.cs:212-221`）；移动、回收站和撤销执行路径分别在 `MainWindow.FileOperations.cs:240-280`、`:395-473`、`:573-601` 只按路径检查并操作。先把文件 A 移入文件夹生成撤销记录，再把目标 A 改名并在原目标路径创建另一文件 A，执行撤销时当前代码会把新 A 移回原位置。项目已有 `NativeMethods.TryGetFileIdentity`（`NativeMethods.FileIdentity.cs:48-77`），这些操作未使用它复核对象。
- 涉及区域：后台文件操作队列、真实文件夹移动、回收站删除、移动撤销、布局回填。

## 有效的退出恢复快照可能被误隔离而不再重试

- 现象：启动时已经成功读取并解析 `layout.pending-exit.json` 后，如果覆盖主 `layout.json` 失败，通用异常分支仍会把有效快照改名为 `.invalid`；后续启动不再发现原名恢复文件。
- 影响：退出时已落盘的最后布局快照无法自动恢复，程序继续使用旧主布局，用户最后一批布局操作可能丢失。
- 证据或复现方式：`MainWindow.Persistence.cs:17-20` 先解析 JSON 再覆盖主布局，但 `:23-35` 对所有异常统一改名；让主布局文件在恢复时被独占占用即可使覆盖失败，而有效 pending 文件随后变成 `layout.pending-exit.json.invalid`。这与 `TEST_CHECKLIST.md:328`、`:354` 规定的占用场景恢复契约不符。
- 涉及区域：布局持久化、退出超时快照、启动恢复。

## 错位多屏的空洞区域会选择较远的显示器

- 现象：元素位于两块上下错位显示器之间的空洞时，目标屏按元素中心到“显示器中心”的距离决定，可能不是到元素最近的显示器。
- 影响：图标、分组、控制面板或回收站组件可能跨越数百像素跳到较远屏幕，破坏多显示器拖放位置。
- 证据或复现方式：`DesktopGeometry.cs:46-73` 在没有重叠屏幕时按中心距离排序，`MainWindow.DesktopGeometry.cs:122-145` 随后据此约束位置。主屏为 `(0,0,3840,2160)`、副屏为 `(3840,1500,1280,1024)`、元素矩形为 `(3841,500,90,94)` 时，当前实现选择副屏；该矩形距主屏边界仅 1 像素，距副屏边界 906 像素。`tests/DesktopOrganizer.Tests/DesktopGeometryTests.cs:34-45` 明确要求空洞点返回最近显示器，`TEST_CHECKLIST.md:402-414` 要求正确处理错位屏幕空洞。
- 涉及区域：多显示器几何、图标与分组边界约束、控制面板、回收站组件。

## 网格没有有效空位时会返回已占用或无效网格

- 现象：所有有效网格都被图标、分组或组件占用后，查找空位会把已被过滤的首选格作为默认结果返回；调用方仍把它当作有效空位写入布局。
- 影响：图标会与已有图标重叠、落到分组或组件下面，或被约束到错误位置，并可能持久化该布局。
- 证据或复现方式：`MainWindow.GridLayout.cs:98-119` 过滤占用、分组覆盖和桌面外网格后，用 `FirstOrDefault((preferredColumn, preferredRow))` 处理空结果；`:35-42`、`:60-86` 等调用方没有再次验证。填满全部有效网格后再对齐一个图标即可进入该分支；行为与 `TEST_CHECKLIST.md:52-72`、`:152-156` 的不重叠、避开分组和无空间时保持原排列契约不符。
- 涉及区域：网格吸附、单项与全部对齐、新项目落位、自动分类位置恢复。

## 分类框滚轮忽略 Windows 的整页滚动和禁止滚动设置

- 现象：Windows 设置为“每次滚动一个屏幕”或 0 行时，分类框每个滚轮刻度仍滚动一行。
- 影响：分类框不遵守系统输入与辅助功能偏好，行为与标准 Windows 滚动控件不一致。
- 证据或复现方式：`VirtualizingGroupPanel.cs:293-302` 对 `SystemParameters.WheelScrollLines` 统一执行 `Math.Max(1, ...)`，把按页值和 0 都变成 1；`MainWindow.GroupVisuals.cs:448-461` 的 `ScrollViewer` 直接使用该 `IScrollInfo`。Microsoft 的 [`SystemParameters.WheelScrollLines`](https://learn.microsoft.com/en-us/dotnet/api/system.windows.systemparameters.wheelscrolllines) 映射到 `SPI_GETWHEELSCROLLLINES`；[`SPI_SETWHEELSCROLLLINES`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow) 规定 0 不滚动、`WHEEL_PAGESCROLL` 按页滚动。
- 涉及区域：分类框虚拟化面板、鼠标滚轮、系统辅助功能偏好。

## 外部删除后在同一路径重建项目会继续显示旧图标

- 现象：路径型图标项目被外部删除并完成界面刷新后，在相同名称和路径创建图标不同的新项目，新项目仍命中旧缓存并显示原图标；手动刷新图标缓存后才恢复。
- 影响：整理层显示的图标与当前真实文件不一致，用户可能按旧图标误认项目。
- 证据或复现方式：`MainWindow.DesktopRefresh.cs:506-515` 删除视觉时不清理图标缓存；`MainWindow.GroupVisuals.cs:576-591` 对 `.exe`、`.lnk`、`.url`、`.ico` 等使用路径作为缓存键；`MainWindow.AsyncIcons.cs:57-61` 命中缓存后直接应用旧图像。放置并加载 `A.exe`，从外部删除它并等待图标消失，再把图标不同的程序复制到同一路径并仍命名为 `A.exe`，新视觉会继续显示旧图标。
- 涉及区域：桌面增量刷新、图标缓存失效、异步 Shell 图标、自由图标与分类框视觉。
