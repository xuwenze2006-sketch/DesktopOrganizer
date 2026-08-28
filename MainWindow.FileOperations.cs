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

        private sealed record RecycleRequest(
            string DisplayName,
            string FullPath,
            string ExpectedIdentity);

        private sealed record RecycleItemCompletion(
            string DisplayName,
            string FullPath,
            bool Success,
            bool Canceled,
            string? ErrorMessage);

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
            _selectedItemNames.Remove(displayName);
            InvalidateIconCacheLocations([fullPath]);
        }

        private PhysicalFolderMoveResult QueuePhysicalFolderMove(
            PendingPhysicalMove pending)
        {
            if (!FileOperationIdentityGuard.Matches(
                    pending.SourcePath,
                    pending.SourceIdentity))
            {
                StatusText.Text = $"无法确认“{pending.DisplayName}”仍是当前项目，未排队移动";
                return PhysicalFolderMoveResult.Rejected;
            }

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
            _ = CompletePhysicalFolderMoveAsync(pending, reservedKeys);
            return PhysicalFolderMoveResult.Queued;
        }

        private async Task CompletePhysicalFolderMoveAsync(
            PendingPhysicalMove pending,
            IReadOnlyList<string> reservedKeys)
        {
            PhysicalMoveCompletion completion;
            try
            {
                completion = await _fileOperationService.Enqueue(
                    token => ExecutePhysicalFolderMove(pending, token),
                    _lifetimeCts.Token);
            }
            catch (OperationCanceledException)
            {
                completion = new PhysicalMoveCompletion(
                    Success: false,
                    Canceled: true,
                    SourcePath: pending.SourcePath,
                    DestinationPath: null,
                    ErrorMessage: null);
            }
            catch (Exception exception)
            {
                completion = new PhysicalMoveCompletion(
                    Success: false,
                    Canceled: false,
                    SourcePath: pending.SourcePath,
                    DestinationPath: null,
                    ErrorMessage: exception.Message);
            }

            if (_isClosing || Dispatcher.HasShutdownStarted)
            {
                return;
            }

            ReleaseFileOperation(reservedKeys);
            if (completion.Success && completion.DestinationPath != null)
            {
                PushFileMoveHistory(new FileMoveUndoRecord(
                    pending.DisplayName,
                    pending.SourcePath,
                    completion.DestinationPath,
                    _appLayout.ActiveWorkspaceId,
                    pending.SourceGroupId,
                    pending.SourceGroupSnapshot,
                    pending.SourceGroupItemIndex,
                    pending.FreePosition,
                    pending.AutoClassificationOriginalPosition,
                    pending.SourceIdentity,
                    DateTime.UtcNow));

                RemoveDesktopItemAfterPhysicalOperation(
                    pending.DisplayName,
                    pending.SourcePath);
                RebuildDesktopIconsAndSaveLayout();
                StatusText.Text = FormatFileOperationStatus(
                    $"已将“{pending.DisplayName}”移入真实文件夹“{Path.GetFileName(pending.TargetFolderPath)}”；可撤销");
                _diagnostics.Log($"MOVE success source={pending.SourcePath}, destination={completion.DestinationPath}");
                RequestDesktopRefresh(clearIconCache: false, statusMessage: null);
                return;
            }

            RebuildDesktopIcons();
            if (completion.Canceled)
            {
                StatusText.Text = FormatFileOperationStatus($"已取消移动“{pending.DisplayName}”");
                return;
            }

            string error = string.IsNullOrWhiteSpace(completion.ErrorMessage)
                ? "文件或目标文件夹的状态已经改变"
                : completion.ErrorMessage;
            StatusText.Text = FormatFileOperationStatus($"未能移动“{pending.DisplayName}”");
            _diagnostics.Log($"MOVE failed source={pending.SourcePath}, target={pending.TargetFolderPath}, error={error}");
            MessageBox.Show(
                $"无法移动该项目：{error}",
                "移动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            RequestDesktopRefresh(clearIconCache: false, statusMessage: null);
        }

        private static PhysicalMoveCompletion ExecutePhysicalFolderMove(
            PendingPhysicalMove pending,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool sourceIsDirectory = Directory.Exists(pending.SourcePath);
            bool sourceIsFile = File.Exists(pending.SourcePath);
            if (!sourceIsDirectory && !sourceIsFile)
            {
                return new PhysicalMoveCompletion(false, false, pending.SourcePath, null, "源项目已不存在");
            }

            if (!FileOperationIdentityGuard.Matches(pending.SourcePath, pending.SourceIdentity))
            {
                return new PhysicalMoveCompletion(false, false, pending.SourcePath, null, "源项目已被替换，未执行移动");
            }

            if (!Directory.Exists(pending.TargetFolderPath))
            {
                return new PhysicalMoveCompletion(false, false, pending.SourcePath, null, "目标文件夹已不存在");
            }

            if (!FileOperationIdentityGuard.Matches(
                    pending.TargetFolderPath,
                    pending.TargetFolderIdentity))
            {
                return new PhysicalMoveCompletion(false, false, pending.SourcePath, null, "目标文件夹已被替换，未执行移动");
            }

            string sourceName = Path.GetFileName(Path.TrimEndingDirectorySeparator(pending.SourcePath));
            string destinationPath = Path.Combine(pending.TargetFolderPath, sourceName);
            if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
            {
                if (!pending.AllowAutoRename)
                {
                    return new PhysicalMoveCompletion(false, false, pending.SourcePath, null, "目标文件夹中已存在同名项目");
                }

                destinationPath = GetUniqueDestinationPath(
                    pending.TargetFolderPath,
                    sourceName,
                    sourceIsDirectory);
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

            if (!TryReserveFileOperation(
                    requests.Select(request => request.FullPath),
                    "所选项目中已有文件操作正在进行",
                    out List<string> reservedKeys))
            {
                return;
            }

            RebuildDesktopIcons();
            StatusText.Text = requests.Count == 1
                ? $"已排队：正在把“{requests[0].DisplayName}”移到回收站…"
                : $"已排队：正在把 {requests.Count} 个项目移到回收站…";
            _ = CompleteRecycleOperationAsync(requests, reservedKeys);
        }

        private async Task CompleteRecycleOperationAsync(
            IReadOnlyList<RecycleRequest> requests,
            IReadOnlyList<string> reservedKeys)
        {
            RecycleBatchCompletion completion;
            try
            {
                completion = await _fileOperationService.Enqueue(
                    token => ExecuteRecycleOperation(requests, token),
                    _lifetimeCts.Token);
            }
            catch (OperationCanceledException)
            {
                completion = new RecycleBatchCompletion(
                    requests.Select(request => new RecycleItemCompletion(
                        request.DisplayName,
                        request.FullPath,
                        Success: false,
                        Canceled: true,
                        ErrorMessage: null)).ToList());
            }
            catch (Exception exception)
            {
                completion = new RecycleBatchCompletion(
                    requests.Select(request => new RecycleItemCompletion(
                        request.DisplayName,
                        request.FullPath,
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
            foreach (RecycleItemCompletion item in succeeded)
            {
                RemoveDesktopItemAfterPhysicalOperation(item.DisplayName, item.FullPath);
                _diagnostics.Log($"RECYCLE success path={item.FullPath}");
            }

            _selectedItemNames.Clear();
            RebuildDesktopIconsAndSaveLayout();
            RequestDesktopRefresh(clearIconCache: false, statusMessage: null);
            RequestRecycleBinStatusRefresh();

            if (failed.Count > 0)
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

                StatusText.Text = FormatFileOperationStatus(
                    $"回收站操作完成：成功 {succeeded.Count}，失败 {failed.Count}，取消 {canceled.Count}");
                MessageBox.Show(
                    $"部分项目未能移到回收站：\n\n{details}",
                    "部分删除失败",
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

        private static RecycleBatchCompletion ExecuteRecycleOperation(
            IReadOnlyList<RecycleRequest> requests,
            CancellationToken cancellationToken)
        {
            var results = new List<RecycleItemCompletion>(requests.Count);
            foreach (RecycleRequest request in requests)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    results.Add(new RecycleItemCompletion(
                        request.DisplayName,
                        request.FullPath,
                        Success: false,
                        Canceled: true,
                        ErrorMessage: null));
                    continue;
                }

                bool isDirectory = Directory.Exists(request.FullPath);
                bool isFile = File.Exists(request.FullPath);
                if (!isDirectory && !isFile)
                {
                    results.Add(new RecycleItemCompletion(
                        request.DisplayName,
                        request.FullPath,
                        Success: false,
                        Canceled: false,
                        ErrorMessage: "项目已不存在"));
                    continue;
                }

                if (!FileOperationIdentityGuard.Matches(
                        request.FullPath,
                        request.ExpectedIdentity))
                {
                    results.Add(new RecycleItemCompletion(
                        request.DisplayName,
                        request.FullPath,
                        Success: false,
                        Canceled: false,
                        ErrorMessage: "项目已被替换，未执行删除"));
                    continue;
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
                    results.Add(new RecycleItemCompletion(
                        request.DisplayName,
                        request.FullPath,
                        Success: removed,
                        Canceled: !removed,
                        ErrorMessage: removed ? null : "操作未完成或已被取消"));
                }
                catch (OperationCanceledException)
                {
                    results.Add(new RecycleItemCompletion(
                        request.DisplayName,
                        request.FullPath,
                        Success: false,
                        Canceled: true,
                        ErrorMessage: null));
                }
                catch (Exception exception)
                {
                    results.Add(new RecycleItemCompletion(
                        request.DisplayName,
                        request.FullPath,
                        Success: false,
                        Canceled: false,
                        exception.Message));
                }
            }

            return new RecycleBatchCompletion(results);
        }

        private bool QueueUndoLastFileMove()
        {
            if (_fileMoveHistory.First?.Value is not FileMoveUndoRecord record)
            {
                UpdateUndoFileMoveButton();
                StatusText.Text = "没有可撤销的文件移动";
                return false;
            }

            if (HasPendingFileOperations ||
                !TryReserveFileOperation(
                    [record.DestinationPath, record.SourcePath],
                    "已有文件操作正在进行，请完成后再撤销",
                    out List<string> reservedKeys))
            {
                return false;
            }

            StatusText.Text = $"已排队：正在撤销“{record.DisplayName}”的文件移动…";
            _ = CompleteUndoFileMoveAsync(record, reservedKeys);
            return true;
        }

        private async Task CompleteUndoFileMoveAsync(
            FileMoveUndoRecord record,
            IReadOnlyList<string> reservedKeys)
        {
            UndoMoveCompletion completion;
            try
            {
                completion = await _fileOperationService.Enqueue(
                    token => ExecuteUndoFileMove(record, token),
                    _lifetimeCts.Token);
            }
            catch (OperationCanceledException)
            {
                completion = new UndoMoveCompletion(UndoMoveCompletionKind.Canceled, null);
            }
            catch (Exception exception)
            {
                completion = new UndoMoveCompletion(UndoMoveCompletionKind.Failed, exception.Message);
            }

            if (_isClosing || Dispatcher.HasShutdownStarted)
            {
                return;
            }

            ReleaseFileOperation(reservedKeys);
            if (completion.Kind == UndoMoveCompletionKind.Success)
            {
                RestoreLayoutAfterUndo(record);
                SaveLayout();
                RequestDesktopRefresh(clearIconCache: true, statusMessage: $"已撤销“{record.DisplayName}”的文件移动");
                _diagnostics.Log($"UNDO_MOVE success source={record.SourcePath}, destination={record.DestinationPath}");
                return;
            }

            if (completion.Kind == UndoMoveCompletionKind.DestinationMissing)
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

            if (completion.Kind == UndoMoveCompletionKind.DestinationChanged)
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

            if (completion.Kind == UndoMoveCompletionKind.SourceOccupied)
            {
                StatusText.Text = FormatFileOperationStatus("撤销失败：原位置存在同名项目");
                MessageBox.Show(
                    $"原位置已经存在同名项目，无法撤销：\n\n{record.SourcePath}",
                    "无法撤销文件移动",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (completion.Kind == UndoMoveCompletionKind.Canceled)
            {
                StatusText.Text = FormatFileOperationStatus("撤销操作已取消");
                return;
            }

            string error = completion.ErrorMessage ?? "未知错误";
            StatusText.Text = FormatFileOperationStatus("无法撤销文件移动");
            _diagnostics.Log($"UNDO_MOVE failed error={error}");
            MessageBox.Show(
                $"无法撤销文件移动：{error}",
                "撤销失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

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
