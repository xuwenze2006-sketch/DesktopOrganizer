# DesktopOrganizer Git 工作规则

## 适用范围

- 本文件适用于项目根目录及其全部子目录。
- `D:\用户文件\桌面\桌面整理` 是唯一主线目录，`main` 是唯一主线分支。
- 不为本项目建立额外 worktree，不使用 relay、租约或中央集成流程。
- 本文件只规定 Git、验证和交付流程，不记录产品功能设计或问题清单。

## 仓库与基线

- 项目必须处于已有的 Git 工作树中；若 `.git` 缺失，停止修改并报告，不得自行重新初始化。
- 原始源码基线提交必须永久保留，不得通过 amend、rebase、reset 或其他方式改写或删除。
- 首次建立原始源码基线的目的仅是冻结接手时的真实状态。若原始源码验证失败，仍可在记录命令和失败结果后创建这一次基线提交，但不得宣称验证通过；修复必须放在后续独立提交中。
- `bin/`、`obj/`、`release/`、`artifacts/`、测试结果、缓存、日志和用户运行数据必须由 `.gitignore` 排除，不得强制加入 Git。

## 修改前检查

每次修改前必须执行并检查：

```powershell
git rev-parse --show-toplevel
git branch --show-current
git status --short --branch
```

- 根目录必须是本项目主线目录，当前分支必须是 `main`。
- 同一时刻只能有一个 Codex 页面写入本项目。
- 若存在来源不明、属于用户或属于其他任务的修改，立即停止并报告。
- 不得 stash、覆盖、还原或提交不属于当前任务的修改。

## 修改与验证

- 每个提交只处理一个独立、完整的修改目标。
- 修改代码后执行与改动相关的构建和测试。默认完整验证命令为：

```powershell
dotnet build DesktopOrganizer.slnx -c Release -warnaserror
dotnet test tests\DesktopOrganizer.Tests\DesktopOrganizer.Tests.csproj -c Release --no-build
```

- 仅修改文档或 Git 配置时，可以执行对应的静态或专项检查，不要求运行无关的程序测试。
- 验证失败时不得把任务作为已完成提交；应继续修复，或保留当前任务改动并明确报告阻塞与未验证内容。
- 不得为了验证而启动桌面整理程序，除非用户明确要求进行运行时测试。

## 自动提交

- 一个独立修改完成且相关验证通过后，必须立即创建本地小型提交，无需再次询问用户。
- 提交前依次检查：

```powershell
git diff --name-status
git diff --check
git diff --cached --name-status
git diff --cached --check
```

- 只使用明确的路径暂存当前任务文件，例如 `git add -- <path>`；暂存删除时使用明确路径的 `git add -u -- <path>`。
- 禁止使用 `git add -A` 或其他会盲目纳入无关文件的命令。
- 提交前必须检查暂存区内容，确认没有用户文件、生成物或其他任务修改。
- 提交信息使用以下类型和简明中文说明：
  - `feat:` 新功能
  - `fix:` 修复
  - `refactor:` 重构
  - `remove:` 删除功能
  - `docs:` 文档
  - `test:` 测试
  - `chore:` 工程维护

## 历史与回滚

- 禁止使用 `git reset --hard`、`git clean`、覆盖式 checkout、commit amend 或 rebase 改写交付历史。
- 用户要求回滚已提交修改时，优先使用 `git revert <commit>` 创建可追踪的反向提交。
- 未经用户明确授权，不得执行 push、fetch、pull、rebase、强制推送或任何远程仓库操作。

## 交付报告

每次交付只需报告：

- 主线目录；
- 当前分支；
- 本次提交哈希和提交说明；
- 已执行的构建、测试或专项检查及结果；
- 剩余未提交内容；
- 未解决问题或未验证内容。
