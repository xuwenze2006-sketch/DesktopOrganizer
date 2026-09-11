namespace DesktopOrganizer
{
    internal enum FileOperationJournalLoadState
    {
        Missing,
        Ready,
        Protected
    }

    internal sealed record FileOperationJournalLoadResult(
        FileOperationJournalLoadState State,
        FileOperationJournalData? Journal,
        string? ErrorMessage)
    {
        public bool IsProtected => State == FileOperationJournalLoadState.Protected;
    }

    /// <summary>
    /// 与布局保存完全独立的账本原子存储。主文件损坏或无法读取后，
    /// 当前 Store 进入保护态，绝不会用空账本覆盖原文件。
    /// </summary>
    internal sealed class FileOperationJournalStore : IFileOperationJournalStore
    {
        private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

        private readonly object _gate = new();
        private readonly string _journalPath;
        private bool _mainInspected;
        private bool _writeProtected;
        private long _loadedGeneration;
        private string? _protectionError;

        public FileOperationJournalStore(string journalPath)
        {
            if (string.IsNullOrWhiteSpace(journalPath))
            {
                throw new ArgumentException("账本路径不能为空。", nameof(journalPath));
            }

            _journalPath = Path.GetFullPath(journalPath);
        }

        public string JournalPath => _journalPath;

        public FileOperationJournalLoadResult Load()
        {
            lock (_gate)
            {
                _mainInspected = true;
                if (!File.Exists(_journalPath))
                {
                    _writeProtected = false;
                    _loadedGeneration = 0;
                    _protectionError = null;
                    return new FileOperationJournalLoadResult(
                        FileOperationJournalLoadState.Missing,
                        new FileOperationJournalData(),
                        ErrorMessage: null);
                }

                try
                {
                    FileOperationJournalData journal = ReadAndValidateMainFile();
                    _writeProtected = false;
                    _loadedGeneration = journal.SaveGeneration;
                    _protectionError = null;
                    return new FileOperationJournalLoadResult(
                        FileOperationJournalLoadState.Ready,
                        journal,
                        ErrorMessage: null);
                }
                catch (Exception exception)
                {
                    EnterProtection(exception.Message);
                    return new FileOperationJournalLoadResult(
                        FileOperationJournalLoadState.Protected,
                        Journal: null,
                        ErrorMessage: _protectionError);
                }
            }
        }

        public bool TrySave(FileOperationJournalData journal, out string? errorMessage)
        {
            ArgumentNullException.ThrowIfNull(journal);
            lock (_gate)
            {
                if (!EnsureMainInspectedForWrite(out errorMessage))
                {
                    return false;
                }

                if (_writeProtected)
                {
                    errorMessage = _protectionError ?? "操作账本处于保护状态。";
                    return false;
                }

                if (!TryValidate(journal, out errorMessage))
                {
                    return false;
                }

                if (journal.SaveGeneration != _loadedGeneration)
                {
                    errorMessage = $"操作账本保存代次冲突：期望 {_loadedGeneration}，" +
                                   $"实际 {journal.SaveGeneration}。";
                    return false;
                }

                long nextGeneration = checked(_loadedGeneration + 1);
                string json;
                long originalGeneration = journal.SaveGeneration;
                try
                {
                    journal.SaveGeneration = nextGeneration;
                    json = JsonSerializer.Serialize(journal, SerializerOptions);
                }
                catch (Exception exception)
                {
                    errorMessage = $"操作账本序列化失败：{exception.Message}";
                    return false;
                }
                finally
                {
                    journal.SaveGeneration = originalGeneration;
                }

                string directory = Path.GetDirectoryName(_journalPath)!;
                string temporaryPath = Path.Combine(
                    directory,
                    $".{Path.GetFileName(_journalPath)}.{Guid.NewGuid():N}.tmp");
                try
                {
                    Directory.CreateDirectory(directory);
                    File.WriteAllText(
                        temporaryPath,
                        json,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    File.Move(temporaryPath, _journalPath, overwrite: true);
                    journal.SaveGeneration = nextGeneration;
                    _loadedGeneration = nextGeneration;
                    errorMessage = null;
                    return true;
                }
                catch (Exception exception)
                {
                    TryDeleteTemporaryFile(temporaryPath);
                    errorMessage = $"操作账本原子保存失败：{exception.Message}";
                    return false;
                }
            }
        }

        private bool EnsureMainInspectedForWrite(out string? errorMessage)
        {
            if (_mainInspected)
            {
                errorMessage = _writeProtected ? _protectionError : null;
                return !_writeProtected;
            }

            _mainInspected = true;
            if (!File.Exists(_journalPath))
            {
                _loadedGeneration = 0;
                errorMessage = null;
                return true;
            }

            try
            {
                FileOperationJournalData existing = ReadAndValidateMainFile();
                _loadedGeneration = existing.SaveGeneration;
                errorMessage = null;
                return true;
            }
            catch (Exception exception)
            {
                EnterProtection(exception.Message);
                errorMessage = _protectionError;
                return false;
            }
        }

        private FileOperationJournalData ReadAndValidateMainFile()
        {
            string json = File.ReadAllText(_journalPath, Encoding.UTF8);
            FileOperationJournalData journal = JsonSerializer.Deserialize<FileOperationJournalData>(
                json,
                SerializerOptions) ?? throw new InvalidDataException("操作账本反序列化返回 null。");
            if (!TryValidate(journal, out string? validationError))
            {
                throw new InvalidDataException(validationError);
            }
            return journal;
        }

        private static bool TryValidate(
            FileOperationJournalData journal,
            out string? errorMessage)
        {
            if (journal.Version != FileOperationJournalData.CurrentVersion)
            {
                errorMessage = $"不支持的操作账本版本 {journal.Version}。";
                return false;
            }
            if (journal.SaveGeneration < 0)
            {
                errorMessage = "操作账本保存代次不得为负数。";
                return false;
            }
            if (journal.Entries == null)
            {
                errorMessage = "操作账本缺少 Entries。";
                return false;
            }

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FileOperationJournalEntry? entry in journal.Entries)
            {
                if (entry == null)
                {
                    errorMessage = "操作账本包含 null 条目。";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(entry.Id) || !ids.Add(entry.Id))
                {
                    errorMessage = "操作账本条目 ID 为空或重复。";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(entry.BatchId))
                {
                    errorMessage = $"操作账本条目 {entry.Id} 缺少 BatchId。";
                    return false;
                }
                if (!Enum.IsDefined(entry.Kind) || !Enum.IsDefined(entry.State))
                {
                    errorMessage = $"操作账本条目 {entry.Id} 包含未知枚举值。";
                    return false;
                }
                if (entry.RequestedUtc == default)
                {
                    errorMessage = $"操作账本条目 {entry.Id} 缺少请求时间。";
                    return false;
                }
                if (entry.State is FileOperationJournalState.Running or
                    FileOperationJournalState.Succeeded or
                    FileOperationJournalState.Failed or
                    FileOperationJournalState.Uncertain &&
                    !entry.StartedUtc.HasValue)
                {
                    errorMessage = $"操作账本条目 {entry.Id} 缺少开始时间。";
                    return false;
                }
                if (FileOperationJournalStateMachine.IsTerminal(entry.State) &&
                    !entry.CompletedUtc.HasValue)
                {
                    errorMessage = $"操作账本条目 {entry.Id} 缺少完成时间。";
                    return false;
                }
                if (entry.Kind is FileOperationJournalKind.MoveIntoFolder or
                    FileOperationJournalKind.UndoMove)
                {
                    if (string.IsNullOrWhiteSpace(entry.SourcePath) ||
                        string.IsNullOrWhiteSpace(entry.DestinationPath) ||
                        string.IsNullOrWhiteSpace(entry.ExpectedIdentity))
                    {
                        errorMessage = $"移动账本条目 {entry.Id} 缺少路径或稳定身份。";
                        return false;
                    }
                }
                else if (entry.Kind == FileOperationJournalKind.MoveToRecycleBin &&
                         (string.IsNullOrWhiteSpace(entry.SourcePath) ||
                          string.IsNullOrWhiteSpace(entry.ExpectedIdentity)))
                {
                    errorMessage = $"回收站账本条目 {entry.Id} 缺少源路径或稳定身份。";
                    return false;
                }
                if (entry.Kind == FileOperationJournalKind.UndoMove &&
                    string.IsNullOrWhiteSpace(entry.UndoOfEntryId))
                {
                    errorMessage = $"撤销账本条目 {entry.Id} 缺少原操作引用。";
                    return false;
                }
                if (entry.LayoutSnapshot is { ItemTags: null })
                {
                    errorMessage = $"操作账本条目 {entry.Id} 的布局快照缺少标签集合。";
                    return false;
                }
                if (entry.LayoutSnapshot is { WorkspacePlacements: null })
                {
                    errorMessage = $"操作账本条目 {entry.Id} 的布局快照缺少工作区位置集合。";
                    return false;
                }
                if (entry.LayoutSnapshot?.WorkspacePlacements is { } placements)
                {
                    var workspaceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (FileOperationWorkspacePlacementSnapshot? placement in placements)
                    {
                        if (placement == null ||
                            string.IsNullOrWhiteSpace(placement.WorkspaceId) ||
                            !workspaceIds.Add(placement.WorkspaceId))
                        {
                            errorMessage = $"操作账本条目 {entry.Id} 包含无效或重复的工作区位置。";
                            return false;
                        }
                    }
                }
                if (entry.LayoutSnapshot?.InboxItem is { Identity: null })
                {
                    errorMessage = $"操作账本条目 {entry.Id} 的收件箱快照缺少稳定身份。";
                    return false;
                }
                if (entry.UndoOfEntryId != null &&
                    entry.UndoOfEntryId.Equals(entry.Id, StringComparison.OrdinalIgnoreCase))
                {
                    errorMessage = $"操作账本条目 {entry.Id} 不得撤销自己。";
                    return false;
                }
            }

            errorMessage = null;
            return true;
        }

        private static JsonSerializerOptions CreateSerializerOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };
            options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            return options;
        }

        private void EnterProtection(string error)
        {
            _writeProtected = true;
            _protectionError = $"操作账本无法安全读取，已进入保护状态：{error}";
        }

        private static void TryDeleteTemporaryFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 主文件没有被覆盖。失败的唯一性临时文件可供人工诊断。
            }
        }
    }
}
