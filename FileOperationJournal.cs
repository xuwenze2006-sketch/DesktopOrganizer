namespace DesktopOrganizer
{
    internal enum FileOperationJournalKind
    {
        MoveIntoFolder,
        MoveToRecycleBin,
        UndoMove,
        EmptyRecycleBin
    }

    internal enum FileOperationJournalState
    {
        Queued,
        Running,
        Succeeded,
        Failed,
        Canceled,
        Interrupted,
        Uncertain
    }

    /// <summary>用于跨会话恢复图标布局的坐标 DTO。</summary>
    internal sealed class FileOperationPositionSnapshot
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    /// <summary>撤销文件移动时可重建的最小分组 DTO。</summary>
    internal sealed class FileOperationGroupSnapshot
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool IsCollapsed { get; set; }
        public bool IsAutoCategory { get; set; }
        public string? AutoCategoryKey { get; set; }
        public string? UserRuleId { get; set; }
        public bool IsSizeLocked { get; set; }
        public bool UseUniformTrackWidth { get; set; }
        public string SortMode { get; set; } = "Custom";
        public List<string> ManuallyAssignedItemNames { get; set; } = new();
    }

    /// <summary>一个项目在某个命名工作区中的视觉位置快照。</summary>
    internal sealed class FileOperationWorkspacePlacementSnapshot
    {
        public string WorkspaceId { get; set; } = string.Empty;
        public string? SourceGroupId { get; set; }
        public FileOperationGroupSnapshot? SourceGroup { get; set; }
        public int SourceGroupItemIndex { get; set; } = -1;
        public FileOperationPositionSnapshot? FreePosition { get; set; }
        public FileOperationPositionSnapshot? AutoClassificationOriginalPosition { get; set; }
    }

    /// <summary>
    /// 真实移动前的视觉布局快照。它只用于安全撤销后的布局回填，
    /// 不参与恢复核验中的文件系统判定。
    /// </summary>
    internal sealed class FileOperationLayoutSnapshot
    {
        public string? SourceWorkspaceId { get; set; }
        public string? SourceGroupId { get; set; }
        public FileOperationGroupSnapshot? SourceGroup { get; set; }
        public int SourceGroupItemIndex { get; set; } = -1;
        public FileOperationPositionSnapshot? FreePosition { get; set; }
        public FileOperationPositionSnapshot? AutoClassificationOriginalPosition { get; set; }
        public List<string> ItemTags { get; set; } = new();
        public long? FirstSeenUtcTicks { get; set; }
        public long? LastMovedUtcTicks { get; set; }
        public InboxItemInfo? InboxItem { get; set; }
        public List<FileOperationWorkspacePlacementSnapshot> WorkspacePlacements { get; set; } = new();
    }

    /// <summary>一次文件操作中的单个项目；批量操作通过 BatchId 联系，不合并项目结果。</summary>
    internal sealed class FileOperationJournalEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string BatchId { get; set; } = Guid.NewGuid().ToString("N");
        public FileOperationJournalKind Kind { get; set; }
        public FileOperationJournalState State { get; set; } = FileOperationJournalState.Queued;
        public string DisplayName { get; set; } = string.Empty;
        public string? SourcePath { get; set; }
        public string? DestinationPath { get; set; }
        public string? ExpectedIdentity { get; set; }
        public string? ResultIdentity { get; set; }
        public DateTime RequestedUtc { get; set; } = DateTime.UtcNow;
        public DateTime? StartedUtc { get; set; }
        public DateTime? CompletedUtc { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string? UndoOfEntryId { get; set; }
        public FileOperationLayoutSnapshot? LayoutSnapshot { get; set; }
        public bool IsAggregate { get; set; }
    }

    /// <summary>与 layout.json 独立演进的文件操作账本根对象。</summary>
    internal sealed class FileOperationJournalData
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;
        public long SaveGeneration { get; set; }
        public List<FileOperationJournalEntry> Entries { get; set; } = new();
    }

    internal static class FileOperationJournalStateMachine
    {
        public static bool IsTerminal(FileOperationJournalState state) => state is
            FileOperationJournalState.Succeeded or
            FileOperationJournalState.Failed or
            FileOperationJournalState.Canceled or
            FileOperationJournalState.Interrupted or
            FileOperationJournalState.Uncertain;

        public static bool CanTransition(
            FileOperationJournalState current,
            FileOperationJournalState next) => current switch
            {
                FileOperationJournalState.Queued => next is
                    FileOperationJournalState.Running or
                    FileOperationJournalState.Canceled or
                    FileOperationJournalState.Interrupted,
                FileOperationJournalState.Running => next is
                    FileOperationJournalState.Succeeded or
                    FileOperationJournalState.Failed or
                    FileOperationJournalState.Canceled or
                    FileOperationJournalState.Interrupted or
                    FileOperationJournalState.Uncertain,
                _ => false
            };

        public static bool TryTransition(
            FileOperationJournalEntry entry,
            FileOperationJournalState next,
            DateTime utcNow)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (!CanTransition(entry.State, next))
            {
                return false;
            }

            entry.State = next;
            if (next == FileOperationJournalState.Running)
            {
                entry.StartedUtc ??= utcNow;
            }
            if (IsTerminal(next))
            {
                entry.CompletedUtc = utcNow;
            }
            return true;
        }
    }

    internal interface IFileOperationJournalStore
    {
        FileOperationJournalLoadResult Load();

        bool TrySave(FileOperationJournalData journal, out string? errorMessage);
    }

    internal sealed record FileOperationExecutionOutcome(
        FileOperationJournalState State,
        string? DestinationPath = null,
        string? ResultIdentity = null,
        string? ErrorCode = null,
        string? ErrorMessage = null)
    {
        public static FileOperationExecutionOutcome Success(
            string? destinationPath = null,
            string? resultIdentity = null) => new(
                FileOperationJournalState.Succeeded,
                destinationPath,
                resultIdentity);

        public static FileOperationExecutionOutcome Failure(
            string errorCode,
            string errorMessage) => new(
                FileOperationJournalState.Failed,
                ErrorCode: errorCode,
                ErrorMessage: errorMessage);
    }

    internal sealed record FileOperationDispatchResult(
        bool Executed,
        bool JournalPersisted,
        FileOperationJournalEntry Entry,
        string? ErrorMessage);

    /// <summary>
    /// 为真实文件委托提供最小写前保护。Queued 与 Running 均必须先落盘，
    /// 任一落盘失败都不会调用操作委托；委托自身始终最多调用一次。
    /// </summary>
    internal sealed class FileOperationWriteAheadCoordinator
    {
        private readonly IFileOperationJournalStore _store;
        private readonly Func<DateTime> _utcNow;
        private readonly Action<FileOperationJournalData>? _afterPersisted;
        private readonly object _journalGate;

        public FileOperationWriteAheadCoordinator(
            IFileOperationJournalStore store,
            Func<DateTime>? utcNow = null,
            Action<FileOperationJournalData>? afterPersisted = null,
            object? journalGate = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _afterPersisted = afterPersisted;
            _journalGate = journalGate ?? new object();
        }

        public FileOperationDispatchResult Dispatch(
            FileOperationJournalData journal,
            FileOperationJournalEntry entry,
            Func<FileOperationExecutionOutcome> operation)
        {
            ArgumentNullException.ThrowIfNull(journal);
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(operation);
            if (!TryQueue(journal, [entry], out string? queuedError))
            {
                return new FileOperationDispatchResult(
                    Executed: false,
                    JournalPersisted: false,
                    Entry: entry,
                    ErrorMessage: queuedError ?? "Queued 状态未能落盘。");
            }

            return DispatchQueued(journal, entry, operation);
        }

        /// <summary>
        /// 在任务进入后台队列前持久化全部 Queued 条目。批量条目要么一起进入账本，
        /// 要么全部从内存回滚；返回成功前不得把真实操作交给后台队列。
        /// </summary>
        public bool TryQueue(
            FileOperationJournalData journal,
            IReadOnlyList<FileOperationJournalEntry> entries,
            out string? errorMessage)
        {
            ArgumentNullException.ThrowIfNull(journal);
            ArgumentNullException.ThrowIfNull(entries);
            lock (_journalGate)
            {
                return TryQueueCore(journal, entries, out errorMessage);
            }
        }

        private bool TryQueueCore(
            FileOperationJournalData journal,
            IReadOnlyList<FileOperationJournalEntry> entries,
            out string? errorMessage)
        {
            journal.Entries ??= new List<FileOperationJournalEntry>();
            if (entries.Count == 0)
            {
                errorMessage = "没有需要排队的操作账本条目。";
                return false;
            }

            var newIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FileOperationJournalEntry? entry in entries)
            {
                if (entry == null ||
                    entry.State != FileOperationJournalState.Queued ||
                    string.IsNullOrWhiteSpace(entry.Id) ||
                    !newIds.Add(entry.Id) ||
                    journal.Entries.Any(existing =>
                        existing.Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    errorMessage = "只能排队 ID 唯一且处于 Queued 状态的新操作。";
                    return false;
                }
            }

            journal.Entries.AddRange(entries);
            if (TrySave(journal, out errorMessage))
            {
                return true;
            }

            foreach (FileOperationJournalEntry entry in entries)
            {
                journal.Entries.Remove(entry);
            }
            errorMessage ??= "Queued 状态未能落盘。";
            return false;
        }

        /// <summary>只执行已经在后台入队前持久化为 Queued 的条目。</summary>
        public FileOperationDispatchResult DispatchQueued(
            FileOperationJournalData journal,
            FileOperationJournalEntry entry,
            Func<FileOperationExecutionOutcome> operation,
            Func<bool>? canStart = null)
        {
            ArgumentNullException.ThrowIfNull(journal);
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(operation);
            lock (_journalGate)
            {
                journal.Entries ??= new List<FileOperationJournalEntry>();
                if (entry.State != FileOperationJournalState.Queued ||
                    !journal.Entries.Any(existing => ReferenceEquals(existing, entry)))
                {
                    return new FileOperationDispatchResult(
                        Executed: false,
                        JournalPersisted: false,
                        Entry: entry,
                        ErrorMessage: "只能执行已经持久化的 Queued 操作。");
                }
                if (canStart != null && !canStart())
                {
                    return new FileOperationDispatchResult(
                        Executed: false,
                        JournalPersisted: true,
                        Entry: entry,
                        ErrorMessage: "操作账本处于保护状态，Queued 操作没有开始。");
                }

                DateTime? previousStartedUtc = entry.StartedUtc;
                if (!FileOperationJournalStateMachine.TryTransition(
                        entry,
                        FileOperationJournalState.Running,
                        _utcNow()))
                {
                    return new FileOperationDispatchResult(
                        Executed: false,
                        JournalPersisted: true,
                        Entry: entry,
                        ErrorMessage: "无法将已排队操作转换为 Running。");
                }

                if (!TrySave(journal, out string? runningError))
                {
                    // Running 没有落盘时，不得让内存假装该委托已开始。
                    entry.State = FileOperationJournalState.Queued;
                    entry.StartedUtc = previousStartedUtc;
                    return new FileOperationDispatchResult(
                        Executed: false,
                        JournalPersisted: false,
                        Entry: entry,
                        ErrorMessage: runningError ?? "Running 状态未能落盘。");
                }
            }

            FileOperationExecutionOutcome outcome;
            try
            {
                outcome = operation() ?? FileOperationExecutionOutcome.Failure(
                    "null_outcome",
                    "文件操作未返回结果。");
            }
            catch (OperationCanceledException exception)
            {
                outcome = new FileOperationExecutionOutcome(
                    FileOperationJournalState.Canceled,
                    ErrorCode: "canceled",
                    ErrorMessage: exception.Message);
            }
            catch (Exception exception)
            {
                outcome = FileOperationExecutionOutcome.Failure(
                    exception.GetType().Name,
                    exception.Message);
            }

            lock (_journalGate)
            {
                if (!FileOperationJournalStateMachine.CanTransition(entry.State, outcome.State))
                {
                    outcome = FileOperationExecutionOutcome.Failure(
                        "invalid_outcome_state",
                        $"文件操作返回了非法终态 {outcome.State}。");
                }

                entry.DestinationPath = outcome.DestinationPath ?? entry.DestinationPath;
                entry.ResultIdentity = outcome.ResultIdentity ?? entry.ResultIdentity;
                entry.ErrorCode = outcome.ErrorCode;
                entry.ErrorMessage = outcome.ErrorMessage;
                _ = FileOperationJournalStateMachine.TryTransition(entry, outcome.State, _utcNow());

                bool terminalSaved = TrySave(journal, out string? terminalError);
                return new FileOperationDispatchResult(
                    Executed: true,
                    JournalPersisted: terminalSaved,
                    Entry: entry,
                    ErrorMessage: terminalSaved
                        ? entry.ErrorMessage
                        : terminalError ?? "操作已执行，但终态未能落盘。");
            }
        }

        private bool TrySave(
            FileOperationJournalData journal,
            out string? errorMessage)
        {
            if (!_store.TrySave(journal, out errorMessage))
            {
                return false;
            }

            try
            {
                _afterPersisted?.Invoke(journal);
            }
            catch
            {
                // 展示快照失败不得改变已经落盘的写前状态或触发文件操作重试。
            }
            return true;
        }
    }

    internal delegate bool FileOperationJournalIdentityReader(string path, out string identity);

    internal sealed record FileOperationRecoveryDecision(
        FileOperationJournalState SuggestedState,
        string EvidenceCode);

    internal sealed record FileOperationJournalRecoveryChange(
        string EntryId,
        FileOperationJournalKind Kind,
        FileOperationJournalState PreviousState,
        FileOperationJournalState RecoveredState,
        string EvidenceCode);

    internal static class FileOperationJournalRecovery
    {
        /// <summary>
        /// 只根据核验器提供的只读证据收束遗留状态。此方法不会保存账本，
        /// 也不会调用或重试任何真实文件操作。
        /// </summary>
        public static IReadOnlyList<FileOperationJournalRecoveryChange> Apply(
            FileOperationJournalData journal,
            FileOperationRecoveryEvaluator evaluator,
            DateTime utcNow)
        {
            ArgumentNullException.ThrowIfNull(journal);
            ArgumentNullException.ThrowIfNull(evaluator);
            journal.Entries ??= new List<FileOperationJournalEntry>();
            var changes = new List<FileOperationJournalRecoveryChange>();
            foreach (FileOperationJournalEntry entry in journal.Entries)
            {
                if (FileOperationJournalStateMachine.IsTerminal(entry.State))
                {
                    continue;
                }

                FileOperationJournalState previous = entry.State;
                FileOperationRecoveryDecision decision = evaluator.Evaluate(entry);
                if (!FileOperationJournalStateMachine.TryTransition(
                        entry,
                        decision.SuggestedState,
                        utcNow))
                {
                    continue;
                }

                entry.ErrorCode = $"recovery:{decision.EvidenceCode}";
                entry.ErrorMessage = decision.SuggestedState switch
                {
                    FileOperationJournalState.Succeeded =>
                        "启动时根据源路径、目标路径和稳定身份核验为已完成；未重新执行操作。",
                    FileOperationJournalState.Interrupted =>
                        "上次操作未开始或未完成；不会自动重试。",
                    _ => "无法仅凭当前磁盘状态确认上次操作结果；需要人工核对，且不会自动重试。"
                };
                if (decision.SuggestedState == FileOperationJournalState.Succeeded)
                {
                    entry.ResultIdentity ??= entry.ExpectedIdentity;
                }
                changes.Add(new FileOperationJournalRecoveryChange(
                    entry.Id,
                    entry.Kind,
                    previous,
                    decision.SuggestedState,
                    decision.EvidenceCode));
            }
            return changes;
        }
    }

    /// <summary>
    /// 对上次会话遗留的 Queued/Running 条目做只读证据判定。
    /// 该类不修改条目、不保存账本、不调用任何真实文件操作。
    /// </summary>
    internal sealed class FileOperationRecoveryEvaluator
    {
        private readonly Func<string, bool> _pathExists;
        private readonly FileOperationJournalIdentityReader _tryReadIdentity;

        public FileOperationRecoveryEvaluator()
            : this(
                path => File.Exists(path) || Directory.Exists(path),
                FileOperationIdentityGuard.TryCapture)
        {
        }

        internal FileOperationRecoveryEvaluator(
            Func<string, bool> pathExists,
            FileOperationJournalIdentityReader tryReadIdentity)
        {
            _pathExists = pathExists ?? throw new ArgumentNullException(nameof(pathExists));
            _tryReadIdentity = tryReadIdentity ??
                throw new ArgumentNullException(nameof(tryReadIdentity));
        }

        public FileOperationRecoveryDecision Evaluate(FileOperationJournalEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (entry.State == FileOperationJournalState.Queued)
            {
                // 写前协调器保证 Running 必须先落盘才会调用委托，
                // 因此遗留 Queued 不得被自动执行或推断为本程序已完成。
                return new FileOperationRecoveryDecision(
                    FileOperationJournalState.Interrupted,
                    "queued_never_started");
            }

            if (entry.State != FileOperationJournalState.Running)
            {
                return new FileOperationRecoveryDecision(entry.State, "terminal_no_recovery");
            }

            return entry.Kind switch
            {
                FileOperationJournalKind.MoveIntoFolder or
                FileOperationJournalKind.UndoMove => EvaluateMove(entry),
                FileOperationJournalKind.MoveToRecycleBin => EvaluateRecycle(entry),
                FileOperationJournalKind.EmptyRecycleBin => new FileOperationRecoveryDecision(
                    FileOperationJournalState.Uncertain,
                    "aggregate_result_unknown"),
                _ => new FileOperationRecoveryDecision(
                    FileOperationJournalState.Uncertain,
                    "unsupported_kind")
            };
        }

        private FileOperationRecoveryDecision EvaluateMove(FileOperationJournalEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.SourcePath) ||
                string.IsNullOrWhiteSpace(entry.DestinationPath) ||
                string.IsNullOrWhiteSpace(entry.ExpectedIdentity))
            {
                return new FileOperationRecoveryDecision(
                    FileOperationJournalState.Uncertain,
                    "move_evidence_incomplete");
            }

            bool sourceExists = _pathExists(entry.SourcePath);
            bool destinationExists = _pathExists(entry.DestinationPath);
            bool sourceMatches = sourceExists && IdentityMatches(
                entry.SourcePath,
                entry.ExpectedIdentity);
            string destinationIdentity = string.IsNullOrWhiteSpace(entry.ResultIdentity)
                ? entry.ExpectedIdentity
                : entry.ResultIdentity;
            bool destinationMatches = destinationExists && IdentityMatches(
                entry.DestinationPath,
                destinationIdentity);

            if (sourceMatches && !destinationExists)
            {
                return new FileOperationRecoveryDecision(
                    FileOperationJournalState.Interrupted,
                    "move_only_source");
            }

            if (destinationMatches && !sourceExists)
            {
                return new FileOperationRecoveryDecision(
                    FileOperationJournalState.Succeeded,
                    "move_only_destination");
            }

            return new FileOperationRecoveryDecision(
                FileOperationJournalState.Uncertain,
                sourceExists && destinationExists
                    ? "move_both_paths_present"
                    : !sourceExists && !destinationExists
                        ? "move_both_paths_missing"
                        : "move_identity_mismatch");
        }

        private FileOperationRecoveryDecision EvaluateRecycle(FileOperationJournalEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.SourcePath) ||
                string.IsNullOrWhiteSpace(entry.ExpectedIdentity))
            {
                return new FileOperationRecoveryDecision(
                    FileOperationJournalState.Uncertain,
                    "recycle_evidence_incomplete");
            }

            if (!_pathExists(entry.SourcePath))
            {
                // 无法证明项目是进入回收站，还是被外部程序移动或删除。
                return new FileOperationRecoveryDecision(
                    FileOperationJournalState.Uncertain,
                    "recycle_source_missing");
            }

            if (IdentityMatches(entry.SourcePath, entry.ExpectedIdentity))
            {
                return new FileOperationRecoveryDecision(
                    FileOperationJournalState.Interrupted,
                    "recycle_source_unchanged");
            }

            return new FileOperationRecoveryDecision(
                FileOperationJournalState.Uncertain,
                "recycle_source_replaced");
        }

        private bool IdentityMatches(string path, string expectedIdentity)
        {
            return _tryReadIdentity(path, out string identity) &&
                   string.Equals(identity, expectedIdentity, StringComparison.Ordinal);
        }
    }
}
