// 桌面项目稳定身份与重命名布局迁移
namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private sealed record DesktopItemRenameCandidate(
            string OldName,
            string NewName,
            string OldFullPath,
            string NewFullPath,
            string Source);

        private static DesktopItemIdentityInfo CreateDesktopItemIdentity(string fullPath)
        {
            string normalizedPath = NormalizePersistedPath(fullPath);
            bool isDirectory = false;
            long? creationTimeUtcTicks = null;

            try
            {
                FileAttributes attributes = File.GetAttributes(fullPath);
                isDirectory = attributes.HasFlag(FileAttributes.Directory);
                DateTime creationTimeUtc = File.GetCreationTimeUtc(fullPath);
                if (creationTimeUtc > DateTime.UnixEpoch)
                {
                    creationTimeUtcTicks = creationTimeUtc.Ticks;
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // Shell 可能正在替换文件；FileId 仍可独立尝试，下一次刷新会补齐元数据。
            }

            _ = NativeMethods.TryGetFileIdentity(fullPath, out string? fileId);
            return new DesktopItemIdentityInfo
            {
                Kind = DesktopItemKind.FileSystem,
                LastKnownPath = normalizedPath,
                FileId = fileId,
                CreationTimeUtcTicks = creationTimeUtcTicks,
                IsDirectory = isDirectory
            };
        }


        private static DesktopItemIdentityInfo CreateShellDesktopItemIdentity(
            string encodedLocation,
            string parsingName,
            bool isFolder)
        {
            return new DesktopItemIdentityInfo
            {
                Kind = DesktopItemKind.ShellNamespace,
                LastKnownPath = encodedLocation,
                ShellParsingName = parsingName,
                IsDirectory = isFolder
            };
        }

        private static string NormalizePersistedPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string trimmed = path.Trim();
            if (ShellItemLocation.TryDecode(trimmed, out _, out _))
            {
                return trimmed;
            }

            try
            {
                return Path.GetFullPath(trimmed);
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return trimmed;
            }
        }

        private static bool PathsEqual(string? first, string? second)
        {
            string normalizedFirst = NormalizePersistedPath(first);
            string normalizedSecond = NormalizePersistedPath(second);
            return ShellItemLocation.AreEquivalent(normalizedFirst, normalizedSecond);
        }

        private void QueueDesktopRename(string? oldFullPath, string? newFullPath)
        {
            string oldPath = NormalizePersistedPath(oldFullPath);
            string newPath = NormalizePersistedPath(newFullPath);
            if (string.IsNullOrWhiteSpace(oldPath) ||
                string.IsNullOrWhiteSpace(newPath) ||
                string.Equals(oldPath, newPath, StringComparison.Ordinal))
            {
                return;
            }

            lock (_desktopRenameLock)
            {
                // 合并同一防抖窗口内的连续重命名：A -> B -> C 最终记录为 A -> C。
                int chainedIndex = _pendingDesktopRenames.FindLastIndex(operation =>
                    PathsEqual(operation.NewFullPath, oldPath));
                if (chainedIndex >= 0)
                {
                    DesktopRenameOperation chained = _pendingDesktopRenames[chainedIndex];
                    _pendingDesktopRenames[chainedIndex] = chained with { NewFullPath = newPath };
                }
                else
                {
                    _pendingDesktopRenames.Add(new DesktopRenameOperation(oldPath, newPath));
                }

                // FileSystemWatcher 缓冲区异常时仍有 FileId 对账兜底，不允许事件队列无限增长。
                if (_pendingDesktopRenames.Count > 64)
                {
                    _pendingDesktopRenames.RemoveRange(0, _pendingDesktopRenames.Count - 64);
                }
            }
        }

        private List<DesktopRenameOperation> DrainDesktopRenames()
        {
            lock (_desktopRenameLock)
            {
                if (_pendingDesktopRenames.Count == 0)
                {
                    return new List<DesktopRenameOperation>();
                }

                var operations = new List<DesktopRenameOperation>(_pendingDesktopRenames);
                _pendingDesktopRenames.Clear();
                return operations;
            }
        }

        private bool ReconcileDesktopItemIdentities(DesktopScanSnapshot snapshot)
        {
            var renameMap = new Dictionary<string, DesktopItemRenameCandidate>(StringComparer.OrdinalIgnoreCase);

            foreach (DesktopRenameOperation operation in DrainDesktopRenames())
            {
                if (TryCreateWatcherRenameCandidate(
                        operation,
                        snapshot,
                        out DesktopItemRenameCandidate? candidate) &&
                    candidate is not null)
                {
                    renameMap[candidate.OldName] = candidate;
                }
            }

            // Shell 虚拟项目的显示名称会随系统语言、命名空间扩展或冲突消解变化。
            // parsing name 是稳定身份，因此用它迁移布局，而不是把显示名称当作永久主键。
            Dictionary<string, List<KeyValuePair<string, DesktopItemIdentityInfo>>> nextByShellParsingName = snapshot.Identities
                .Where(pair => pair.Value.Kind == DesktopItemKind.ShellNamespace &&
                               !string.IsNullOrWhiteSpace(pair.Value.ShellParsingName))
                .GroupBy(pair => pair.Value.ShellParsingName!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

            // Watcher 在程序未运行、缓冲区溢出或文件系统不完整上报时可能没有事件。
            // 使用上次保存的卷序列号 + 文件 ID 与本次扫描结果进行二次对账。
            Dictionary<string, List<KeyValuePair<string, DesktopItemIdentityInfo>>> nextByFileId = snapshot.Identities
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.FileId))
                .GroupBy(pair => pair.Value.FileId!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach ((string oldName, DesktopItemIdentityInfo oldIdentity) in _appLayout.ItemIdentities)
            {
                if (renameMap.ContainsKey(oldName))
                {
                    continue;
                }

                if (oldIdentity.Kind == DesktopItemKind.ShellNamespace)
                {
                    if (TryGetActualEntry(snapshot.Identities, oldName, out string? shellActualSameName,
                            out DesktopItemIdentityInfo? shellSameNameIdentity) &&
                        ShellIdentityMatches(oldIdentity, shellSameNameIdentity))
                    {
                        if (!string.Equals(oldName, shellActualSameName, StringComparison.Ordinal))
                        {
                            renameMap[oldName] = new DesktopItemRenameCandidate(
                                oldName,
                                shellActualSameName,
                                oldIdentity.LastKnownPath,
                                shellSameNameIdentity.LastKnownPath,
                                "shell-case");
                        }
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(oldIdentity.ShellParsingName) ||
                        !nextByShellParsingName.TryGetValue(
                            oldIdentity.ShellParsingName,
                            out List<KeyValuePair<string, DesktopItemIdentityInfo>>? shellCandidates))
                    {
                        continue;
                    }

                    List<KeyValuePair<string, DesktopItemIdentityInfo>> shellMatches = shellCandidates
                        .Where(candidate => ShellIdentityMatches(oldIdentity, candidate.Value))
                        .ToList();
                    if (shellMatches.Count == 1 &&
                        !string.Equals(oldName, shellMatches[0].Key, StringComparison.Ordinal))
                    {
                        KeyValuePair<string, DesktopItemIdentityInfo> shellMatch = shellMatches[0];
                        renameMap[oldName] = new DesktopItemRenameCandidate(
                            oldName,
                            shellMatch.Key,
                            oldIdentity.LastKnownPath,
                            shellMatch.Value.LastKnownPath,
                            "shell-parsing-name");
                    }
                    continue;
                }

                if (TryGetActualEntry(snapshot.Identities, oldName, out string? actualSameName,
                        out DesktopItemIdentityInfo? sameNameIdentity) &&
                    PhysicalIdentityMatches(oldIdentity, sameNameIdentity))
                {
                    if (!string.Equals(oldName, actualSameName, StringComparison.Ordinal))
                    {
                        renameMap[oldName] = new DesktopItemRenameCandidate(
                            oldName,
                            actualSameName,
                            oldIdentity.LastKnownPath,
                            sameNameIdentity.LastKnownPath,
                            "file-id-case");
                    }
                    continue;
                }

                if (string.IsNullOrWhiteSpace(oldIdentity.FileId) ||
                    !nextByFileId.TryGetValue(oldIdentity.FileId, out List<KeyValuePair<string, DesktopItemIdentityInfo>>? candidates))
                {
                    continue;
                }

                List<KeyValuePair<string, DesktopItemIdentityInfo>> matchingCandidates = candidates
                    .Where(candidate => PhysicalIdentityMatches(oldIdentity, candidate.Value))
                    .ToList();

                // 硬链接等场景可能让多个名称共享文件 ID；存在歧义时不自动迁移。
                if (matchingCandidates.Count != 1)
                {
                    continue;
                }

                KeyValuePair<string, DesktopItemIdentityInfo> fileMatch = matchingCandidates[0];
                if (!string.Equals(oldName, fileMatch.Key, StringComparison.Ordinal))
                {
                    renameMap[oldName] = new DesktopItemRenameCandidate(
                        oldName,
                        fileMatch.Key,
                        oldIdentity.LastKnownPath,
                        fileMatch.Value.LastKnownPath,
                        "file-id");
                }
            }

            // 两个旧项目不应同时迁移到同一名称；这种情况通常来自硬链接或不完整事件。
            HashSet<string> ambiguousTargets = renameMap.Values
                .GroupBy(candidate => candidate.NewName, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Select(candidate => candidate.OldName)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string oldName in renameMap
                         .Where(pair => ambiguousTargets.Contains(pair.Value.NewName))
                         .Select(pair => pair.Key)
                         .ToList())
            {
                renameMap.Remove(oldName);
            }

            bool changed = ApplyDesktopRenameBatch(renameMap.Values.ToList());
            changed |= ReplacePersistedDesktopIdentities(snapshot.Identities);
            return changed;
        }

        private bool TryCreateWatcherRenameCandidate(
            DesktopRenameOperation operation,
            DesktopScanSnapshot snapshot,
            out DesktopItemRenameCandidate? candidate)
        {
            candidate = null;
            string oldName = Path.GetFileName(operation.OldFullPath);
            string newName = Path.GetFileName(operation.NewFullPath);
            if (string.IsNullOrWhiteSpace(oldName) ||
                string.IsNullOrWhiteSpace(newName) ||
                oldName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) ||
                newName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) ||
                !snapshot.Items.TryGetValue(newName, out string? scannedNewPath) ||
                !PathsEqual(scannedNewPath, operation.NewFullPath))
            {
                return false;
            }

            bool oldPathKnown =
                (_desktopItems.TryGetValue(oldName, out string? currentOldPath) &&
                 PathsEqual(currentOldPath, operation.OldFullPath)) ||
                (_appLayout.ItemIdentities.TryGetValue(oldName, out DesktopItemIdentityInfo? storedIdentity) &&
                 PathsEqual(storedIdentity.LastKnownPath, operation.OldFullPath));

            if (!oldPathKnown)
            {
                return false;
            }

            candidate = new DesktopItemRenameCandidate(
                oldName,
                newName,
                operation.OldFullPath,
                operation.NewFullPath,
                "watcher");
            return true;
        }

        private bool ApplyDesktopRenameBatch(IReadOnlyCollection<DesktopItemRenameCandidate> renames)
        {
            if (renames.Count == 0)
            {
                return false;
            }

            var renameMap = renames.ToDictionary(
                candidate => candidate.OldName,
                candidate => candidate,
                StringComparer.OrdinalIgnoreCase);
            var targetNames = renames
                .Select(candidate => candidate.NewName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            TransformDictionaryKeys(_appLayout.FreeIcons, renameMap, targetNames);
            TransformDictionaryKeys(_appLayout.AutoClassificationOriginalPositions, renameMap, targetNames);
            TransformDictionaryKeys(_appLayout.ItemIdentities, renameMap, targetNames);
            if (_pushPreviewOriginalPositions != null)
            {
                TransformDictionaryKeys(_pushPreviewOriginalPositions, renameMap, targetNames);
            }
            if (_pushPreviewPositions != null)
            {
                TransformDictionaryKeys(_pushPreviewPositions, renameMap, targetNames);
            }

            foreach (GroupInfo group in _appLayout.Groups)
            {
                group.ItemNames = TransformNameList(group.ItemNames, renameMap, targetNames);
            }

            List<string> selectedNames = TransformNameList(
                _selectedItemNames.ToList(),
                renameMap,
                targetNames);
            _selectedItemNames.Clear();
            foreach (string selectedName in selectedNames)
            {
                _selectedItemNames.Add(selectedName);
            }

            _pushPreviewDraggedName = TransformSingleName(_pushPreviewDraggedName, renameMap, targetNames);
            _pushPreviewTargetName = TransformSingleName(_pushPreviewTargetName, renameMap, targetNames);
            UpdateFileMoveHistoryAfterRenames(renames, renameMap, targetNames);

            foreach (DesktopItemRenameCandidate rename in renames)
            {
                _diagnostics.Log(
                    $"LAYOUT_RENAME source={rename.Source}, old={rename.OldName}, new={rename.NewName}, oldPath={rename.OldFullPath}, newPath={rename.NewFullPath}");
            }

            return true;
        }

        private static void TransformDictionaryKeys<T>(
            Dictionary<string, T> dictionary,
            IReadOnlyDictionary<string, DesktopItemRenameCandidate> renameMap,
            IReadOnlySet<string> targetNames)
        {
            List<KeyValuePair<string, T>> entries = dictionary.ToList();
            dictionary.Clear();

            // 先写入被迁移项目，确保旧目标名称的过期状态不能覆盖新状态。
            foreach ((string name, T value) in entries)
            {
                if (renameMap.TryGetValue(name, out DesktopItemRenameCandidate? rename))
                {
                    dictionary[rename.NewName] = value;
                }
            }

            foreach ((string name, T value) in entries)
            {
                if (renameMap.ContainsKey(name) || targetNames.Contains(name) || dictionary.ContainsKey(name))
                {
                    continue;
                }

                dictionary[name] = value;
            }
        }

        private static List<string> TransformNameList(
            IEnumerable<string> names,
            IReadOnlyDictionary<string, DesktopItemRenameCandidate> renameMap,
            IReadOnlySet<string> targetNames)
        {
            var transformed = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
            {
                string? transformedName = TransformSingleName(name, renameMap, targetNames);
                if (!string.IsNullOrWhiteSpace(transformedName) && seen.Add(transformedName))
                {
                    transformed.Add(transformedName);
                }
            }

            return transformed;
        }

        private static string? TransformSingleName(
            string? name,
            IReadOnlyDictionary<string, DesktopItemRenameCandidate> renameMap,
            IReadOnlySet<string> targetNames)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            if (renameMap.TryGetValue(name, out DesktopItemRenameCandidate? rename))
            {
                return rename.NewName;
            }

            return targetNames.Contains(name) ? null : name;
        }

        private void UpdateFileMoveHistoryAfterRenames(
            IReadOnlyCollection<DesktopItemRenameCandidate> renames,
            IReadOnlyDictionary<string, DesktopItemRenameCandidate> renameMap,
            IReadOnlySet<string> targetNames)
        {
            for (LinkedListNode<FileMoveUndoRecord>? node = _fileMoveHistory.First;
                 node != null;
                 node = node.Next)
            {
                FileMoveUndoRecord record = node.Value;
                string displayName = TransformSingleName(record.DisplayName, renameMap, targetNames)
                    ?? record.DisplayName;
                string sourcePath = TransformPath(record.SourcePath, renames);
                string destinationPath = TransformPath(record.DestinationPath, renames);

                if (record.SourceGroupSnapshot != null)
                {
                    record.SourceGroupSnapshot.ItemNames = TransformNameList(
                        record.SourceGroupSnapshot.ItemNames,
                        renameMap,
                        targetNames);
                }

                if (!displayName.Equals(record.DisplayName, StringComparison.Ordinal) ||
                    !sourcePath.Equals(record.SourcePath, StringComparison.Ordinal) ||
                    !destinationPath.Equals(record.DestinationPath, StringComparison.Ordinal))
                {
                    node.Value = record with
                    {
                        DisplayName = displayName,
                        SourcePath = sourcePath,
                        DestinationPath = destinationPath
                    };
                }
            }
        }

        private static string TransformPath(
            string path,
            IReadOnlyCollection<DesktopItemRenameCandidate> renames)
        {
            DesktopItemRenameCandidate? rename = renames.FirstOrDefault(candidate =>
                PathsEqual(path, candidate.OldFullPath));
            return rename?.NewFullPath ?? path;
        }

        private static bool ShellIdentityMatches(
            DesktopItemIdentityInfo first,
            DesktopItemIdentityInfo second)
        {
            return first.Kind == DesktopItemKind.ShellNamespace &&
                   second.Kind == DesktopItemKind.ShellNamespace &&
                   !string.IsNullOrWhiteSpace(first.ShellParsingName) &&
                   !string.IsNullOrWhiteSpace(second.ShellParsingName) &&
                   string.Equals(
                       first.ShellParsingName,
                       second.ShellParsingName,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool PhysicalIdentityMatches(
            DesktopItemIdentityInfo first,
            DesktopItemIdentityInfo second)
        {
            if (first.Kind != DesktopItemKind.FileSystem ||
                second.Kind != DesktopItemKind.FileSystem ||
                string.IsNullOrWhiteSpace(first.FileId) ||
                string.IsNullOrWhiteSpace(second.FileId) ||
                !first.FileId.Equals(second.FileId, StringComparison.OrdinalIgnoreCase) ||
                first.IsDirectory != second.IsDirectory)
            {
                return false;
            }

            return !first.CreationTimeUtcTicks.HasValue ||
                   !second.CreationTimeUtcTicks.HasValue ||
                   first.CreationTimeUtcTicks.Value == second.CreationTimeUtcTicks.Value;
        }

        private static bool TryGetActualEntry<T>(
            IReadOnlyDictionary<string, T> dictionary,
            string lookupName,
            out string actualName,
            out T value)
        {
            foreach ((string name, T candidate) in dictionary)
            {
                if (name.Equals(lookupName, StringComparison.OrdinalIgnoreCase))
                {
                    actualName = name;
                    value = candidate;
                    return true;
                }
            }

            actualName = string.Empty;
            value = default!;
            return false;
        }

        private static bool DesktopIdentityMapsEqual(
            IReadOnlyDictionary<string, DesktopItemIdentityInfo> current,
            IReadOnlyDictionary<string, DesktopItemIdentityInfo> next)
        {
            if (current.Count != next.Count)
            {
                return false;
            }

            foreach ((string name, DesktopItemIdentityInfo nextIdentity) in next)
            {
                if (!TryGetActualEntry(current, name, out string? actualName,
                        out DesktopItemIdentityInfo? currentIdentity) ||
                    !string.Equals(actualName, name, StringComparison.Ordinal) ||
                    !IdentityMetadataEquals(currentIdentity, nextIdentity))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IdentityMetadataEquals(
            DesktopItemIdentityInfo first,
            DesktopItemIdentityInfo second)
        {
            return first.Kind == second.Kind &&
                   PathsEqual(first.LastKnownPath, second.LastKnownPath) &&
                   string.Equals(first.FileId, second.FileId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(first.ShellParsingName, second.ShellParsingName, StringComparison.OrdinalIgnoreCase) &&
                   first.CreationTimeUtcTicks == second.CreationTimeUtcTicks &&
                   first.IsDirectory == second.IsDirectory;
        }

        private bool ReplacePersistedDesktopIdentities(
            IReadOnlyDictionary<string, DesktopItemIdentityInfo> identities)
        {
            if (DesktopIdentityMapsEqual(_appLayout.ItemIdentities, identities))
            {
                return false;
            }

            var replacement = new Dictionary<string, DesktopItemIdentityInfo>(StringComparer.OrdinalIgnoreCase);
            foreach ((string name, DesktopItemIdentityInfo identity) in identities)
            {
                replacement[name] = new DesktopItemIdentityInfo
                {
                    Kind = identity.Kind,
                    LastKnownPath = identity.LastKnownPath,
                    FileId = identity.FileId,
                    ShellParsingName = identity.ShellParsingName,
                    CreationTimeUtcTicks = identity.CreationTimeUtcTicks,
                    IsDirectory = identity.IsDirectory
                };
            }

            _appLayout.ItemIdentities = replacement;
            return true;
        }
    }
}
