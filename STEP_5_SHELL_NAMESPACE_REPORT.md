# DesktopOrganizer v1.12.6 第五步修复报告

## 目标

补回 Explorer 原生桌面被隐藏后消失的 Shell 命名空间项目，包括回收站、此电脑、网络、用户文件以及可见的第三方桌面扩展，并让这些项目安全参与现有布局系统。

## 实现摘要

### 1. 桌面根命名空间枚举

新增 `NativeMethods.ShellNamespace.cs`，通过 `SHGetDesktopFolder` 获取桌面根 `IShellFolder`，使用 `EnumObjects`/`IEnumIDList` 枚举可见文件夹与非文件夹。项目读取 `SFGAO_FOLDER`、`SFGAO_FILESYSTEM`、`SFGAO_HIDDEN` 和 `SFGAO_NONENUMERATED`，只排除隐藏或明确不应枚举的项目。

与早期“过滤所有文件系统项目”的方案不同，本版本保留带文件系统支持的 Shell 特殊项（例如用户文件），再通过 `SIGDN_FILESYSPATH` 与用户桌面/公共桌面实际路径集合去重。公共桌面同名项目即使被用户桌面显示项覆盖，其路径仍进入去重集合。

### 2. 稳定身份与布局 Version 14

新增 `DesktopItemKind` 与 Shell parsing name 身份。Shell location 使用 Base64 封装和 `shell-namespace:` 前缀，与真实路径形成不可混淆的类型边界。布局升级为 Version 14；旧布局不含新字段时按真实文件系统项目解释。

同一 Shell 项目的显示名称因系统语言、扩展更新或同名冲突变化时，通过 desktop-absolute parsing name 批量迁移自由坐标、分组顺序、自动分类恢复坐标和选择状态。

### 3. 图标、打开与属性

Shell 图标通过 `SHParseDisplayName` 得到 PIDL，再使用 `SHGetFileInfo` 的 PIDL 模式读取。双击打开和右键“属性”通过 `ShellExecuteEx` 的 `SEE_MASK_INVOKEIDLIST` 模式执行，不把 `::{CLSID}` 当作普通文件路径。

所有 Shell 返回字符串、枚举 PIDL、解析 PIDL、克隆 PIDL、图标句柄和 COM 对象均有明确释放路径。

### 4. 文件操作隔离

系统项目支持：自由拖动、网格吸附、挤压排列、分组、跨组移动、排序、多选、自动分类和布局持久化。

系统项目不会触发真实文件夹的绿色投放目标；松手只调整布局或分类，不执行磁盘移动。系统项目同样禁止删除到回收站、读取文件时间和进入真实文件移动撤销；混合多选删除只筛选真实项目。

### 5. 关联缺陷修复

异常输入恢复此前使用 `Path.GetFileName(iconTag.FullPath)` 查找挤压预览快照。Shell location 不是路径，现统一改用 `IconTag.DisplayName`，避免切换显示器、安全模式或丢失鼠标捕获时无法恢复系统项目位置。

## 静态验证

- 36 个 C# 文件通过字符串、字符、注释及括号词法平衡检查；
- 5 个 XAML、项目及 manifest XML 文件通过解析；
- 37 个 XAML 事件绑定均能找到处理器；
- 17 个资源定义和 14 个资源引用通过检查；
- Shell parsing name 编解码、无效输入及同名去重模拟通过；
- PIDL、COM、图标句柄释放路径源码断言通过；
- 真实文件 API 前的 Shell 类型隔离检查通过；
- Version 14 与旧布局兼容字段检查通过；
- ZIP CRC 和 SHA-256 完整性检查通过。

## 环境限制

当前容器不是 Windows Explorer/WPF 会话，无法枚举真实 Shell 桌面或执行 PIDL 动词。容器也未预装 .NET 10 SDK；尝试获取官方 SDK 时运行环境的直接网络解析/压缩包策略阻止安装，因此没有声称完成 `dotnet build`。Windows 实机编译和交互回归步骤已加入 `TEST_CHECKLIST.md`。
