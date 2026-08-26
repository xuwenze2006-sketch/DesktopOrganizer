# 当前已知问题

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
