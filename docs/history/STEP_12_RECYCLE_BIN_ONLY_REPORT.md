# DesktopOrganizer v1.12.13：Shell 桌面项目仅保留回收站

## 用户需求

整理层接管 Explorer 原生桌面后，只保留回收站这一项 Windows Shell 虚拟项目；此电脑、网络、控制面板、用户文件、Linux、库及第三方命名空间项目均不显示。真实桌面文件、文件夹和快捷方式不受影响。

## 实现

新增 `ShellDesktopItemPolicy.cs`，以回收站稳定 CLSID `645FF040-5081-101B-9F08-00AA002F954E` 作为唯一白名单。判断会扫描 desktop-absolute parsing name 中的 GUID，因此兼容 `::{CLSID}`、`shell:::{CLSID}`、大小写变化和带上级 Shell 路径的形式，不依赖“回收站”这一可本地化显示名称。

`ScanDesktopItems` 仍先扫描用户桌面和公共桌面目录，再枚举 Shell 桌面根；但 Shell 项目只有通过白名单后才进入快照。其余项目不会进入分类器、身份表、视觉树或异步图标队列。

## 旧布局清理

布局格式保持 Version 14。升级后首次扫描会把已过滤项目视为不存在，并复用现有流程完成：

1. 从手工/自动分类的 `ItemNames` 中移除；
2. 清除对应自由图标坐标；
3. 移除增量视觉和图标映射；
4. 以当前快照替换 `ItemIdentities`；
5. 自动删除因此变空的自动分类框。

真实文件和文件夹使用独立文件系统扫描，不会被 Shell 白名单影响。

## 自动化测试

新增 `ShellDesktopItemPolicyTests`，覆盖：

- 回收站 CLSID 的标准、大小写、`shell:::` 和多级 parsing name；
- 不接受中文显示名称作为身份；
- 拒绝此电脑、网络、控制面板和 Linux 等常见 CLSID；
- `ShellNamespaceItem` 白名单入口只允许回收站。

## 兼容性

- 应用版本：1.12.13；
- 布局版本：14；
- 无新增 JSON 字段；
- 无新增运行时 NuGet 依赖；
- Windows CI 和既有 MSTest 流程保持不变。

## 本环境限制

当前执行环境没有可用的 .NET 10 SDK，也不是 Windows Explorer/WPF 桌面会话，因此无法在此处实际运行 `dotnet build` 或枚举真实桌面 Shell。已完成源码结构、XML、测试入口、差异和压缩包完整性检查；Windows 实机回归步骤已更新到 `TEST_CHECKLIST.md`。
