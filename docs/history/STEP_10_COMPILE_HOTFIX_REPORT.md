# DesktopOrganizer v1.12.11 编译修复报告

## 用户实机编译结果

用户在 Windows x64、.NET 10 发布时报告：

- `MainWindow.DesktopIdentity.cs(207,71)`：CS0136；
- `MainWindow.DesktopIdentity.cs(149,31)`：CS8602；
- `ShellIconLoadService.cs(175,46)`：CS8600；
- `MainWindow.DesktopRefresh.cs(251,67)`：CS8600。

## 修复

1. 将 Shell 命名空间匹配变量改为 `shellMatch`，物理文件匹配变量改为 `fileMatch`，消除同一 `foreach` 作用域内的局部变量名称冲突。
2. `TryCreateWatcherRenameCandidate` 的可空输出在解引用前增加 `candidate is not null`，不以空值抑制运算符掩盖风险。
3. `PriorityQueue.TryDequeue` 使用可空候选并显式判空，符合泛型集合 API 的可空注解。
4. `Dictionary.TryGetValue` 获取自由图标目标时使用可空引用并显式判空。

## 兼容性

- 程序版本升级为 1.12.11；
- 布局格式保持 Version 14；
- 不修改 JSON 字段；
- 不新增第三方依赖；
- v1.12.10 的后台真实文件操作行为不变。

## 建议实机验证

在源码目录运行：

```powershell
dotnet clean
dotnet publish -c Release -r win-x64 --self-contained false
```

预期不再出现上述 CS0136、CS8600、CS8602。
