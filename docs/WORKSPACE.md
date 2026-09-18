# 开发工作区维护

本程序仅供个人使用，维护以方便日常使用、查找文件和恢复备份为准。

主线目录为 `D:\用户文件\桌面\桌面整理`，分支为 `main`。源码分层和测试路径映射见 [SOURCE_LAYOUT.md](SOURCE_LAYOUT.md)。

## 目录用途

| 路径 | 用途与维护方式 |
|---|---|
| `src/`、`Assets/`、`ThirdParty/` | 程序源码、内置素材和第三方许可，纳入 Git |
| `tests/` | 测试源码，内部 `bin/`、`obj/` 是可再生成的缓存 |
| `scripts/` | 构建、验证、发布和工作区维护入口 |
| `docs/`、`docs/history/` | 当前说明与历史交付记录，保留历史 |
| 根目录的项目文件、`App.xaml`、`App.xaml.cs` | WPF 项目入口，保留原位置 |
| 根目录及测试项目中的 `bin/`、`obj/` | 常规编译输出与中间文件，可清理 |
| `artifacts/build/` | 新增临时验证统一使用的独立构建目录；只清理其中的 `bin/`、`obj/` |
| `artifacts/test-results/`、其他 `*-test-results/` | 测试结果和覆盖率证据，日常缓存清理保留；CI 会更新自己的 `test-results/` |
| `artifacts/publish/`、`artifacts/` 根目录中的发布 ZIP 与 SHA-256 文件 | CI 发布产物；CI 会更新指定平台产物，日常缓存清理保留 |
| `release/` | 本地发布及历史预览程序，可能正被使用，保留路径与内容 |
| `artifacts/` 中的其余内容 | 素材源文件、截图、实验记录、备份与交付文件，不能整体删除 |
| `.git/` | 原始基线及提交历史，不手动整理 |

表中的构建产物和 `artifacts/` 本地资料由 `.gitignore` 排除。被 Git 忽略只表示不提交，**不表示可以删除**。用户运行数据位于 `%AppData%\DesktopOrganizer`，不属于仓库清理范围。

## 日常清理

先停止本项目的编译和测试；若程序从待清理的 `bin/` 运行，先正常退出。运行于 `release/` 的程序不受影响。

```powershell
# 只预览路径、文件数与大小
.\scripts\clean-workspace.ps1

# 将明确列出的构建缓存移入回收站
.\scripts\clean-workspace.ps1 -Apply
```

脚本只处理目录表中的常规缓存及 `artifacts/build/bin`、`artifacts/build/obj`，不遍历查找所有名为 `bin` 或 `obj` 的目录。移入回收站前检查主线分支、目录边界、Git 跟踪与忽略状态、链接或目录联接，以及从目标目录运行的进程。也可使用 `-Apply -WhatIf` 查看操作。

下一次构建会重新生成缓存；回收站中的文件仍占磁盘空间，可恢复，需要释放磁盘空间时由本人清空。不要为了日常清理执行发布流程，也不要清空整个 `artifacts/` 或 `release/`。

## 临时验证输出

普通开发按协作规则执行默认 Release 构建和测试。确需独立输出时，统一使用同一个位置，构建和测试参数保持一致：

```powershell
dotnet build DesktopOrganizer.slnx -c Release -warnaserror --artifacts-path artifacts/build
dotnet test tests\DesktopOrganizer.Tests\DesktopOrganizer.Tests.csproj -c Release --no-build --artifacts-path artifacts/build
```

新的素材工作文件放在 `artifacts/artwork/<主题>/`，截图和手工验证证据放在 `artifacts/verification/<日期-主题>/`，个人备份放在 `artifacts/backups/`。需要可重复发布的素材仍通过项目现有流程进入 `Assets/` 并保留许可。

既有 `desktop-pet/`、`scene-*`、`pink-*`、`sunset-*`、`runtime-acceptance-*` 等目录保留原路径，便于继续使用已有素材脚本及历史引用。历史构建目录在确认只含可重建的 `bin/`、`obj/` 后单独清理，不把通配符删除写入日常维护脚本。
