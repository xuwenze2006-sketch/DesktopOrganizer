// 后台真实文件操作队列、完成回填与布局同步
namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private sealed record PendingPhysicalMove(
            string DisplayName,
            string SourcePath,
            string TargetFolderPath,
            bool AllowAutoRename,
            string? SourceGroupId,
            GroupInfo? SourceGroupSnapshot,
            int SourceGroupItemIndex,
            IconPosition? FreePosition,
            IconPosition? AutoClassificationOriginalPosition,
            string SourceIdentity,
            string TargetFolderIdentity);

        private sealed record PhysicalMoveCompletion(
            bool Success,
            bool Canceled,
            string SourcePath,
            string? DestinationPath,
            string? ErrorMessage);

        private sealed record JournaledPhysicalMoveCompletion(
            PhysicalMoveCompletion Operation,
            FileOperationJournalEntry? JournalEntry,
            bool JournalPersisted,
            string? JournalError);

        private sealed record RecycleRequest(
            string DisplayName,
            string FullPath,
            string ExpectedIdentity);

        private sealed record JournaledRecycleRequest(
            RecycleRequest Request,
            FileOperationJournalEntry JournalEntry);

        private sealed record RecycleItemCompletion(
            string DisplayName,
            string FullPath,
            bool Success,
            bool Canceled,
            string? ErrorMessage,
            bool JournalPersisted = true);

        private sealed record RecycleBatchCompletion(
            IReadOnlyList<RecycleItemCompletion> Items);

        private enum UndoMoveCompletionKind
        {
            Success,
            DestinationMissing,
            DestinationChanged,
            SourceOccupied,
            Canceled,
            Failed
        }

        private sealed record UndoMoveCompletion(
            UndoMoveCompletionKind Kind,
            string? ErrorMessage);

        private sealed record JournaledUndoMoveCompletion(
            UndoMoveCompletion Operation,
            FileOperationJournalEntry? JournalEntry,
            bool JournalPersisted,
            string? JournalError);

        private bool HasPendingFileOperations => _fileOperationCount > 0;

        private static string GetFileOperationPathKey(string path)
        {
            try
            {
                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            }
            catch
            {
                return path;
            }
        }

        private bool IsFileOperationPending(string path) =>
            _pendingFileOperationPaths.Contains(GetFileOperationPathKey(path));

        private bool TryReserveFileOperation(
            IEnumerable<string> paths,
            string busyMessage,
            out List<string> reservedKeys)
        {
            reservedKeys = paths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(GetFileOperationPathKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (_isClosing || reservedKeys.Count == 0)
            {
                return false;
            }

            if (reservedKeys.Any(key => _pendingFileOperationPaths.Contains(key)))
            {
                StatusText.Text = busyMessage;
                return false;
            }

            foreach (string key in reservedKeys)
            {
                _pendingFileOperationPaths.Add(key);
            }

            _fileOperationCount++;
            UpdateUndoFileMoveButton();
            return true;
        }

        private void ReleaseFileOperation(IReadOnlyList<string> reservedKeys)
        {
            foreach (string key in reservedKeys)
            {
                _pendingFileOperationPaths.Remove(key);
            }

            _fileOperationCount = Math.Max(0, _fileOperationCount - 1);
            UpdateUndoFileMoveButton();
        }

        private string FormatFileOperationStatus(string message) =>
            HasPendingFileOperations
                ? $"{message}（另有 {_fileOperationCount} 个后台文件任务等待或执行）"
                : message;

        private void RemoveDesktopItemAfterPhysicalOperation(
            string displayName,
            string fullPath)
        {
            foreach (GroupInfo layoutGroup in _appLayout.Groups)
            {
                layoutGroup.ItemNames.RemoveAll(item =>
                    item.Equals(displayName, StringComparison.OrdinalIgnoreCase));
                layoutGroup.ManuallyAssignedItemNames.RemoveAll(item =>
                    item.Equals(displayName, StringComparison.OrdinalIgnoreCase));
            }

            _appLayout.FreeIcons.Remove(displayName);
            _appLayout.AutoClassificationOriginalPositions.Remove(displayName);
            _appLayout.InboxItems.Remove(displayName);
            _appLayout.ItemTags.Remove(displayName);
            _appLayout.ItemFirstSeenUtcTicks.Remove(displayName);
            _appLayout.ItemLastMovedUtcTicks.Remove(displayName);
            WorkspaceLayoutManager.RemoveItemFromSnapshots(_appLayout, displayName);
            _desktopItems.Remove(displayName);
            _desktopCategories.Remove(displayName);
            InvalidateIconCacheLocations([fullPath]);
        }

        internal static void RemoveCompletedRecycleItemsFromSelection(
            ISet<string> selectedItemNames,
            IEnumerable<string> succeededItemNames)
        {
            foreach (string itemName in succeededItemNames)
            {
                selectedItemNames.Remove(itemName);
            }
        }

        private PhysicalFolderMoveResult QueuePhysicalFolderMove(
            PendingPhysicalMove pending)
        {
            if (!EnsureFileOperationJournalAvailable("真实文件移动"))
            {
                return PhysicalFolderMoveResult.Rejected;
            }

            if (!FileOperationIdentityGuard.Matches(
                    pending.SourcePath,
                    pending.SourceIdentity))
            {
                StatusText.Text = $"无法确认“{pending.DisplayName}”仍是当前项目，未排队移动";
                return PhysicalFolderMoveResult.Rejected;
            }

            if (!TryPlanPhysicalFolderMove(
                    pending,
                    out string destinationPath,
                    out string planningError))
            {
                StatusText.Text = $"未能移动“{pending.DisplayName}”：{planningError}";
                return PhysicalFolderMoveResult.Rejected;
            }

            FileOperationLayoutSnapshot snapshot = CreateJournalLayoutSnapshot(pending);
            AttachWorkspacePlacementsToSnapshot(snapshot, pending);
            var journalEntry = new FileOperationJournalEntry
            {
                Kind = FileOperationJournalKind.MoveIntoFolder,
                DisplayName = pending.DisplayName,
                SourcePath = pending.SourcePath,
                DestinationPath = destinationPath,
                ExpectedIdentity = pending.SourceIdentity,
                LayoutSnapshot = snapshot
            };

            if (!FileOperationIdentityGuard.Matches(
                    pending.TargetFolderPath,
                    pending.TargetFolderIdentity))
            {
                StatusText.Text = "无法确认目标文件夹身份，未排队移动";
                return PhysicalFolderMoveResult.Rejected;
            }

            if (!TryReserveFileOperation(
                    [pending.SourcePath, pending.TargetFolderPath],
                    $"“{pending.DisplayName}”或目标文件夹已有文件操作正在进行",
                    out List<string> reservedKeys))
            {
                return PhysicalFolderMoveResult.Rejected;
            }

            if (!TryPersistQueuedJournalEntries([journalEntry], out string queueError))
            {
                ReleaseFileOperation(reservedKeys);
                StatusText.Text =
                    $"未排队移动“{pending.DisplayName}”：操作账本无法安全写入";
                _diagnostics.Log($"MOVE queue journal failed error={queueError}");
                return PhysicalFolderMoveResult.Rejected;
            }

            // 当前仍位于 MouseUp 调用栈内，直接重建会与拖拽清理争用同一个视觉。
            // 延迟到本次输入事件结束后，再把图标放回原布局并显示半透明等待状态。
            string queuedStatus = $"已排队：正在把“{pending.DisplayName}”移入“{Path.GetFileName(pending.TargetFolderPath)}”…";
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() =>
                {
                    RebuildDesktopIcons();
                    if (IsFileOperationPending(pending.SourcePath))
                    {
                        StatusText.Text = queuedStatus;
                    }
                }));
            StatusText.Text = queuedStatus;
            _ = CompletePhysicalFolderMoveAsync(pending, journalEntry, reservedKeys);
            return PhysicalFolderMoveResult.Queued;
        }

        private async Task CompletePhysicalFolderMoveAsync(
            PendingPhysicalMove pending,
            FileOperationJournalEntry journalEntry,
            IReadOnlyList<string> reservedKeys)
        {
            JournaledPhysicalMoveCompletion completion;
            try
            {
                completion = await _fileOperationService.Enqueue(
                    token => ExecuteJournaledPhysicalFolderMove(
                        pending,
                        journalEntry,
                        token),
                    _lifetimeCts.Token);
            }
            catch (OperationCanceledException)
            {
                completion = new JournaledPhysicalMoveCompletion(
                    new PhysicalMoveCompletion(
                        Success: false,
                        Canceled: true,
                        SourcePath: pending.SourcePath,
                        DestinationPath: null,
                        ErrorMessage: null),
                    JournalEntry: null,
                    JournalPersisted: false,
                    JournalError: "任务在写入运行状态前被取消。");
            }
            catch (Exception exception)
            {
                completion = new JournaledPhysicalMoveCompletion(
                    new PhysicalMoveCompletion(
                        Success: false,
                        Canceled: false,
                        SourcePath: pending.SourcePath,
                        DestinationPath: null,
                        ErrorMessage: exception.Message),
                    JournalEntry: null,
                    JournalPersisted: false,
                    JournalError: exception.Message);
            }

            if (_isClosing || Dispatcher.HasShutdownStarted)
            {
                return;
            }

            ReleaseFileOperation(reservedKeys);
            PhysicalMoveCompletion operation = completion.Operation;
            if (operation.Success && operation.DestinationPath != null)
            {
                FileMoveUndoRecord? undoRecord = completion.JournalPersisted &&
                    completion.JournalEntry?.State == FileOperationJournalState.Succeeded
                        ? CreateUndoRecordFromJournalEntry(completion.JournalEntry)
                        : null;
                if (undoRecord != null)
                {
                    PushFileMoveHistory(undoRecord);
                }

                RemoveDesktopItemAfterPhysicalOperation(
                    pending.DisplayName,
                    pending.SourcePath);
                _selectedItemNames.Remove(pending.DisplayName);
                RebuildDesktopIconsAndSaveLayout();
                StatusText.Text = FormatFileOperationStatus(
                    undoRecord != null
                        ? $"已将“{pending.DisplayName}”移入真实文件夹“{Path.GetFileName(pending.TargetFolderPath)}”；可完整撤销"
                        : $"已移动“{pending.DisplayName}”，但账本终态未能可靠确认；不可自动撤销，请在操作中心核对");
                _diagnostics.Log($"MOVE success source={pending.SourcePath}, destination={operation.DestinationPath}, journalPersisted={completion.JournalPersisted}");
                RequestDesktopRefresh(clearIconCache: false, statusMessage: null);
                return;
            }

            RebuildDesktopIcons();
            if (operation.Canceled)
            {
                StatusText.Text = FormatFileOperationStatus($"已取消移动“{pending.DisplayName}”");
                return;
            }

            string error = string.IsNullOrWhiteSpace(operation.ErrorMessage)
                ? "文件或目标文件夹的状态已经改变"
                : operation.ErrorMessage;
            StatusText.Text = FormatFileOperationStatus($"未能移动“{pending.DisplayName}”");
            _diagnostics.Log($"MOVE failed source={pending.SourcePath}, target={pending.TargetFolderPath}, error={error}");
            MessageBox.Show(
                $"无法移动该项目：{error}",
                "移动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            RequestDesktopRefresh(clearIconCache: false, statusMessage: null);
        }

        private JournaledPhysicalMoveCompletion ExecuteJournaledPhysicalFolderMove(
            PendingPhysicalMove pending,
            FileOperationJournalEntry journalEntry,
            CancellationToken cancellationToken)
        {
            PhysicalMoveCompletion? operationCompletion = null;
            FileOperationDispatchResult dispatch = DispatchJournaledOperation(
                journalEntry,
                () =>
                {
                    operationCompletion = ExecutePhysicalFolderMove(
                        pending,
                        journalEntry.DestinationPath!,
                        cancellationToken);
                    if (operationCompletion.Canceled)
                    {
                        return new FileOperationExecutionOutcome(
                            FileOperationJournalState.Canceled,
                            ErrorCode: "canceled",
                            ErrorMessage: operationCompletion.ErrorMessage);
                    }
                    if (!operationCompletion.Success)
                    {
                        return FileOperationExecutionOutcome.Failure(
                            "move_failed",
                            operationCompletion.ErrorMessage ?? "文件移动失败。");
                    }

                    if (!FileOperationIdentityGuard.TryCapture(
                            operationCompletion.DestinationPath!,
                            out string resultIdentity) ||
                        !string.Equals(
                            pending.SourceIdentity,
                            resultIdentity,
                            StringComparison.Ordinal))
                    {
                        return new FileOperationExecutionOutcome(
                            FileOperationJournalState.Uncertain,
                            operationCompletion.DestinationPath,
                            ResultIdentity: resultIdentity,
                            ErrorCode: "result_identity_unverified",
                            ErrorMessage: "真实移动已发生，但无法核验移动后项目身份。");
                    }

                    return FileOperationExecutionOutcome.Success(
                        operationCompletion.DestinationPath,
                        resultIdentity);
                });

            operationCompletion ??= new PhysicalMoveCompletion(
                Success: false,
                Canceled: dispatch.Entry.State == FileOperationJournalState.Canceled,
                SourcePath: pending.SourcePath,
                DestinationPath: null,
                ErrorMessage: dispatch.ErrorMessage ?? "操作账本未能写入，真实移动没有执行。");
            return new JournaledPhysicalMoveCompletion(
                operationCompletion,
                dispatch.Entry,
                dispatch.JournalPersisted,
                dispatch.ErrorMessage);
        }

        private static bool TryPlanPhysicalFolderMove(
            PendingPhysicalMove pending,
            out string destinationPath,
            out string errorMessage)
        {
            destinationPath = string.Empty;
            errorMessage = string.Empty;
            bool sourceIsDirectory = Directory.Exists(pending.SourcePath);
            bool sourceIsFile = File.Exists(pending.SourcePath);
            if (!sourceIsDirectory && !sourceIsFile)
            {
                errorMessage = "源项目已不存在";
                return false;
            }

            if (!FileOperationIdentityGuard.Matches(pending.SourcePath, pending.SourceIdentity))
            {
                errorMessage = "源项目已被替换，未执行移动";
                return false;
            }

            if (!Directory.Exists(pending.TargetFolderPath))
            {
                errorMessage = "目标文件夹已不存在";
                return false;
            }

            if (!FileOperationIdentityGuard.Matches(
                    pending.TargetFolderPath,
                    pending.TargetFolderIdentity))
            {
                errorMessage = "目标文件夹已被替换，未执行移动";
                return false;
            }

            string sourceName = Path.GetFileName(Path.TrimEndingDirectorySeparator(pending.SourcePath));
            destinationPath = Path.Combine(pending.TargetFolderPath, sourceName);
            if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
            {
                if (!pending.AllowAutoRename)
                {
                    errorMessage = "目标文件夹中已存在同名项目";
                    return false;
                }

                destinationPath = GetUniqueDestinationPath(
                    pending.TargetFolderPath,
                    sourceName,
                    sourceIsDirectory);
            }

            return true;
        }

        private static PhysicalMoveCompletion ExecutePhysicalFolderMove(
            PendingPhysicalMove pending,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool sourceIsDirectory = Directory.Exists(pending.SourcePath);
            bool sourceIsFile = File.Exists(pending.SourcePath);
            if (!sourceIsDirectory && !sourceIsFile)
            {
                return new PhysicalMoveCompletion(false, false, pending.SourcePath, null, "源项目已不存在");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!FileOperationIdentityGuard.Matches(pending.SourcePath, pending.SourceIdentity))
            {
                return new PhysicalMoveCompletion(false, false, pending.SourcePath, null, "源项目已被替换，未执行移动");
            }

            if (!FileOperationIdentityGuard.Matches(
                    pending.TargetFolderPath,
                    pending.TargetFolderIdentity))
            {
                return new PhysicalMoveCompletion(false, false, pending.SourcePath, null, "目标文件夹已被替换，未执行移动");
            }

            if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
            {
                return new PhysicalMoveCompletion(false, false, pending.SourcePath, null, "预定目标位置已被占用，未执行移动");
            }

            if (sourceIsDirectory)
            {
                Directory.Move(pending.SourcePath, destinationPath);
            }
            else
            {
                File.Move(pending.SourcePath, destinationPath);
            }

            return new PhysicalMoveCompletion(true, false, pending.SourcePath, destinationPath, null);
        }

        private void QueueRecycleOperation(IReadOnlyList<RecycleRequest> requests)
        {
            if (requests.Count == 0)
            {
                return;
            }

            if (!EnsureFileOperationJournalAvailable("移到回收站"))
            {
                return;
            }

            foreach (RecycleRequest request in requests)
            {
                if (!FileOperationIdentityGuard.Matches(
                        request.FullPath,
                        request.ExpectedIdentity))
                {
                    StatusText.Text = $"无法确认“{request.DisplayName}”仍是当前项目，未排队删除";
                    return;
                }
            }

            string batchId = Guid.NewGuid().ToString("N");
            List<JournaledRecycleRequest> journaledRequests = requests
                .Select(request => new JournaledRecycleRequest(
                    request,
                    new FileOperationJournalEntry
                    {
                        BatchId = batchId,
                        Kind = FileOperationJournalKind.MoveToRecycleBin,
                        DisplayName = request.DisplayName,
                        SourcePath = request.FullPath,
                        ExpectedIdentity = request.ExpectedIdentity
                    }))
                .ToList();

            if (!TryReserveFileOperation(
                    requests.Select(request => request.FullPath),
                    "所选项目中已有文件操作正在进行",
                    out List<string> reservedKeys))
            {
                return;
            }

            if (!TryPersistQueuedJournalEntries(
                    journaledRequests.Select(request => request.JournalEntry).ToList(),
                    out string queueError))
            {
                ReleaseFileOperation(reservedKeys);
                StatusText.Text = "未排队回收站操作：操作账本无法安全写入";
                _diagnostics.Log($"RECYCLE queue journal failed error={queueError}");
                return;
            }

            RebuildDesktopIcons();
            StatusText.Text = requests.Count == 1
                ? $"已排队：正在把“{requests[0].DisplayName}”移到回收站…"
                : $"已排队：正在把 {requests.Count} 个项目移到回收站…";
            _ = CompleteRecycleOperationAsync(journaledRequests, reservedKeys);
        }

        private async Task CompleteRecycleOperationAsync(
            IReadOnlyList<JournaledRecycleRequest> requests,
            IReadOnlyList<string> reservedKeys)
        {
            RecycleBatchCompletion completion;
            try
            {
                completion = await _fileOperationService.Enqueue(
                    token => ExecuteJournaledRecycleOperation(requests, token),
                    _lifetimeCts.Token);
            }
            catch (OperationCanceledException)
            {
                completion = new RecycleBatchCompletion(
                    requests.Select(journaled => new RecycleItemCompletion(
                        journaled.Request.DisplayName,
                        journaled.Request.FullPath,
                        Success: false,
                        Canceled: true,
                        ErrorMessage: null)).ToList());
            }
            catch (Exception exception)
            {
                completion = new RecycleBatchCompletion(
                    requests.Select(journaled => new RecycleItemCompletion(
                        journaled.Request.DisplayName,
                        journaled.Request.FullPath,
                        Success: false,
                        Canceled: false,
                        exception.Message)).ToList());
            }

            if (_isClosing || Dispatcher.HasShutdownStarted)
            {
                return;
            }

            ReleaseFileOperation(reservedKeys);
            List<RecycleItemCompletion> succeeded = completion.Items.Where(item => item.Success).ToList();
            List<RecycleItemCompletion> failed = completion.Items.Where(item => !item.Success && !item.Canceled).ToList();
            List<RecycleItemCompletion> canceled = completion.Items.Where(item => item.Canceled).ToList();
            List<RecycleItemCompletion> unpersisted = completion.Items
                .Where(item => item.Success && !item.JournalPersisted)
                .ToList();
            foreach (RecycleItemCompletion item in succeeded)
            {
                RemoveDesktopItemAfterPhysicalOperation(item.DisplayName, item.FullPath);
                _diagnostics.Log($"RECYCLE success path={item.FullPath}");
            }

            RemoveCompletedRecycleItemsFromSelection(
                _selectedItemNames,
                succeeded.Select(item => item.DisplayName));
            RebuildDesktopIconsAndSaveLayout();
            RequestDesktopRefresh(clearIconCache: false, statusMessage: null);
            RequestRecycleBinStatusRefresh();

            if (failed.Count > 0 || unpersisted.Count > 0)
            {
                string details = string.Join(
                    "\n",
                    failed.Take(6).Select(item => $"• {item.DisplayName}：{item.ErrorMessage}"));
                if (failed.Count > 6)
                {
                    details += $"\n• 另有 {failed.Count - 6} 项失败";
                }
                if (canceled.Count > 0)
                {
                    details += $"\n• {canceled.Count} 项被取消或未完成";
                }
                if (unpersisted.Count > 0)
                {
                    details += $"\n• {unpersisted.Count} 项已从桌面移除，但账本终态未能保存；请在操作中心和回收站人工核对";
                }

                StatusText.Text = FormatFileOperationStatus(
                    $"回收站操作完成：成功 {succeeded.Count}，失败 {failed.Count}，取消 {canceled.Count}；逐项结果已记录");
                MessageBox.Show(
                    $"部分回收站操作需要人工核对：\n\n{details}",
                    "回收站操作需要核对",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else if (canceled.Count > 0)
            {
                StatusText.Text = FormatFileOperationStatus(
                    $"回收站操作完成：成功 {succeeded.Count}，取消 {canceled.Count}");
            }
            else if (succeeded.Count > 0)
            {
                StatusText.Text = FormatFileOperationStatus(
                    succeeded.Count == 1
                        ? $"已将“{succeeded[0].DisplayName}”移到回收站"
                        : $"已将 {succeeded.Count} 个项目移到回收站");
            }
            else
            {
                StatusText.Text = FormatFileOperationStatus("回收站操作已取消");
            }
        }

        private RecycleBatchCompletion ExecuteJournaledRecycleOperation(
            IReadOnlyList<JournaledRecycleRequest> requests,
            CancellationToken cancellationToken)
        {
            var results = new List<RecycleItemCompletion>(requests.Count);
            foreach (JournaledRecycleRequest journaled in requests)
            {
                RecycleRequest request = journaled.Request;
                RecycleItemCompletion? operationCompletion = null;
                FileOperationDispatchResult dispatch = DispatchJournaledOperation(
                    journaled.JournalEntry,
                    () =>
                    {
                        operationCompletion = ExecuteRecycleItem(request, cancellationToken);
                        if (operationCompletion.Canceled)
                        {
                            return new FileOperationExecutionOutcome(
                                FileOperationJournalState.Canceled,
                                ErrorCode: "canceled",
                                ErrorMessage: operationCompletion.ErrorMessage);
                        }
                        return operationCompletion.Success
                            ? FileOperationExecutionOutcome.Success()
                            : FileOperationExecutionOutcome.Failure(
                                "recycle_failed",
                                operationCompletion.ErrorMessage ?? "项目未能移到回收站。");
                    });

                if (operationCompletion == null)
                {
                    operationCompletion = new RecycleItemCompletion(
                        request.DisplayName,
                        request.FullPath,
                        Success: false,
                        Canceled: false,
                        ErrorMessage: dispatch.ErrorMessage ?? "操作账本未能写入，真实删除没有执行。");
                }

                results.Add(operationCompletion with
                {
                    JournalPersisted = dispatch.JournalPersisted
                });
            }

            return new RecycleBatchCompletion(results);
        }

        private static RecycleItemCompletion ExecuteRecycleItem(
            RecycleRequest request,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new RecycleItemCompletion(
                    request.DisplayName,
                    request.FullPath,
                    Success: false,
                    Canceled: true,
                    ErrorMessage: null);
            }

            bool isDirectory = Directory.Exists(request.FullPath);
            bool isFile = File.Exists(request.FullPath);
            if (!isDirectory && !isFile)
            {
                return new RecycleItemCompletion(
                        request.DisplayName,
                        request.FullPath,
                        Success: false,
                        Canceled: false,
                        ErrorMessage: "项目已不存在");
            }

            if (!FileOperationIdentityGuard.Matches(
                    request.FullPath,
                    request.ExpectedIdentity))
            {
                return new RecycleItemCompletion(
                        request.DisplayName,
                        request.FullPath,
                        Success: false,
                        Canceled: false,
                        ErrorMessage: "项目已被替换，未执行删除");
            }

            try
            {
                if (isDirectory)
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                        request.FullPath,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                        Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing);
                }
                else
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                        request.FullPath,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                        Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing);
                }

                bool removed = !File.Exists(request.FullPath) && !Directory.Exists(request.FullPath);
                return new RecycleItemCompletion(
                    request.DisplayName,
                    request.FullPath,
                    Success: removed,
                    Canceled: !removed,
                    ErrorMessage: removed ? null : "操作未完成或已被取消");
            }
            catch (OperationCanceledException)
            {
                return new RecycleItemCompletion(
                    request.DisplayName,
                    request.FullPath,
                    Success: false,
                    Canceled: true,
                    ErrorMessage: null);
            }
            catch (Exception exception)
            {
                return new RecycleItemCompletion(
                    request.DisplayName,
                    request.FullPath,
                    Success: false,
                    Canceled: false,
                    exception.Message);
            }
        }

        private bool QueueUndoLastFileMove()
        {
            if (_fileMoveHistory.First?.Value is not FileMoveUndoRecord record)
            {
                UpdateUndoFileMoveButton();
                StatusText.Text = "没有可撤销的文件移动";
                return false;
            }

            if (!EnsureFileOperationJournalAvailable("撤销文件移动"))
            {
                return false;
            }

            var journalEntry = new FileOperationJournalEntry
            {
                Kind = FileOperationJournalKind.UndoMove,
                DisplayName = record.DisplayName,
                SourcePath = record.DestinationPath,
                DestinationPath = record.SourcePath,
                ExpectedIdentity = record.ItemIdentity,
                UndoOfEntryId = record.JournalEntryId
            };

            if (HasPendingFileOperations ||
                !TryReserveFileOperation(
                    [record.DestinationPath, record.SourcePath],
                    "已有文件操作正在进行，请完成后再撤销",
                    out List<string> reservedKeys))
            {
                return false;
            }

            if (!TryPersistQueuedJournalEntries([journalEntry], out string queueError))
            {
                ReleaseFileOperation(reservedKeys);
                StatusText.Text = "未排队撤销：操作账本无法安全写入";
                _diagnostics.Log($"UNDO_MOVE queue journal failed error={queueError}");
                return false;
            }

            StatusText.Text = $"已排队：正在撤销“{record.DisplayName}”的文件移动…";
            _ = CompleteUndoFileMoveAsync(record, journalEntry, reservedKeys);
            return true;
        }

        private async Task CompleteUndoFileMoveAsync(
            FileMoveUndoRecord record,
            FileOperationJournalEntry journalEntry,
            IReadOnlyList<string> reservedKeys)
        {
            JournaledUndoMoveCompletion completion;
            try
            {
                completion = await _fileOperationService.Enqueue(
                    token => ExecuteJournaledUndoFileMove(
                        record,
                        journalEntry,
                        token),
                    _lifetimeCts.Token);
            }
            catch (OperationCanceledException)
            {
                completion = new JournaledUndoMoveCompletion(
                    new UndoMoveCompletion(UndoMoveCompletionKind.Canceled, null),
                    JournalEntry: null,
                    JournalPersisted: false,
                    JournalError: "任务在写入运行状态前被取消。");
            }
            catch (Exception exception)
            {
                completion = new JournaledUndoMoveCompletion(
                    new UndoMoveCompletion(UndoMoveCompletionKind.Failed, exception.Message),
                    JournalEntry: null,
                    JournalPersisted: false,
                    JournalError: exception.Message);
            }

            if (_isClosing || Dispatcher.HasShutdownStarted)
            {
                return;
            }

            ReleaseFileOperation(reservedKeys);
            UndoMoveCompletion operation = completion.Operation;
            if (operation.Kind == UndoMoveCompletionKind.Success)
            {
                RestoreLayoutAfterUndo(record);
                SaveLayout();
                string status = completion.JournalPersisted &&
                                completion.JournalEntry?.State == FileOperationJournalState.Succeeded
                    ? $"已撤销“{record.DisplayName}”的文件移动"
                    : $"“{record.DisplayName}”已移回原位置，但账本终态未能可靠保存；请在操作中心核对";
                RequestDesktopRefresh(clearIconCache: true, statusMessage: status);
                _diagnostics.Log($"UNDO_MOVE success source={record.SourcePath}, destination={record.DestinationPath}, journalPersisted={completion.JournalPersisted}");
                return;
            }

            if (operation.Kind == UndoMoveCompletionKind.DestinationMissing)
            {
                if (_fileMoveHistory.First?.Value == record)
                {
                    _fileMoveHistory.RemoveFirst();
                }
                UpdateUndoFileMoveButton();
                StatusText.Text = FormatFileOperationStatus("撤销失败：移动后的项目已不存在");
                _diagnostics.Log($"UNDO_MOVE missing destination={record.DestinationPath}");
                return;
            }

            if (operation.Kind == UndoMoveCompletionKind.DestinationChanged)
            {
                if (_fileMoveHistory.First?.Value == record)
                {
                    _fileMoveHistory.RemoveFirst();
                }
                UpdateUndoFileMoveButton();
                StatusText.Text = FormatFileOperationStatus("撤销失败：移动后的项目已被替换");
                _diagnostics.Log($"UNDO_MOVE identity changed destination={record.DestinationPath}");
                return;
            }

            if (operation.Kind == UndoMoveCompletionKind.SourceOccupied)
            {
                StatusText.Text = FormatFileOperationStatus("撤销失败：原位置存在同名项目");
                MessageBox.Show(
                    $"原位置已经存在同名项目，无法撤销：\n\n{record.SourcePath}",
                    "无法撤销文件移动",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (operation.Kind == UndoMoveCompletionKind.Canceled)
            {
                StatusText.Text = FormatFileOperationStatus("撤销操作已取消");
                return;
            }

            string error = operation.ErrorMessage ?? "未知错误";
            StatusText.Text = FormatFileOperationStatus("无法撤销文件移动");
            _diagnostics.Log($"UNDO_MOVE failed error={error}");
            MessageBox.Show(
                $"无法撤销文件移动：{error}",
                "撤销失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private JournaledUndoMoveCompletion ExecuteJournaledUndoFileMove(
            FileMoveUndoRecord record,
            FileOperationJournalEntry journalEntry,
            CancellationToken cancellationToken)
        {
            UndoMoveCompletion? operationCompletion = null;
            FileOperationDispatchResult dispatch = DispatchJournaledOperation(
                journalEntry,
                () =>
                {
                    operationCompletion = ExecuteUndoFileMove(record, cancellationToken);
                    if (operationCompletion.Kind == UndoMoveCompletionKind.Success)
                    {
                        if (!FileOperationIdentityGuard.TryCapture(
                                record.SourcePath,
                                out string resultIdentity) ||
                            !string.Equals(
                                record.ItemIdentity,
                                resultIdentity,
                                StringComparison.Ordinal))
                        {
                            return new FileOperationExecutionOutcome(
                                FileOperationJournalState.Uncertain,
                                record.SourcePath,
                                ResultIdentity: resultIdentity,
                                ErrorCode: "undo_result_identity_unverified",
                                ErrorMessage: "撤销移动已发生，但无法核验移回后项目身份。");
                        }
                        return FileOperationExecutionOutcome.Success(
                            record.SourcePath,
                            resultIdentity);
                    }
                    if (operationCompletion.Kind == UndoMoveCompletionKind.Canceled)
                    {
                        return new FileOperationExecutionOutcome(
                            FileOperationJournalState.Canceled,
                            ErrorCode: "canceled",
                            ErrorMessage: operationCompletion.ErrorMessage);
                    }

                    return FileOperationExecutionOutcome.Failure(
                        operationCompletion.Kind.ToString(),
                        operationCompletion.ErrorMessage ?? GetUndoFailureMessage(
                            operationCompletion.Kind));
                });

            operationCompletion ??= new UndoMoveCompletion(
                dispatch.Entry.State == FileOperationJournalState.Canceled
                    ? UndoMoveCompletionKind.Canceled
                    : UndoMoveCompletionKind.Failed,
                dispatch.ErrorMessage ?? "操作账本未能写入，真实撤销没有执行。");
            return new JournaledUndoMoveCompletion(
                operationCompletion,
                dispatch.Entry,
                dispatch.JournalPersisted,
                dispatch.ErrorMessage);
        }

        private static string GetUndoFailureMessage(UndoMoveCompletionKind kind) => kind switch
        {
            UndoMoveCompletionKind.DestinationMissing => "移动后的项目已不存在。",
            UndoMoveCompletionKind.DestinationChanged => "移动后的项目已被替换。",
            UndoMoveCompletionKind.SourceOccupied => "原位置存在同名项目。",
            _ => "文件移动撤销失败。"
        };

        private static UndoMoveCompletion ExecuteUndoFileMove(
            FileMoveUndoRecord record,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool destinationIsDirectory = Directory.Exists(record.DestinationPath);
            bool destinationIsFile = File.Exists(record.DestinationPath);
            if (!destinationIsDirectory && !destinationIsFile)
            {
                return new UndoMoveCompletion(UndoMoveCompletionKind.DestinationMissing, null);
            }

            if (!FileOperationIdentityGuard.Matches(
                    record.DestinationPath,
                    record.ItemIdentity))
            {
                return new UndoMoveCompletion(UndoMoveCompletionKind.DestinationChanged, null);
            }

            if (Directory.Exists(record.SourcePath) || File.Exists(record.SourcePath))
            {
                return new UndoMoveCompletion(UndoMoveCompletionKind.SourceOccupied, null);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(record.SourcePath)!);
            cancellationToken.ThrowIfCancellationRequested();
            if (!FileOperationIdentityGuard.Matches(
                    record.DestinationPath,
                    record.ItemIdentity))
            {
                return new UndoMoveCompletion(UndoMoveCompletionKind.DestinationChanged, null);
            }

            if (Directory.Exists(record.SourcePath) || File.Exists(record.SourcePath))
            {
                return new UndoMoveCompletion(UndoMoveCompletionKind.SourceOccupied, null);
            }

            if (destinationIsDirectory)
            {
                Directory.Move(record.DestinationPath, record.SourcePath);
            }
            else
            {
                File.Move(record.DestinationPath, record.SourcePath);
            }

            return new UndoMoveCompletion(UndoMoveCompletionKind.Success, null);
        }

        private void RestoreLayoutAfterUndo(FileMoveUndoRecord record)
        {
            if (_fileMoveHistory.First?.Value == record)
            {
                _fileMoveHistory.RemoveFirst();
            }
            UpdateUndoFileMoveButton();
            RestoreItemMetadataAfterUndo(record);
            if (RestoreWorkspacePlacementsAfterUndo(record))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(record.SourceWorkspaceId) &&
                !string.Equals(
                    record.SourceWorkspaceId,
                    _appLayout.ActiveWorkspaceId,
                    StringComparison.OrdinalIgnoreCase))
            {
                WorkspaceProfileInfo? sourceWorkspace = _appLayout.Workspaces.FirstOrDefault(workspace =>
                    workspace.Id.Equals(record.SourceWorkspaceId, StringComparison.OrdinalIgnoreCase));
                if (sourceWorkspace != null)
                {
                    RestoreWorkspaceSnapshotAfterUndo(record, sourceWorkspace.Layout);
                    return;
                }
            }

            bool canceledAutoCategory = record.SourceGroupSnapshot?.IsAutoCategory == true &&
                !string.IsNullOrWhiteSpace(record.SourceGroupId) &&
                _canceledAutoCategoryGroupIds.Contains(record.SourceGroupId);
            GroupInfo? originalGroup = !canceledAutoCategory && !string.IsNullOrWhiteSpace(record.SourceGroupId)
                ? _appLayout.Groups.FirstOrDefault(group =>
                    group.Id.Equals(record.SourceGroupId, StringComparison.OrdinalIgnoreCase))
                : null;
            if (originalGroup == null && record.SourceGroupSnapshot != null && !canceledAutoCategory)
            {
                originalGroup = record.SourceGroupSnapshot;
                originalGroup.ItemNames = new List<string>();
                _appLayout.Groups.Add(originalGroup);
                ClampGroupToCanvas(originalGroup);
            }

            if (originalGroup != null)
            {
                if (!originalGroup.ItemNames.Contains(record.DisplayName, StringComparer.OrdinalIgnoreCase))
                {
                    int restoreIndex = Math.Clamp(
                        record.SourceGroupItemIndex,
                        0,
                        originalGroup.ItemNames.Count);
                    originalGroup.ItemNames.Insert(restoreIndex, record.DisplayName);
                }
                if (record.SourceGroupSnapshot?.ManuallyAssignedItemNames.Contains(
                        record.DisplayName,
                        StringComparer.OrdinalIgnoreCase) == true &&
                    !originalGroup.ManuallyAssignedItemNames.Contains(
                        record.DisplayName,
                        StringComparer.OrdinalIgnoreCase))
                {
                    originalGroup.ManuallyAssignedItemNames.Add(record.DisplayName);
                }
                _appLayout.FreeIcons.Remove(record.DisplayName);
            }
            else
            {
                IconPosition requestedPosition = record.FreePosition != null
                    ? ClonePosition(record.FreePosition)
                    : record.AutoClassificationOriginalPosition != null
                        ? ClonePosition(record.AutoClassificationOriginalPosition)
                        : CreateUndoFallbackPosition(record);
                ClampIconPosition(requestedPosition);
                _appLayout.FreeIcons[record.DisplayName] = _appLayout.SnapToGrid
                    ? FindAlignedIconPosition(record.DisplayName, requestedPosition) ?? requestedPosition
                    : requestedPosition;
            }

            if (!canceledAutoCategory &&
                record.AutoClassificationOriginalPosition != null &&
                !_appLayout.AutoClassificationOriginalPositions.ContainsKey(record.DisplayName))
            {
                _appLayout.AutoClassificationOriginalPositions[record.DisplayName] =
                    ClonePosition(record.AutoClassificationOriginalPosition);
            }
        }

        private void RestoreWorkspaceSnapshotAfterUndo(
            FileMoveUndoRecord record,
            WorkspaceLayoutState layout)
        {
            bool canceledAutoCategory = record.SourceGroupSnapshot?.IsAutoCategory == true &&
                !string.IsNullOrWhiteSpace(record.SourceGroupId) &&
                _canceledAutoCategoryGroupIds.Contains(record.SourceGroupId);
            GroupInfo? originalGroup = !canceledAutoCategory && !string.IsNullOrWhiteSpace(record.SourceGroupId)
                ? layout.Groups.FirstOrDefault(group =>
                    group.Id.Equals(record.SourceGroupId, StringComparison.OrdinalIgnoreCase))
                : null;
            if (originalGroup == null && record.SourceGroupSnapshot != null && !canceledAutoCategory)
            {
                GroupInfo snapshot = CreateUndoGroupSnapshot(record.SourceGroupSnapshot)!;
                layout.Groups.Add(snapshot);
                originalGroup = snapshot;
            }

            if (originalGroup != null)
            {
                if (!originalGroup.ItemNames.Contains(record.DisplayName, StringComparer.OrdinalIgnoreCase))
                {
                    int restoreIndex = Math.Clamp(
                        record.SourceGroupItemIndex,
                        0,
                        originalGroup.ItemNames.Count);
                    originalGroup.ItemNames.Insert(restoreIndex, record.DisplayName);
                }
                if (record.SourceGroupSnapshot?.ManuallyAssignedItemNames.Contains(
                        record.DisplayName,
                        StringComparer.OrdinalIgnoreCase) == true &&
                    !originalGroup.ManuallyAssignedItemNames.Contains(
                        record.DisplayName,
                        StringComparer.OrdinalIgnoreCase))
                {
                    originalGroup.ManuallyAssignedItemNames.Add(record.DisplayName);
                }
                layout.FreeIcons.Remove(record.DisplayName);
            }
            else
            {
                layout.FreeIcons[record.DisplayName] = record.FreePosition != null
                    ? ClonePosition(record.FreePosition)
                    : record.AutoClassificationOriginalPosition != null
                        ? ClonePosition(record.AutoClassificationOriginalPosition)
                        : CreateUndoFallbackPosition(record);
            }

            if (!canceledAutoCategory && record.AutoClassificationOriginalPosition != null)
            {
                layout.AutoClassificationOriginalPositions[record.DisplayName] =
                    ClonePosition(record.AutoClassificationOriginalPosition);
            }
        }

        private static IconPosition CreateUndoFallbackPosition(FileMoveUndoRecord record)
        {
            GroupInfo? group = record.SourceGroupSnapshot;
            if (group == null)
            {
                return new IconPosition { X = GridOriginX, Y = GridOriginY };
            }

            int index = Math.Max(0, record.SourceGroupItemIndex);
            return new IconPosition
            {
                X = group.X + (index % 3) * IconCellWidth,
                Y = group.Y + 36 + (index / 3) * IconCellHeight
            };
        }
    }
}
