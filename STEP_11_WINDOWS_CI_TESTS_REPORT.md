# v1.12.12 Windows CI 与自动化测试报告

## 目标

将此前依赖人工执行的编译验证固化为可重复运行的流水线，使每次修改都能在真实 Windows GitHub Hosted Runner 上完成 .NET 10 WPF 编译、单元测试和 win-x64 发布。

## 新增文件

- `global.json`：以 .NET SDK 10.0.302 为最低基线，并仅允许在 10.0.3xx feature band 内向较新补丁前滚。
- `DesktopOrganizer.slnx`：包含桌面应用和测试项目。
- `Directory.Build.props`：确定性构建；在 `CI=true` 时将编译警告视为错误。
- `.github/workflows/windows-ci.yml`：Windows 2025 构建、测试、覆盖率、发布和 artifact 上传（checkout v6、setup-dotnet v5、upload-artifact v6）。
- `.github/dependabot.yml`：每周检查 NuGet 测试依赖和 GitHub Actions 更新。
- `scripts/ci.ps1`：本地和 GitHub Actions 共用的唯一流水线入口。
- `run-tests.cmd` / `ci-local.cmd`：Windows 双击入口。
- `tests/DesktopOrganizer.Tests`：MSTest 自动化测试项目。

## 自动化测试范围

测试项目当前覆盖：

1. 桌面文件扩展名分类与大小写处理。
2. `.git`、`package.json` 等开发项目目录识别。
3. 多显示器 Canvas 边界、点/矩形屏幕选择、混合 DPI 和拓扑等价判断。
4. Shell parsing name 的 Base64 编解码、类型隔离和等价比较。
5. Layout Version 14 的默认值、显示器拓扑、文件身份和 Shell 身份 JSON 往返。
6. 后台文件任务的 FIFO 顺序、STA apartment、异常传播、队列继续执行和预取消语义。

测试程序集显式使用 `[assembly: DoNotParallelize]`，避免 STA 队列测试被并行调度。

这些测试不启动 `MainWindow`，不会隐藏 Explorer 桌面图标，也不会移动或删除真实用户文件。

## CI 流程

1. 在 `windows-2025` GitHub Hosted Runner 检出源码。
2. 使用 `actions/setup-dotnet@v5` 和 `global.json` 安装 .NET 10 SDK。
3. 还原 `DesktopOrganizer.slnx`。
4. 以 Release 配置编译，所有警告提升为错误。
5. 运行 MSTest，并通过 Coverlet 生成 Cobertura 覆盖率和 TRX。
6. 还原 win-x64 Runtime Pack。
7. 发布自包含、单文件、ReadyToRun、未裁剪的 `DesktopOrganizer.exe`。
8. 生成 ZIP，以及 EXE 和 ZIP 的 SHA-256。
9. 无论测试成功与否都尝试上传测试结果；仅成功发布后上传应用构建产物。

## 依赖边界

桌面应用项目仍无第三方 NuGet 依赖。测试项目新增：

- Microsoft.NET.Test.Sdk 18.8.1
- MSTest.TestAdapter 4.3.2
- MSTest.TestFramework 4.3.2
- coverlet.collector 10.0.1

这些包只参与测试和覆盖率，不会进入发布后的桌面程序。

## 尚需真实 CI 验证的内容

当前执行容器不能解析外网 NuGet，也不是 Windows，因此不能在此处真实执行 WPF 编译和 MSTest。源码已完成 XML/YAML、默认编译项隔离、MSTest 警告规则、引用关系和静态结构验证；将工程推送到 GitHub 后，Windows CI 会成为最终编译验证来源。
