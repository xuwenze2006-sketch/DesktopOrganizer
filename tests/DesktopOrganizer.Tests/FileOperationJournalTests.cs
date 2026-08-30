using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class FileOperationJournalTests
{
    private static readonly DateTime FirstUtc = new(2026, 8, 28, 1, 2, 3, DateTimeKind.Utc);
    private static readonly DateTime SecondUtc = FirstUtc.AddSeconds(1);

    [TestMethod]
    public void StateMachine_AllowsOnlyDeclaredTransitionsAndTerminalCannotRestart()
    {
        FileOperationJournalState[] queuedTargets =
        [
            FileOperationJournalState.Running,
            FileOperationJournalState.Canceled,
            FileOperationJournalState.Interrupted
        ];
        foreach (FileOperationJournalState target in queuedTargets)
        {
            var entry = NewEntry();
            Assert.IsTrue(FileOperationJournalStateMachine.TryTransition(entry, target, FirstUtc));
            Assert.AreEqual(target, entry.State);
            if (FileOperationJournalStateMachine.IsTerminal(target))
            {
                Assert.AreEqual(FirstUtc, entry.CompletedUtc);
                Assert.IsFalse(FileOperationJournalStateMachine.TryTransition(
                    entry,
                    FileOperationJournalState.Running,
                    SecondUtc));
            }
        }

        FileOperationJournalState[] runningTargets =
        [
            FileOperationJournalState.Succeeded,
            FileOperationJournalState.Failed,
            FileOperationJournalState.Canceled,
            FileOperationJournalState.Interrupted,
            FileOperationJournalState.Uncertain
        ];
        foreach (FileOperationJournalState target in runningTargets)
        {
            var entry = NewEntry();
            Assert.IsTrue(FileOperationJournalStateMachine.TryTransition(
                entry,
                FileOperationJournalState.Running,
                FirstUtc));
            Assert.IsTrue(FileOperationJournalStateMachine.TryTransition(entry, target, SecondUtc));
            Assert.AreEqual(FirstUtc, entry.StartedUtc);
            Assert.AreEqual(SecondUtc, entry.CompletedUtc);
            Assert.IsFalse(FileOperationJournalStateMachine.TryTransition(
                entry,
                FileOperationJournalState.Running,
                SecondUtc.AddSeconds(1)));
        }

        var illegal = NewEntry();
        Assert.IsFalse(FileOperationJournalStateMachine.TryTransition(
            illegal,
            FileOperationJournalState.Succeeded,
            FirstUtc));
        Assert.AreEqual(FileOperationJournalState.Queued, illegal.State);
    }

    [TestMethod]
    public void Store_AtomicRoundTripPreservesItemizedFieldsAndLayoutSnapshot()
    {
        string root = CreateTestDirectory();
        try
        {
            string path = Path.Combine(root, "operation-journal.json");
            var journal = new FileOperationJournalData();
            FileOperationJournalEntry entry = NewEntry();
            entry.Kind = FileOperationJournalKind.MoveIntoFolder;
            entry.SourcePath = @"C:\Desktop\item.txt";
            entry.DestinationPath = @"C:\Desktop\Folder\item.txt";
            entry.ExpectedIdentity = "volume:item";
            entry.ResultIdentity = "volume:item";
            entry.UndoOfEntryId = "earlier-entry";
            entry.LayoutSnapshot = new FileOperationLayoutSnapshot
            {
                SourceWorkspaceId = "workspace-1",
                SourceGroupId = "group-1",
                SourceGroup = new FileOperationGroupSnapshot
                {
                    Id = "group-1",
                    Name = "规则分组",
                    UserRuleId = "rule-1",
                    ManuallyAssignedItemNames = ["item.txt"]
                },
                SourceGroupItemIndex = 4,
                FreePosition = new FileOperationPositionSnapshot { X = 12.5, Y = 30.25 },
                ItemTags = ["重要", "资料"],
                FirstSeenUtcTicks = DateTime.UnixEpoch.Ticks,
                WorkspacePlacements =
                [
                    new FileOperationWorkspacePlacementSnapshot
                    {
                        WorkspaceId = "workspace-1",
                        SourceGroupId = "group-1",
                        SourceGroupItemIndex = 4
                    },
                    new FileOperationWorkspacePlacementSnapshot
                    {
                        WorkspaceId = "workspace-2",
                        FreePosition = new FileOperationPositionSnapshot { X = 55, Y = 66 }
                    }
                ],
                InboxItem = new InboxItemInfo
                {
                    Identity = new DesktopItemIdentityInfo
                    {
                        LastKnownPath = @"C:\Desktop\item.txt",
                        FileId = "volume:item"
                    },
                    SuggestedCategoryKey = "documents",
                    Reliability = ClassificationReliability.Reliable
                }
            };
            journal.Entries.Add(entry);
            var store = new FileOperationJournalStore(path);

            Assert.IsTrue(store.TrySave(journal, out string? saveError), saveError);
            Assert.AreEqual(1L, journal.SaveGeneration);
            FileOperationJournalLoadResult loaded = new FileOperationJournalStore(path).Load();

            Assert.AreEqual(FileOperationJournalLoadState.Ready, loaded.State);
            Assert.IsNotNull(loaded.Journal);
            Assert.AreEqual(1L, loaded.Journal.SaveGeneration);
            Assert.AreEqual(1, loaded.Journal.Entries.Count);
            FileOperationJournalEntry restored = loaded.Journal.Entries[0];
            Assert.AreEqual(entry.Id, restored.Id);
            Assert.AreEqual(entry.BatchId, restored.BatchId);
            Assert.AreEqual(FileOperationJournalKind.MoveIntoFolder, restored.Kind);
            Assert.AreEqual("volume:item", restored.ExpectedIdentity);
            Assert.AreEqual("earlier-entry", restored.UndoOfEntryId);
            Assert.AreEqual("workspace-1", restored.LayoutSnapshot?.SourceWorkspaceId);
            Assert.AreEqual("rule-1", restored.LayoutSnapshot?.SourceGroup?.UserRuleId);
            CollectionAssert.AreEqual(
                new[] { "item.txt" },
                restored.LayoutSnapshot?.SourceGroup?.ManuallyAssignedItemNames);
            Assert.AreEqual(12.5, restored.LayoutSnapshot?.FreePosition?.X);
            CollectionAssert.AreEqual(
                new[] { "重要", "资料" },
                restored.LayoutSnapshot?.ItemTags);
            Assert.AreEqual(
                "documents",
                restored.LayoutSnapshot?.InboxItem?.SuggestedCategoryKey);
            Assert.AreEqual(2, restored.LayoutSnapshot?.WorkspacePlacements.Count);
            Assert.AreEqual(
                55,
                restored.LayoutSnapshot?.WorkspacePlacements[1].FreePosition?.X);
            Assert.AreEqual(0, Directory.EnumerateFiles(root, "*.tmp").Count());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Store_CorruptMainReturnsProtectedAndNeverOverwritesIt()
    {
        string root = CreateTestDirectory();
        try
        {
            string path = Path.Combine(root, "operation-journal.json");
            const string corruptContent = "{not-json";
            File.WriteAllText(path, corruptContent);
            var store = new FileOperationJournalStore(path);

            FileOperationJournalLoadResult loaded = store.Load();
            bool saved = store.TrySave(new FileOperationJournalData(), out string? saveError);

            Assert.AreEqual(FileOperationJournalLoadState.Protected, loaded.State);
            Assert.IsTrue(loaded.IsProtected);
            Assert.IsNull(loaded.Journal);
            Assert.IsFalse(saved);
            Assert.IsFalse(string.IsNullOrWhiteSpace(saveError));
            Assert.AreEqual(corruptContent, File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Store_TrySaveWithoutLoadAlsoProtectsCorruptMain()
    {
        string root = CreateTestDirectory();
        try
        {
            string path = Path.Combine(root, "operation-journal.json");
            const string corruptContent = "[]";
            File.WriteAllText(path, corruptContent);
            var store = new FileOperationJournalStore(path);

            Assert.IsFalse(store.TrySave(new FileOperationJournalData(), out _));
            Assert.AreEqual(corruptContent, File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Coordinator_QueuedPersistenceFailureNeverInvokesOperation()
    {
        var store = new CapturingStore(failSaveNumber: 1);
        var coordinator = new FileOperationWriteAheadCoordinator(store, () => FirstUtc);
        var journal = new FileOperationJournalData();
        FileOperationJournalEntry entry = NewEntry();
        int operationCalls = 0;

        FileOperationDispatchResult result = coordinator.Dispatch(
            journal,
            entry,
            () =>
            {
                operationCalls++;
                return FileOperationExecutionOutcome.Success();
            });

        Assert.IsFalse(result.Executed);
        Assert.IsFalse(result.JournalPersisted);
        Assert.AreEqual(0, operationCalls);
        Assert.AreEqual(0, journal.Entries.Count);
        Assert.AreEqual(FileOperationJournalState.Queued, entry.State);
    }

    [TestMethod]
    public void Coordinator_RunningPersistenceFailureNeverInvokesOperation()
    {
        var store = new CapturingStore(failSaveNumber: 2);
        var coordinator = new FileOperationWriteAheadCoordinator(store, () => FirstUtc);
        var journal = new FileOperationJournalData();
        FileOperationJournalEntry entry = NewEntry();
        int operationCalls = 0;

        FileOperationDispatchResult result = coordinator.Dispatch(
            journal,
            entry,
            () =>
            {
                operationCalls++;
                return FileOperationExecutionOutcome.Success();
            });

        Assert.IsFalse(result.Executed);
        Assert.IsFalse(result.JournalPersisted);
        Assert.AreEqual(0, operationCalls);
        Assert.AreEqual(FileOperationJournalState.Queued, entry.State);
        CollectionAssert.AreEqual(
            new[] { FileOperationJournalState.Queued, FileOperationJournalState.Running },
            store.SavedStates.ToArray());
    }

    [TestMethod]
    public void Coordinator_PrequeuePersistsBeforeDispatchAndOperationRunsOnce()
    {
        var store = new CapturingStore();
        var coordinator = new FileOperationWriteAheadCoordinator(store, () => FirstUtc);
        var journal = new FileOperationJournalData();
        FileOperationJournalEntry entry = NewEntry();
        int operationCalls = 0;

        bool queued = coordinator.TryQueue(journal, [entry], out string? queueError);

        Assert.IsTrue(queued, queueError);
        Assert.AreEqual(0, operationCalls);
        Assert.AreEqual(FileOperationJournalState.Queued, entry.State);
        CollectionAssert.AreEqual(
            new[] { FileOperationJournalState.Queued },
            store.SavedStates.ToArray());

        FileOperationDispatchResult result = coordinator.DispatchQueued(
            journal,
            entry,
            () =>
            {
                operationCalls++;
                return FileOperationExecutionOutcome.Success();
            });

        Assert.IsTrue(result.Executed);
        Assert.IsTrue(result.JournalPersisted);
        Assert.AreEqual(1, operationCalls);
        CollectionAssert.AreEqual(
            new[]
            {
                FileOperationJournalState.Queued,
                FileOperationJournalState.Running,
                FileOperationJournalState.Succeeded
            },
            store.SavedStates.ToArray());
    }

    [TestMethod]
    public void Coordinator_BatchPrequeueFailureRollsBackEveryEntry()
    {
        var store = new CapturingStore(failSaveNumber: 1);
        var coordinator = new FileOperationWriteAheadCoordinator(store, () => FirstUtc);
        var journal = new FileOperationJournalData();
        FileOperationJournalEntry first = NewEntry();
        FileOperationJournalEntry second = NewEntry();
        second.Id = Guid.NewGuid().ToString("N");

        bool queued = coordinator.TryQueue(
            journal,
            [first, second],
            out string? queueError);

        Assert.IsFalse(queued);
        Assert.IsFalse(string.IsNullOrWhiteSpace(queueError));
        Assert.AreEqual(0, journal.Entries.Count);
        Assert.AreEqual(FileOperationJournalState.Queued, first.State);
        Assert.AreEqual(FileOperationJournalState.Queued, second.State);
    }

    [TestMethod]
    public void Coordinator_ProtectedPrequeuedEntryDoesNotStartOrPersistRunning()
    {
        var store = new CapturingStore();
        var gate = new object();
        var coordinator = new FileOperationWriteAheadCoordinator(
            store,
            () => FirstUtc,
            journalGate: gate);
        var journal = new FileOperationJournalData();
        FileOperationJournalEntry entry = NewEntry();
        Assert.IsTrue(coordinator.TryQueue(journal, [entry], out _));
        int operationCalls = 0;

        FileOperationDispatchResult result = coordinator.DispatchQueued(
            journal,
            entry,
            () =>
            {
                operationCalls++;
                return FileOperationExecutionOutcome.Success();
            },
            canStart: () => false);

        Assert.IsFalse(result.Executed);
        Assert.IsTrue(result.JournalPersisted);
        Assert.AreEqual(0, operationCalls);
        Assert.AreEqual(FileOperationJournalState.Queued, entry.State);
        CollectionAssert.AreEqual(
            new[] { FileOperationJournalState.Queued },
            store.SavedStates.ToArray());
    }

    [TestMethod]
    public void Coordinator_SuccessPersistsQueuedRunningAndTerminalAndInvokesOnce()
    {
        int clockCalls = 0;
        var store = new CapturingStore();
        var coordinator = new FileOperationWriteAheadCoordinator(
            store,
            () => FirstUtc.AddSeconds(clockCalls++));
        var journal = new FileOperationJournalData();
        FileOperationJournalEntry entry = NewEntry();
        int operationCalls = 0;

        FileOperationDispatchResult result = coordinator.Dispatch(
            journal,
            entry,
            () =>
            {
                operationCalls++;
                return FileOperationExecutionOutcome.Success(
                    @"C:\Desktop\Folder\item.txt",
                    "volume:item");
            });

        Assert.IsTrue(result.Executed);
        Assert.IsTrue(result.JournalPersisted);
        Assert.AreEqual(1, operationCalls);
        Assert.AreEqual(FileOperationJournalState.Succeeded, entry.State);
        Assert.AreEqual(@"C:\Desktop\Folder\item.txt", entry.DestinationPath);
        Assert.AreEqual("volume:item", entry.ResultIdentity);
        CollectionAssert.AreEqual(
            new[]
            {
                FileOperationJournalState.Queued,
                FileOperationJournalState.Running,
                FileOperationJournalState.Succeeded
            },
            store.SavedStates.ToArray());
    }

    [TestMethod]
    public void Coordinator_CancellationIsPersistedAndNeverRetried()
    {
        var store = new CapturingStore();
        var coordinator = new FileOperationWriteAheadCoordinator(store, () => FirstUtc);
        var journal = new FileOperationJournalData();
        FileOperationJournalEntry entry = NewEntry();
        int operationCalls = 0;

        FileOperationDispatchResult result = coordinator.Dispatch(
            journal,
            entry,
            () =>
            {
                operationCalls++;
                throw new OperationCanceledException("expected cancellation");
            });

        Assert.IsTrue(result.Executed);
        Assert.IsTrue(result.JournalPersisted);
        Assert.AreEqual(1, operationCalls);
        Assert.AreEqual(FileOperationJournalState.Canceled, entry.State);
        CollectionAssert.AreEqual(
            new[]
            {
                FileOperationJournalState.Queued,
                FileOperationJournalState.Running,
                FileOperationJournalState.Canceled
            },
            store.SavedStates.ToArray());
    }

    [TestMethod]
    public void Coordinator_TerminalSaveFailureInvokesOperationOnlyOnce()
    {
        var store = new CapturingStore(failSaveNumber: 3);
        var coordinator = new FileOperationWriteAheadCoordinator(store, () => FirstUtc);
        var journal = new FileOperationJournalData();
        FileOperationJournalEntry entry = NewEntry();
        int operationCalls = 0;

        FileOperationDispatchResult result = coordinator.Dispatch(
            journal,
            entry,
            () =>
            {
                operationCalls++;
                return FileOperationExecutionOutcome.Success(
                    @"C:\Desktop\Folder\item.txt",
                    "volume:item");
            });

        Assert.IsTrue(result.Executed);
        Assert.IsFalse(result.JournalPersisted);
        Assert.AreEqual(1, operationCalls);
        Assert.AreEqual(FileOperationJournalState.Succeeded, entry.State);
        CollectionAssert.AreEqual(
            new[]
            {
                FileOperationJournalState.Queued,
                FileOperationJournalState.Running,
                FileOperationJournalState.Succeeded
            },
            store.SavedStates.ToArray());
    }

    [TestMethod]
    public void RecoveryApply_ClosesInterruptedAndSucceededEntriesWithoutRetry()
    {
        FileOperationJournalEntry queued = NewEntry();
        queued.Kind = FileOperationJournalKind.EmptyRecycleBin;
        queued.IsAggregate = true;
        FileOperationJournalEntry running = RunningMove();
        var journal = new FileOperationJournalData { Entries = [queued, running] };
        int identityCalls = 0;
        var evaluator = new FileOperationRecoveryEvaluator(
            path => path.Equals(running.DestinationPath, StringComparison.OrdinalIgnoreCase),
            (string path, out string identity) =>
            {
                identityCalls++;
                identity = path.Equals(running.DestinationPath, StringComparison.OrdinalIgnoreCase)
                    ? "volume:item"
                    : string.Empty;
                return identity.Length > 0;
            });

        IReadOnlyList<FileOperationJournalRecoveryChange> changes =
            FileOperationJournalRecovery.Apply(journal, evaluator, SecondUtc);

        Assert.AreEqual(2, changes.Count);
        Assert.AreEqual(FileOperationJournalState.Interrupted, queued.State);
        Assert.AreEqual(FileOperationJournalState.Succeeded, running.State);
        Assert.AreEqual("volume:item", running.ResultIdentity);
        Assert.AreEqual(1, identityCalls);
        StringAssert.Contains(queued.ErrorMessage, "不会自动重试");
    }

    [TestMethod]
    public void Recovery_MoveOnlySourceIsInterrupted()
    {
        FileOperationJournalEntry entry = RunningMove();
        FileOperationRecoveryEvaluator evaluator = CreateRecoveryEvaluator(
            existingPaths: [entry.SourcePath!],
            identities: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [entry.SourcePath!] = "volume:item"
            });

        FileOperationRecoveryDecision decision = evaluator.Evaluate(entry);

        Assert.AreEqual(FileOperationJournalState.Interrupted, decision.SuggestedState);
        Assert.AreEqual("move_only_source", decision.EvidenceCode);
        Assert.AreEqual(FileOperationJournalState.Running, entry.State, "核验器不得修改账本条目。");
    }

    [TestMethod]
    public void Recovery_MoveOnlyDestinationIsSucceeded()
    {
        FileOperationJournalEntry entry = RunningMove();
        FileOperationRecoveryEvaluator evaluator = CreateRecoveryEvaluator(
            existingPaths: [entry.DestinationPath!],
            identities: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [entry.DestinationPath!] = "volume:item"
            });

        FileOperationRecoveryDecision decision = evaluator.Evaluate(entry);

        Assert.AreEqual(FileOperationJournalState.Succeeded, decision.SuggestedState);
        Assert.AreEqual("move_only_destination", decision.EvidenceCode);
    }

    [TestMethod]
    public void Recovery_MoveBothPresentOrBothMissingIsUncertain()
    {
        FileOperationJournalEntry both = RunningMove();
        var bothIdentities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [both.SourcePath!] = "volume:item",
            [both.DestinationPath!] = "volume:item"
        };
        FileOperationRecoveryDecision bothDecision = CreateRecoveryEvaluator(
            [both.SourcePath!, both.DestinationPath!],
            bothIdentities).Evaluate(both);

        FileOperationJournalEntry neither = RunningMove();
        FileOperationRecoveryDecision neitherDecision = CreateRecoveryEvaluator(
            [],
            new Dictionary<string, string>()).Evaluate(neither);

        Assert.AreEqual(FileOperationJournalState.Uncertain, bothDecision.SuggestedState);
        Assert.AreEqual("move_both_paths_present", bothDecision.EvidenceCode);
        Assert.AreEqual(FileOperationJournalState.Uncertain, neitherDecision.SuggestedState);
        Assert.AreEqual("move_both_paths_missing", neitherDecision.EvidenceCode);
    }

    [TestMethod]
    public void Recovery_RecycleUsesOnlyReadOnlySourceEvidence()
    {
        FileOperationJournalEntry unchanged = RunningRecycle();
        FileOperationRecoveryDecision unchangedDecision = CreateRecoveryEvaluator(
            [unchanged.SourcePath!],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [unchanged.SourcePath!] = "volume:item"
            }).Evaluate(unchanged);

        FileOperationJournalEntry missing = RunningRecycle();
        FileOperationRecoveryDecision missingDecision = CreateRecoveryEvaluator(
            [],
            new Dictionary<string, string>()).Evaluate(missing);

        FileOperationJournalEntry replaced = RunningRecycle();
        FileOperationRecoveryDecision replacedDecision = CreateRecoveryEvaluator(
            [replaced.SourcePath!],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [replaced.SourcePath!] = "replacement"
            }).Evaluate(replaced);

        Assert.AreEqual(FileOperationJournalState.Interrupted, unchangedDecision.SuggestedState);
        Assert.AreEqual("recycle_source_unchanged", unchangedDecision.EvidenceCode);
        Assert.AreEqual(FileOperationJournalState.Uncertain, missingDecision.SuggestedState);
        Assert.AreEqual("recycle_source_missing", missingDecision.EvidenceCode);
        Assert.AreEqual(FileOperationJournalState.Uncertain, replacedDecision.SuggestedState);
        Assert.AreEqual("recycle_source_replaced", replacedDecision.EvidenceCode);
    }

    [TestMethod]
    public void Recovery_QueuedEntryIsInterruptedWithoutProbingOrRetrying()
    {
        int existenceCalls = 0;
        int identityCalls = 0;
        var evaluator = new FileOperationRecoveryEvaluator(
            _ =>
            {
                existenceCalls++;
                return true;
            },
            (string _, out string identity) =>
            {
                identityCalls++;
                identity = "volume:item";
                return true;
            });
        FileOperationJournalEntry entry = NewEntry();

        FileOperationRecoveryDecision decision = evaluator.Evaluate(entry);

        Assert.AreEqual(FileOperationJournalState.Interrupted, decision.SuggestedState);
        Assert.AreEqual("queued_never_started", decision.EvidenceCode);
        Assert.AreEqual(0, existenceCalls);
        Assert.AreEqual(0, identityCalls);
    }

    private static FileOperationJournalEntry NewEntry() => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        BatchId = "batch-1",
        DisplayName = "item.txt",
        RequestedUtc = FirstUtc
    };

    private static FileOperationJournalEntry RunningMove()
    {
        FileOperationJournalEntry entry = NewEntry();
        entry.Kind = FileOperationJournalKind.MoveIntoFolder;
        entry.SourcePath = @"C:\Desktop\item.txt";
        entry.DestinationPath = @"C:\Desktop\Folder\item.txt";
        entry.ExpectedIdentity = "volume:item";
        entry.ResultIdentity = "volume:item";
        Assert.IsTrue(FileOperationJournalStateMachine.TryTransition(
            entry,
            FileOperationJournalState.Running,
            FirstUtc));
        return entry;
    }

    private static FileOperationJournalEntry RunningRecycle()
    {
        FileOperationJournalEntry entry = NewEntry();
        entry.Kind = FileOperationJournalKind.MoveToRecycleBin;
        entry.SourcePath = @"C:\Desktop\item.txt";
        entry.ExpectedIdentity = "volume:item";
        Assert.IsTrue(FileOperationJournalStateMachine.TryTransition(
            entry,
            FileOperationJournalState.Running,
            FirstUtc));
        return entry;
    }

    private static FileOperationRecoveryEvaluator CreateRecoveryEvaluator(
        IEnumerable<string> existingPaths,
        IReadOnlyDictionary<string, string> identities)
    {
        var existing = new HashSet<string>(existingPaths, StringComparer.OrdinalIgnoreCase);
        return new FileOperationRecoveryEvaluator(
            path => existing.Contains(path),
            (string path, out string identity) =>
            {
                if (identities.TryGetValue(path, out string? captured))
                {
                    identity = captured;
                    return true;
                }

                identity = string.Empty;
                return false;
            });
    }

    private static string CreateTestDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "DesktopOrganizer.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class CapturingStore : IFileOperationJournalStore
    {
        private readonly int? _failSaveNumber;
        private int _saveCount;

        public CapturingStore(int? failSaveNumber = null)
        {
            _failSaveNumber = failSaveNumber;
        }

        public List<FileOperationJournalState> SavedStates { get; } = new();

        public FileOperationJournalLoadResult Load() => new(
            FileOperationJournalLoadState.Missing,
            new FileOperationJournalData(),
            ErrorMessage: null);

        public bool TrySave(FileOperationJournalData journal, out string? errorMessage)
        {
            _saveCount++;
            FileOperationJournalEntry? latest = journal.Entries.LastOrDefault();
            if (latest != null)
            {
                SavedStates.Add(latest.State);
            }
            if (_failSaveNumber == _saveCount)
            {
                errorMessage = "expected save failure";
                return false;
            }

            journal.SaveGeneration++;
            errorMessage = null;
            return true;
        }
    }
}
