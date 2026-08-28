// 跨会话真实文件操作账本、只读恢复核验与历史重建
namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private sealed record FileOperationRecoveryBackup(
            FileOperationJournalEntry Entry,
            FileOperationJournalState State,
            DateTime? CompletedUtc,
            string? ErrorCode,
            string? ErrorMessage,
            string? ResultIdentity);

        private void InitializeFileOperationJournal()
        {
            FileOperationJournalLoadResult loaded = _fileOperationJournalStore.Load();
            if (loaded.IsProtected || loaded.Journal == null)
            {
                ProtectFileOperationJournal(
                    loaded.ErrorMessage ?? "操作账本无法安全读取。");
                _fileOperationJournal = new FileOperationJournalData();
                PublishFileOperationJournalSnapshot();
                return;
            }

            _fileOperationJournal = loaded.Journal;
            List<FileOperationRecoveryBackup> recoveryBackups = _fileOperationJournal.Entries
                .Where(entry => !FileOperationJournalStateMachine.IsTerminal(entry.State))
                .Select(entry => new FileOperationRecoveryBackup(
                    entry,
                    entry.State,
                    entry.CompletedUtc,
                    entry.ErrorCode,
                    entry.ErrorMessage,
                    entry.ResultIdentity))
                .ToList();
            var recoveryEvaluator = new FileOperationRecoveryEvaluator();
            IReadOnlyList<FileOperationJournalRecoveryChange> recoveryChanges =
                FileOperationJournalRecovery.Apply(
                    _fileOperationJournal,
                    recoveryEvaluator,
                    DateTime.UtcNow);
            if (recoveryChanges.Count > 0 && !_fileOperationJournalStore.TrySave(
                    _fileOperationJournal,
                    out string? recoverySaveError))
            {
                foreach (FileOperationRecoveryBackup backup in recoveryBackups)
                {
                    backup.Entry.State = backup.State;
                    backup.Entry.CompletedUtc = backup.CompletedUtc;
                    backup.Entry.ErrorCode = backup.ErrorCode;
                    backup.Entry.ErrorMessage = backup.ErrorMessage;
                    backup.Entry.ResultIdentity = backup.ResultIdentity;
                }
                recoveryChanges = Array.Empty<FileOperationJournalRecoveryChange>();
                ProtectFileOperationJournal(
                    recoverySaveError ?? "恢复核验结果无法写入操作账本。");
            }

            bool restoredRecoveredUndoLayout = false;
            foreach (FileOperationJournalRecoveryChange change in recoveryChanges.Where(change =>
                         change.Kind == FileOperationJournalKind.UndoMove &&
                         change.RecoveredState == FileOperationJournalState.Succeeded))
            {
                FileOperationJournalEntry? undo = _fileOperationJournal.Entries.FirstOrDefault(entry =>
                    entry.Id.Equals(change.EntryId, StringComparison.OrdinalIgnoreCase));
                FileOperationJournalEntry? original = FindOriginalMoveEntry(undo?.UndoOfEntryId);
                FileMoveUndoRecord? record = original == null
                    ? null
                    : CreateUndoRecordFromJournalEntry(original);
                if (record != null)
                {
                    RestoreLayoutAfterUndo(record);
                    restoredRecoveredUndoLayout = true;
                }
            }

            RebuildFileMoveHistoryFromJournal();
            PublishFileOperationJournalSnapshot();
            if (restoredRecoveredUndoLayout)
            {
                SaveLayout();
                SaveLayoutNow();
            }
        }

        private bool EnsureFileOperationJournalAvailable(string operationDescription)
        {
            if (!_fileOperationJournalProtected)
            {
                return true;
            }

            string detail = string.IsNullOrWhiteSpace(_fileOperationJournalError)
                ? "操作账本当前不可用。"
                : _fileOperationJournalError;
            StatusText.Text = $"未执行{operationDescription}：操作账本处于保护状态";
            MessageBox.Show(
                $"为避免产生无法追踪的真实文件变化，本次操作没有执行。\n\n{detail}\n\n" +
                "请先保留并检查 operation-journal.json；程序不会自动重试。",
                "真实文件操作已阻止",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        private void ProtectFileOperationJournal(string errorMessage)
        {
            lock (_fileOperationJournalGate)
            {
                _fileOperationJournalError = string.IsNullOrWhiteSpace(errorMessage)
                    ? "操作账本不可用。"
                    : errorMessage;
                _fileOperationJournalProtected = true;
                _diagnostics.Log($"JOURNAL protected error={_fileOperationJournalError}");
            }
        }

        private FileOperationDispatchResult DispatchJournaledOperation(
            FileOperationJournalEntry entry,
            Func<FileOperationExecutionOutcome> operation)
        {
            if (_fileOperationJournalProtected)
            {
                return new FileOperationDispatchResult(
                    Executed: false,
                    JournalPersisted: false,
                    Entry: entry,
                    ErrorMessage: _fileOperationJournalError ?? "操作账本处于保护状态。");
            }

            var coordinator = new FileOperationWriteAheadCoordinator(
                _fileOperationJournalStore,
                afterPersisted: _ => PublishFileOperationJournalSnapshot(),
                journalGate: _fileOperationJournalGate);
            FileOperationDispatchResult result = coordinator.DispatchQueued(
                _fileOperationJournal,
                entry,
                operation,
                canStart: () => !_fileOperationJournalProtected);
            if (!result.JournalPersisted)
            {
                ProtectFileOperationJournal(
                    result.ErrorMessage ?? "操作账本写入失败。");
            }
            return result;
        }

        private bool TryPersistQueuedJournalEntries(
            IReadOnlyList<FileOperationJournalEntry> entries,
            out string errorMessage)
        {
            lock (_fileOperationJournalGate)
            {
                errorMessage = string.Empty;
                if (_fileOperationJournalProtected)
                {
                    errorMessage = _fileOperationJournalError ?? "操作账本处于保护状态。";
                    return false;
                }

                var coordinator = new FileOperationWriteAheadCoordinator(
                    _fileOperationJournalStore,
                    afterPersisted: _ => PublishFileOperationJournalSnapshot(),
                    journalGate: _fileOperationJournalGate);
                if (coordinator.TryQueue(
                        _fileOperationJournal,
                        entries,
                        out string? queueError))
                {
                    return true;
                }

                errorMessage = queueError ?? "Queued 状态未能落盘。";
                ProtectFileOperationJournal(errorMessage);
                return false;
            }
        }

        private void PublishFileOperationJournalSnapshot()
        {
            _fileOperationJournalDisplayEntries = _fileOperationJournal.Entries
                .Select(CloneJournalEntryForDisplay)
                .OrderByDescending(entry => entry.RequestedUtc)
                .ToList();
        }

        private static FileOperationJournalEntry CloneJournalEntryForDisplay(
            FileOperationJournalEntry entry) => new()
        {
            Id = entry.Id,
            BatchId = entry.BatchId,
            Kind = entry.Kind,
            State = entry.State,
            DisplayName = entry.DisplayName,
            SourcePath = entry.SourcePath,
            DestinationPath = entry.DestinationPath,
            ExpectedIdentity = entry.ExpectedIdentity,
            ResultIdentity = entry.ResultIdentity,
            RequestedUtc = entry.RequestedUtc,
            StartedUtc = entry.StartedUtc,
            CompletedUtc = entry.CompletedUtc,
            ErrorCode = entry.ErrorCode,
            ErrorMessage = entry.ErrorMessage,
            UndoOfEntryId = entry.UndoOfEntryId,
            IsAggregate = entry.IsAggregate
        };

        private FileOperationJournalEntry? FindOriginalMoveEntry(string? entryId) =>
            string.IsNullOrWhiteSpace(entryId)
                ? null
                : _fileOperationJournal.Entries.FirstOrDefault(entry =>
                    entry.Id.Equals(entryId, StringComparison.OrdinalIgnoreCase) &&
                    entry.Kind == FileOperationJournalKind.MoveIntoFolder);

        private void RebuildFileMoveHistoryFromJournal()
        {
            _fileMoveHistory.Clear();
            var successfullyUndone = new HashSet<string>(
                _fileOperationJournal.Entries
                    .Where(entry =>
                        entry.Kind == FileOperationJournalKind.UndoMove &&
                        entry.State == FileOperationJournalState.Succeeded &&
                        !string.IsNullOrWhiteSpace(entry.UndoOfEntryId))
                    .Select(entry => entry.UndoOfEntryId!),
                StringComparer.OrdinalIgnoreCase);

            foreach (FileOperationJournalEntry entry in _fileOperationJournal.Entries
                         .Where(entry =>
                             entry.Kind == FileOperationJournalKind.MoveIntoFolder &&
                             entry.State == FileOperationJournalState.Succeeded &&
                             !successfullyUndone.Contains(entry.Id))
                         .OrderByDescending(entry => entry.CompletedUtc ?? entry.RequestedUtc)
                         .Take(20)
                         .Reverse())
            {
                FileMoveUndoRecord? record = CreateUndoRecordFromJournalEntry(entry);
                if (record != null)
                {
                    PushFileMoveHistory(record);
                }
            }
            UpdateUndoFileMoveButton();
        }

        private static FileMoveUndoRecord? CreateUndoRecordFromJournalEntry(
            FileOperationJournalEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.SourcePath) ||
                string.IsNullOrWhiteSpace(entry.DestinationPath) ||
                string.IsNullOrWhiteSpace(entry.ExpectedIdentity))
            {
                return null;
            }

            FileOperationLayoutSnapshot snapshot = entry.LayoutSnapshot ?? new();
            return new FileMoveUndoRecord(
                string.IsNullOrWhiteSpace(entry.DisplayName)
                    ? Path.GetFileName(entry.SourcePath)
                    : entry.DisplayName,
                entry.SourcePath,
                entry.DestinationPath,
                snapshot.SourceWorkspaceId,
                snapshot.SourceGroupId,
                FromJournalGroupSnapshot(snapshot.SourceGroup),
                snapshot.SourceGroupItemIndex,
                FromJournalPosition(snapshot.FreePosition),
                FromJournalPosition(snapshot.AutoClassificationOriginalPosition),
                entry.ResultIdentity ?? entry.ExpectedIdentity,
                entry.CompletedUtc ?? entry.RequestedUtc,
                entry.Id);
        }

        private FileOperationLayoutSnapshot CreateJournalLayoutSnapshot(
            PendingPhysicalMove pending) => new()
        {
            SourceWorkspaceId = null,
            SourceGroupId = pending.SourceGroupId,
            SourceGroup = ToJournalGroupSnapshot(pending.SourceGroupSnapshot),
            SourceGroupItemIndex = pending.SourceGroupItemIndex,
            FreePosition = ToJournalPosition(pending.FreePosition),
            AutoClassificationOriginalPosition = ToJournalPosition(
                pending.AutoClassificationOriginalPosition),
            ItemTags = _appLayout.ItemTags.TryGetValue(
                pending.DisplayName,
                out List<string>? tags)
                    ? tags.ToList()
                    : new List<string>(),
            FirstSeenUtcTicks = _appLayout.ItemFirstSeenUtcTicks.TryGetValue(
                pending.DisplayName,
                out long firstSeen)
                    ? firstSeen
                    : null,
            LastMovedUtcTicks = _appLayout.ItemLastMovedUtcTicks.TryGetValue(
                pending.DisplayName,
                out long lastMoved)
                    ? lastMoved
                    : null,
            InboxItem = _appLayout.InboxItems.TryGetValue(
                pending.DisplayName,
                out InboxItemInfo? inboxItem)
                    ? CloneJournalInboxItem(inboxItem)
                    : null
        };

        private void AttachWorkspacePlacementsToSnapshot(
            FileOperationLayoutSnapshot snapshot,
            PendingPhysicalMove pending)
        {
            snapshot.SourceWorkspaceId = _appLayout.ActiveWorkspaceId;
            snapshot.WorkspacePlacements.Clear();
            if (!string.IsNullOrWhiteSpace(_appLayout.ActiveWorkspaceId))
            {
                snapshot.WorkspacePlacements.Add(new FileOperationWorkspacePlacementSnapshot
                {
                    WorkspaceId = _appLayout.ActiveWorkspaceId,
                    SourceGroupId = snapshot.SourceGroupId,
                    SourceGroup = snapshot.SourceGroup,
                    SourceGroupItemIndex = snapshot.SourceGroupItemIndex,
                    FreePosition = snapshot.FreePosition,
                    AutoClassificationOriginalPosition =
                        snapshot.AutoClassificationOriginalPosition
                });
            }

            foreach (WorkspaceProfileInfo workspace in _appLayout.Workspaces)
            {
                if (workspace.Id.Equals(
                        _appLayout.ActiveWorkspaceId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                FileOperationWorkspacePlacementSnapshot? placement =
                    CaptureWorkspacePlacement(
                        workspace.Id,
                        workspace.Layout,
                        pending.DisplayName);
                if (placement != null)
                {
                    snapshot.WorkspacePlacements.Add(placement);
                }
            }
        }

        private static FileOperationWorkspacePlacementSnapshot? CaptureWorkspacePlacement(
            string workspaceId,
            WorkspaceLayoutState layout,
            string displayName)
        {
            GroupInfo? group = layout.Groups.FirstOrDefault(candidate =>
                candidate.ItemNames.Contains(displayName, StringComparer.OrdinalIgnoreCase));
            int groupIndex = group?.ItemNames.FindIndex(item =>
                item.Equals(displayName, StringComparison.OrdinalIgnoreCase)) ?? -1;
            layout.FreeIcons.TryGetValue(displayName, out IconPosition? freePosition);
            layout.AutoClassificationOriginalPositions.TryGetValue(
                displayName,
                out IconPosition? autoPosition);
            if (group == null && freePosition == null && autoPosition == null)
            {
                return null;
            }

            return new FileOperationWorkspacePlacementSnapshot
            {
                WorkspaceId = workspaceId,
                SourceGroupId = group?.Id,
                SourceGroup = ToJournalGroupSnapshot(group),
                SourceGroupItemIndex = groupIndex,
                FreePosition = ToJournalPosition(freePosition),
                AutoClassificationOriginalPosition = ToJournalPosition(autoPosition)
            };
        }

        private static FileOperationPositionSnapshot? ToJournalPosition(IconPosition? position) =>
            position == null
                ? null
                : new FileOperationPositionSnapshot { X = position.X, Y = position.Y };

        private static IconPosition? FromJournalPosition(FileOperationPositionSnapshot? position) =>
            position == null ? null : new IconPosition { X = position.X, Y = position.Y };

        private static FileOperationGroupSnapshot? ToJournalGroupSnapshot(GroupInfo? group) =>
            group == null
                ? null
                : new FileOperationGroupSnapshot
                {
                    Id = group.Id,
                    Name = group.Name,
                    X = group.X,
                    Y = group.Y,
                    Width = group.Width,
                    Height = group.Height,
                    IsCollapsed = group.IsCollapsed,
                    IsAutoCategory = group.IsAutoCategory,
                    AutoCategoryKey = group.AutoCategoryKey,
                    UserRuleId = group.UserRuleId,
                    IsSizeLocked = group.IsSizeLocked,
                    SortMode = group.SortMode.ToString()
                };

        private static GroupInfo? FromJournalGroupSnapshot(FileOperationGroupSnapshot? group)
        {
            if (group == null)
            {
                return null;
            }

            _ = Enum.TryParse(group.SortMode, ignoreCase: true, out GroupSortMode sortMode);
            return new GroupInfo
            {
                Id = group.Id,
                Name = group.Name,
                X = group.X,
                Y = group.Y,
                Width = group.Width,
                Height = group.Height,
                IsCollapsed = group.IsCollapsed,
                IsAutoCategory = group.IsAutoCategory,
                AutoCategoryKey = group.AutoCategoryKey,
                UserRuleId = group.UserRuleId,
                IsSizeLocked = group.IsSizeLocked,
                SortMode = Enum.IsDefined(sortMode) ? sortMode : GroupSortMode.Custom,
                ItemNames = new List<string>()
            };
        }

        /// <summary>
        /// 恢复真实移动前项目在全部命名工作区中的位置。返回 true 表示新格式快照
        /// 已完整处理，调用方不应再走只恢复单一来源工作区的兼容路径。
        /// </summary>
        private bool RestoreWorkspacePlacementsAfterUndo(FileMoveUndoRecord record)
        {
            FileOperationLayoutSnapshot? snapshot =
                FindOriginalMoveEntry(record.JournalEntryId)?.LayoutSnapshot;
            if (snapshot?.WorkspacePlacements == null ||
                snapshot.WorkspacePlacements.Count == 0)
            {
                return false;
            }

            foreach (FileOperationWorkspacePlacementSnapshot placement in
                     snapshot.WorkspacePlacements)
            {
                WorkspaceProfileInfo? workspace = _appLayout.Workspaces.FirstOrDefault(candidate =>
                    candidate.Id.Equals(
                        placement.WorkspaceId,
                        StringComparison.OrdinalIgnoreCase));
                if (workspace != null)
                {
                    RestoreWorkspacePlacement(
                        record.DisplayName,
                        placement,
                        workspace.Layout);
                }
            }

            FileOperationWorkspacePlacementSnapshot? activePlacement =
                snapshot.WorkspacePlacements.FirstOrDefault(placement =>
                    placement.WorkspaceId.Equals(
                        _appLayout.ActiveWorkspaceId,
                        StringComparison.OrdinalIgnoreCase));
            if (activePlacement != null)
            {
                RestoreCurrentWorkspacePlacement(record.DisplayName, activePlacement);
            }
            // 未命名根布局没有 WorkspaceId，继续走兼容路径恢复顶部快照；
            // 上面的循环仍已恢复所有已命名工作区。
            return !string.IsNullOrWhiteSpace(snapshot.SourceWorkspaceId);
        }

        private void RestoreWorkspacePlacement(
            string displayName,
            FileOperationWorkspacePlacementSnapshot placement,
            WorkspaceLayoutState layout)
        {
            foreach (GroupInfo group in layout.Groups)
            {
                group.ItemNames.RemoveAll(item =>
                    item.Equals(displayName, StringComparison.OrdinalIgnoreCase));
            }
            layout.FreeIcons.Remove(displayName);

            bool canceledAutoCategory = placement.SourceGroup?.IsAutoCategory == true &&
                !string.IsNullOrWhiteSpace(placement.SourceGroupId) &&
                _canceledAutoCategoryGroupIds.Contains(placement.SourceGroupId);
            GroupInfo? originalGroup = !canceledAutoCategory &&
                                       !string.IsNullOrWhiteSpace(placement.SourceGroupId)
                ? layout.Groups.FirstOrDefault(group =>
                    group.Id.Equals(
                        placement.SourceGroupId,
                        StringComparison.OrdinalIgnoreCase))
                : null;
            if (originalGroup == null &&
                placement.SourceGroup != null &&
                !canceledAutoCategory)
            {
                originalGroup = FromJournalGroupSnapshot(placement.SourceGroup);
                if (originalGroup != null)
                {
                    layout.Groups.Add(originalGroup);
                }
            }

            if (originalGroup != null)
            {
                int restoreIndex = Math.Clamp(
                    placement.SourceGroupItemIndex,
                    0,
                    originalGroup.ItemNames.Count);
                originalGroup.ItemNames.Insert(restoreIndex, displayName);
            }
            else
            {
                IconPosition requestedPosition = FromJournalPosition(placement.FreePosition) ??
                    FromJournalPosition(placement.AutoClassificationOriginalPosition) ??
                    CreatePlacementFallbackPosition(placement);
                layout.FreeIcons[displayName] = requestedPosition;
            }

            if (!canceledAutoCategory &&
                placement.AutoClassificationOriginalPosition != null)
            {
                layout.AutoClassificationOriginalPositions[displayName] =
                    FromJournalPosition(placement.AutoClassificationOriginalPosition)!;
            }
        }

        private void RestoreCurrentWorkspacePlacement(
            string displayName,
            FileOperationWorkspacePlacementSnapshot placement)
        {
            var current = new WorkspaceLayoutState
            {
                Groups = _appLayout.Groups,
                FreeIcons = _appLayout.FreeIcons,
                AutoClassificationOriginalPositions =
                    _appLayout.AutoClassificationOriginalPositions
            };
            RestoreWorkspacePlacement(
                displayName,
                placement,
                current);

            GroupInfo? restoredGroup = !string.IsNullOrWhiteSpace(placement.SourceGroupId)
                ? _appLayout.Groups.FirstOrDefault(group =>
                    group.Id.Equals(
                        placement.SourceGroupId,
                        StringComparison.OrdinalIgnoreCase))
                : null;
            if (restoredGroup != null)
            {
                ClampGroupToCanvas(restoredGroup);
                return;
            }

            if (_appLayout.FreeIcons.TryGetValue(displayName, out IconPosition? position))
            {
                ClampIconPosition(position);
                _appLayout.FreeIcons[displayName] = _appLayout.SnapToGrid
                    ? FindAlignedIconPosition(displayName, position) ?? position
                    : position;
            }
        }

        private static IconPosition CreatePlacementFallbackPosition(
            FileOperationWorkspacePlacementSnapshot placement)
        {
            FileOperationGroupSnapshot? group = placement.SourceGroup;
            if (group == null)
            {
                return new IconPosition { X = GridOriginX, Y = GridOriginY };
            }

            int index = Math.Max(0, placement.SourceGroupItemIndex);
            return new IconPosition
            {
                X = group.X + (index % 3) * IconCellWidth,
                Y = group.Y + 36 + (index / 3) * IconCellHeight
            };
        }

        private void RestoreItemMetadataAfterUndo(FileMoveUndoRecord record)
        {
            FileOperationJournalEntry? original = FindOriginalMoveEntry(record.JournalEntryId);
            FileOperationLayoutSnapshot? snapshot = original?.LayoutSnapshot;
            if (snapshot == null)
            {
                return;
            }

            if (snapshot.ItemTags.Count > 0)
            {
                _appLayout.ItemTags[record.DisplayName] = snapshot.ItemTags
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Select(tag => tag.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            else
            {
                _appLayout.ItemTags.Remove(record.DisplayName);
            }

            RestoreOptionalItemTime(
                _appLayout.ItemFirstSeenUtcTicks,
                record.DisplayName,
                snapshot.FirstSeenUtcTicks);
            RestoreOptionalItemTime(
                _appLayout.ItemLastMovedUtcTicks,
                record.DisplayName,
                snapshot.LastMovedUtcTicks);

            if (snapshot.InboxItem != null)
            {
                _appLayout.InboxItems[record.DisplayName] =
                    CloneJournalInboxItem(snapshot.InboxItem);
            }
            else
            {
                _appLayout.InboxItems.Remove(record.DisplayName);
            }
        }

        private static void RestoreOptionalItemTime(
            Dictionary<string, long> destination,
            string displayName,
            long? value)
        {
            if (value.HasValue)
            {
                destination[displayName] = value.Value;
            }
            else
            {
                destination.Remove(displayName);
            }
        }

        private static InboxItemInfo CloneJournalInboxItem(InboxItemInfo item) => new()
        {
            Identity = new DesktopItemIdentityInfo
            {
                Kind = item.Identity.Kind,
                LastKnownPath = item.Identity.LastKnownPath,
                FileId = item.Identity.FileId,
                ShellParsingName = item.Identity.ShellParsingName,
                CreationTimeUtcTicks = item.Identity.CreationTimeUtcTicks,
                LastWriteTimeUtcTicks = item.Identity.LastWriteTimeUtcTicks,
                IsDirectory = item.Identity.IsDirectory
            },
            DetectedUtc = item.DetectedUtc,
            UpdatedUtc = item.UpdatedUtc,
            SuggestedCategoryKey = item.SuggestedCategoryKey,
            SuggestedCategoryName = item.SuggestedCategoryName,
            SuggestedCategoryOrder = item.SuggestedCategoryOrder,
            MatchReason = item.MatchReason,
            Reliability = item.Reliability,
            ReviewState = item.ReviewState
        };

        private void OperationCenterButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OperationCenterWindow(
                CreateOperationCenterJournalSnapshot,
                () => _fileOperationJournalProtected ? _fileOperationJournalError : null);
            if (_isAttachedToDesktop)
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            else
            {
                dialog.Owner = this;
            }
            dialog.ShowDialog();
        }

        private FileOperationJournalData CreateOperationCenterJournalSnapshot()
        {
            IReadOnlyList<FileOperationJournalEntry> entries =
                _fileOperationJournalDisplayEntries;
            return new FileOperationJournalData
            {
                Entries = entries
                    .Select(CloneJournalEntryForDisplay)
                    .ToList()
            };
        }
    }
}
