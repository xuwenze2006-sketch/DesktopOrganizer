// 布局持久化与崩溃恢复
namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private volatile bool _layoutWriteProtected;

        private void ProtectLayoutWrites(string reason)
        {
            _layoutWriteProtected = true;
            _layoutDirty = true;
            _diagnostics.Log($"LAYOUT write protected: {reason}");
            StatusText.Text = "原布局无法安全读取，本次布局变更不会保存；请关闭程序后重试";
        }

        private void LoadLayout()
        {
            PendingExitLayoutRecoveryResult recovery =
                PendingExitLayoutRecovery.ReadAndPromote(
                    _layoutExitRecoveryPath,
                    _layoutFilePath);

            if (recovery.State == PendingExitLayoutRecoveryState.Promoted)
            {
                _diagnostics.Log("LAYOUT recovered pending exit snapshot");
            }
            else if (recovery.State == PendingExitLayoutRecoveryState.Deferred)
            {
                // 主布局不可读时，无法判断恢复快照是否比它更新，不能随后反向覆盖主文件。
                ProtectLayoutWrites("pending snapshot could not be compared/promoted to main layout");
                _diagnostics.Log("LAYOUT pending exit snapshot loaded; promotion deferred");
            }
            else if (recovery.State == PendingExitLayoutRecoveryState.Superseded)
            {
                _diagnostics.Log("LAYOUT ignored older pending exit snapshot");
            }
            else if (recovery.State == PendingExitLayoutRecoveryState.Conflict)
            {
                _preservePendingExitRecovery = true;
                _diagnostics.Log("LAYOUT preserved ambiguous pending exit snapshot");
            }
            else if ((recovery.State == PendingExitLayoutRecoveryState.Invalid ||
                      recovery.State == PendingExitLayoutRecoveryState.Unavailable) &&
                     File.Exists(_layoutExitRecoveryPath))
            {
                _preservePendingExitRecovery = true;
            }

            if (recovery.Json != null)
            {
                try
                {
                    ApplyLayoutJson(recovery.Json);
                    return;
                }
                catch (JsonException exception)
                    when (recovery.State == PendingExitLayoutRecoveryState.Deferred)
                {
                    Debug.WriteLine($"Pending exit layout content failed: {exception}");
                    bool quarantined = PendingExitLayoutRecovery.TryQuarantine(
                        _layoutExitRecoveryPath,
                        recovery.PendingIdentity);
                    if (quarantined)
                    {
                        _preservePendingExitRecovery = false;
                    }
                    else
                    {
                        _preservePendingExitRecovery = true;
                    }

                    LoadMainLayoutOrDefault();
                    return;
                }
                catch (Exception exception)
                {
                    Debug.WriteLine($"Pending exit layout application failed: {exception}");
                    if (recovery.State == PendingExitLayoutRecoveryState.Deferred)
                    {
                        _preservePendingExitRecovery = true;
                        LoadMainLayoutOrDefault();
                        return;
                    }

                    if (!BackupCorruptLayout())
                        ProtectLayoutWrites("failed to preserve unreadable layout");
                    _appLayout = new AppLayoutData();
                    return;
                }
            }

            LoadMainLayoutOrDefault();
        }

        private void LoadMainLayoutOrDefault()
        {
            string json;
            try
            {
                json = File.ReadAllText(_layoutFilePath, Encoding.UTF8);
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                _appLayout = new AppLayoutData();
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                ProtectLayoutWrites(exception.GetType().Name);
                _appLayout = new AppLayoutData();
                return;
            }

            try
            {
                ApplyLayoutJson(json);
            }
            catch
            {
                if (!BackupCorruptLayout())
                    ProtectLayoutWrites("failed to back up invalid layout");
                _appLayout = new AppLayoutData();
            }
        }

        private void ApplyLayoutJson(string json)
        {
            LayoutDeserializationResult deserialized = LayoutJsonSerializer.Deserialize(json);
            _appLayout = deserialized.Layout;
            _layoutSaveGeneration = Math.Max(
                _layoutSaveGeneration,
                _appLayout.SaveGeneration);

            NormalizeLayout();
            if (ReconcileLoadedLayoutCoordinateSpace(deserialized.SerializedVersion))
            {
                SaveLayout();
            }
        }

        private bool BackupCorruptLayout()
        {
            try
            {
                string directory = Path.GetDirectoryName(_layoutFilePath)!;
                string backup = Path.Combine(
                    directory,
                    $"layout.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
                File.Copy(_layoutFilePath, backup, overwrite: false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void SaveLayout()
        {
            if (_layoutWriteProtected)
            {
                _layoutDirty = true;
                return;
            }
            if (_isClosing)
            {
                SaveLayoutNow();
                return;
            }

            _layoutDirty = true;
            _layoutSaveTimer.Stop();
            _layoutSaveTimer.Start();
        }

        private void LayoutSaveTimer_Tick(object? sender, EventArgs e)
        {
            _layoutSaveTimer.Stop();
            if (_layoutDirty && !_isClosing)
            {
                QueueLayoutWrite();
            }
        }

        private void QueueLayoutWrite()
        {
            if (_layoutWriteProtected)
            {
                _layoutDirty = true;
                return;
            }
            string json;
            try
            {
                PrepareLayoutForPersistence();
                _appLayout.SaveGeneration = ++_layoutSaveGeneration;
                json = JsonSerializer.Serialize(_appLayout, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                _layoutDirty = false;
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Layout serialization failed: {exception}");
                _layoutDirty = true;
                return;
            }

            lock (_layoutWriteLock)
            {
                // 写入尚未开始时直接用最新快照覆盖旧快照；无需把每次拖动都写入磁盘。
                _pendingLayoutJson = json;
            }

            if (Interlocked.CompareExchange(ref _layoutWriterRunning, 1, 0) == 0)
            {
                // 空闲写锁的 WaitAsync 会同步完成，必须显式调度才能让磁盘 I/O 离开界面线程。
                _ = Task.Run(RunLayoutWriterAsync);
            }
        }

        private async Task RunLayoutWriterAsync()
        {
            try
            {
                while (!_isClosing)
                {
                    string? json;
                    lock (_layoutWriteLock)
                    {
                        json = _pendingLayoutJson;
                        _pendingLayoutJson = null;
                    }

                    if (json == null)
                    {
                        break;
                    }

                    await _layoutWriteGate.WaitAsync(_lifetimeCts.Token).ConfigureAwait(false);
                    try
                    {
                        // 等锁期间可能已开始退出，旧快照不能覆盖 Closing 保存的最终布局。
                        if (_isClosing || _layoutWriteProtected)
                        {
                            return;
                        }

                        _backgroundLayoutWriter(json);
                    }
                    finally
                    {
                        _layoutWriteGate.Release();
                    }
                }
            }
            catch (OperationCanceledException) when (_isClosing || _lifetimeCts.IsCancellationRequested)
            {
                // 正常退出；Closing 会同步写入最终布局。
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Layout background save failed: {exception}");
                if (!_isClosing && !Dispatcher.HasShutdownStarted)
                {
                    _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                    {
                        _layoutDirty = true;
                        StatusText.Text = "布局保存失败，将在下次操作时重试";
                    }));
                }
            }
            finally
            {
                Interlocked.Exchange(ref _layoutWriterRunning, 0);
                bool hasPendingLayout;
                lock (_layoutWriteLock)
                {
                    hasPendingLayout = _pendingLayoutJson != null;
                }

                if (!_isClosing && hasPendingLayout &&
                    Interlocked.CompareExchange(ref _layoutWriterRunning, 1, 0) == 0)
                {
                    _ = Task.Run(RunLayoutWriterAsync);
                }
            }
        }

        private void SaveLayoutNow()
        {
            if (_layoutWriteProtected)
            {
                _layoutDirty = true;
                return;
            }
            if (!_layoutDirty && File.Exists(_layoutFilePath) && !_isClosing)
            {
                return;
            }

            try
            {
                _layoutSaveTimer.Stop();
                PrepareLayoutForPersistence();
                _appLayout.SaveGeneration = ++_layoutSaveGeneration;
                string json = JsonSerializer.Serialize(_appLayout, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                lock (_layoutWriteLock)
                {
                    _pendingLayoutJson = null;
                }

                // 退出不能无限等待被杀毒软件、网络重定向目录或异常磁盘 I/O 占用的写锁。
                // 两秒内取得锁就正常原子保存；超时则写入独立恢复文件，下次启动优先恢复。
                bool acquired = _layoutWriteGate.Wait(TimeSpan.FromSeconds(2));
                if (acquired)
                {
                    try
                    {
                        WriteLayoutJsonAtomically(json);
                        TryDeletePendingExitLayout();
                    }
                    finally
                    {
                        _layoutWriteGate.Release();
                    }
                }
                else
                {
                    WritePendingExitLayout(json);
                    _diagnostics.Log("LAYOUT exit save gate timeout; recovery snapshot created");
                }

                _layoutDirty = false;
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Layout save failed: {exception}");
                _layoutDirty = true;
            }
        }

        private void WritePendingExitLayout(string json)
        {
            if (_layoutWriteProtected)
                throw new IOException("Original layout must be preserved until it can be read safely.");
            PendingExitLayoutRecovery.WriteSnapshot(_layoutExitRecoveryPath, json, _preservePendingExitRecovery);
        }

        private void TryDeletePendingExitLayout()
        {
            if (_preservePendingExitRecovery)
            {
                return;
            }

            try
            {
                if (File.Exists(_layoutExitRecoveryPath))
                {
                    File.Delete(_layoutExitRecoveryPath);
                }

            }
            catch
            {
                // 主布局已经写入成功；旧恢复文件下次启动会被同内容覆盖，不影响运行。
            }
        }

        private void WriteLayoutJsonAtomically(string json)
        {
            if (_layoutWriteProtected)
                throw new IOException("Original layout must be preserved until it can be read safely.");
            string directory = Path.GetDirectoryName(_layoutFilePath)!;
            Directory.CreateDirectory(directory);
            string temporaryPath = _layoutFilePath + ".tmp";
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, _layoutFilePath, overwrite: true);
        }

        // ==================== 崩溃恢复标记 ====================

        private void RecoverNativeIconsAfterPreviousCrash()
        {
            try
            {
                if (File.Exists(_sessionMarkerPath))
                {
                    NativeMethods.RestoreNativeDesktopIcons();
                    File.Delete(_sessionMarkerPath);
                }
            }
            catch
            {
                // 恢复失败时仍继续启动，用户可通过 Windows 桌面右键菜单手动恢复。
            }
        }

        private void WriteSessionMarker()
        {
            try
            {
                string directory = Path.GetDirectoryName(_sessionMarkerPath)!;
                Directory.CreateDirectory(directory);
                File.WriteAllText(_sessionMarkerPath, "restore-native-icons", Encoding.UTF8);
            }
            catch
            {
                // 标记文件仅用于额外保险，不影响主功能。
            }
        }

        private void TryDeleteSessionMarker()
        {
            try
            {
                if (File.Exists(_sessionMarkerPath))
                {
                    File.Delete(_sessionMarkerPath);
                }
            }
            catch
            {
                // 退出恢复已执行，标记删除失败不需要阻止退出。
            }
        }
    }
}
