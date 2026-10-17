# 安全模式基础桌面同步实现计划

> **For agentic workers:** 使用 superpowers:executing-plans 在唯一 main 工作树内执行；子代理只读评审。

**Goal:** 安全模式仍可显示真实桌面的增删改，并持续提示降级状态，避免单次延迟长期停更。
**Architecture:** 复用现有 15 秒巡检与单刷新 worker；正常模式维持目录监听。独立状态按钮不受操作反馈覆盖。健康检测保留资源临界保护，界面延迟按连续三次警告判定。
**Tech Stack:** .NET 10、WPF、MSTest。
**Spec:** 本聊天中 2026-10-17 已批准的四项修复方案。

## 约束与决策

- 唯一写入目录为本项目、分支 main；不启动应用、不触碰真实桌面文件或布局、不推送远程。
- 安全模式继续停用 watcher、自动归类、规则与挤压。用户偏好保持不变。
- 巡检使用 RequestDesktopRefresh 合并请求；关闭或生命周期取消后不排队。
- 安全模式拖动期间等待下一轮巡检，不产生 850ms 重复扫描循环。
- 单次延迟仅记录；连续三次健康警告才降级。工作集达到 1200 MiB 或句柄达到 12000 仍立即保护。

## 实现与验收

- [x] 在 SafeModeTests 中先覆盖新增/改名/删除、目录恢复、交互暂缓、关闭/取消、普通模式监听、暂停整理、偏好保护、告警恢复入口与健康判定；测试目录和日志隔离。
- [x] 扩展 DesktopPathRefreshTests.Fixture 提供巡检、刷新空闲等待与反射调用；运行定向测试确认旧实现失败。
- [x] 在 MainWindow.DesktopPaths.cs 增加基础同步巡检入口，由 DesktopRefresh.cs 的原 15 秒定时器调用；复用原刷新和取消链路。
- [x] 在 MainWindow.xaml / Settings.cs 增加独立常驻状态按钮与恢复入口；切换时更新提示详情。
- [x] Settings.cs 分离健康样本判定，AppDiagnostics.cs 的临界状态只包含资源阈值，连续警告逻辑保持三次。
- [x] 同步 README 与实机测试清单中已改变的安全模式行为。
- [x] 定向测试通过后执行 Release warnaserror 构建、完整测试与只读评审；交付前检查 diff 并提交本地 main。

## 验证记录

- 初始 11 项回归测试在旧实现下失败；补充的菜单扫描时序、无变化巡检详情、底部面板边界 3 项测试也先复现了问题，再修复通过。
- 安全模式、桌面路径与控制栏相关测试：41/41 通过。
- `dotnet build DesktopOrganizer.slnx -c Release -warnaserror`：0 警告、0 错误。
- `dotnet test tests\DesktopOrganizer.Tests\DesktopOrganizer.Tests.csproj -c Release --no-build`：1399/1399 通过，无跳过。
- 只读评审提出的三项边界问题均已补测修复，复核未发现剩余问题。

## 验证边界

自动测试只操作隔离临时目录和未显示的窗口，不证明真实桌面、鼠标交互与当前发布包已更新；这些需另行授权运行验证。
