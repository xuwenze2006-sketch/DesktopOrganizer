using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class InboxQueueManagerTests
{
    private static readonly DesktopCategoryDefinition Documents =
        new("documents", "文档", 40);

    [TestMethod]
    public void Reconcile_IncompleteScan_DoesNotEstablishBaselineOrMutateInbox()
    {
        Dictionary<string, InboxItemInfo> inbox = CreateInbox(
            "keep.txt",
            Physical(@"C:\Desktop\keep.txt", "volume:keep", 1),
            ClassificationReliability.Reliable);
        string before = JsonSerializer.Serialize(inbox);

        InboxReconcileResult result = InboxQueueManager.Reconcile(
            inbox,
            baselineEstablished: false,
            completeScan: false,
            previousIdentities: new Dictionary<string, DesktopItemIdentityInfo>(),
            currentIdentities: new Dictionary<string, DesktopItemIdentityInfo>(),
            suggestions: new Dictionary<string, InboxClassificationSuggestion>(),
            utcNow: DateTime.UnixEpoch);

        Assert.IsFalse(result.BaselineEstablished);
        Assert.IsFalse(result.Changed);
        Assert.AreEqual(before, JsonSerializer.Serialize(inbox));
    }

    [TestMethod]
    public void Reconcile_FirstCompleteScan_EstablishesBaselineWithoutFloodingExistingDesktop()
    {
        var inbox = new Dictionary<string, InboxItemInfo>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, DesktopItemIdentityInfo> current = new()
        {
            ["existing.txt"] = Physical(@"C:\Desktop\existing.txt", "volume:existing", 10)
        };

        InboxReconcileResult result = InboxQueueManager.Reconcile(
            inbox,
            baselineEstablished: false,
            completeScan: true,
            previousIdentities: new Dictionary<string, DesktopItemIdentityInfo>(),
            currentIdentities: current,
            suggestions: Suggestions(("existing.txt", Reliable("扩展名 .txt"))),
            utcNow: DateTime.UnixEpoch);

        Assert.IsTrue(result.BaselineEstablished);
        Assert.IsTrue(result.BaselineChanged);
        Assert.AreEqual(0, result.Added);
        Assert.AreEqual(0, inbox.Count);
    }

    [TestMethod]
    public void Reconcile_KnownBaseline_AddsOnlyNewIdentityAndClonesEvidence()
    {
        var inbox = new Dictionary<string, InboxItemInfo>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, DesktopItemIdentityInfo> previous = new()
        {
            ["old.txt"] = Physical(@"C:\Desktop\old.txt", "volume:old", 11)
        };
        Dictionary<string, DesktopItemIdentityInfo> current = new()
        {
            ["old.txt"] = Physical(@"C:\Desktop\old.txt", "volume:old", 11),
            ["new.txt"] = Physical(@"C:\Desktop\new.txt", "volume:new", 12)
        };
        DateTime now = DateTime.UnixEpoch.AddHours(1);

        InboxReconcileResult result = InboxQueueManager.Reconcile(
            inbox,
            baselineEstablished: true,
            completeScan: true,
            previous,
            current,
            Suggestions(
                ("old.txt", Reliable("扩展名 .txt")),
                ("new.txt", Reliable("扩展名 .txt"))),
            now);

        Assert.IsTrue(result.Changed);
        Assert.AreEqual(1, result.Added);
        Assert.AreEqual(1, inbox.Count);
        InboxItemInfo entry = inbox["new.txt"];
        Assert.AreEqual("volume:new", entry.Identity.FileId);
        Assert.AreEqual("documents", entry.SuggestedCategoryKey);
        Assert.AreEqual("扩展名 .txt", entry.MatchReason);
        Assert.AreEqual(ClassificationReliability.Reliable, entry.Reliability);
        Assert.AreEqual(now, entry.DetectedUtc);

        current["new.txt"].FileId = "mutated";
        Assert.AreEqual("volume:new", entry.Identity.FileId);
    }

    [TestMethod]
    public void Reconcile_RenamedStableIdentity_MigratesQueueKeyAndPreservesReviewState()
    {
        DateTime detected = DateTime.UnixEpoch.AddMinutes(1);
        Dictionary<string, InboxItemInfo> inbox = CreateInbox(
            "old.txt",
            Physical(@"C:\Desktop\old.txt", "volume:same", 20),
            ClassificationReliability.Reliable,
            InboxReviewState.Deferred,
            detected);
        Dictionary<string, DesktopItemIdentityInfo> previous = new()
        {
            ["old.txt"] = Physical(@"C:\Desktop\old.txt", "volume:same", 20)
        };
        Dictionary<string, DesktopItemIdentityInfo> current = new()
        {
            ["renamed.txt"] = Physical(@"C:\Desktop\renamed.txt", "volume:same", 20)
        };

        InboxReconcileResult result = InboxQueueManager.Reconcile(
            inbox,
            baselineEstablished: true,
            completeScan: true,
            previous,
            current,
            Suggestions(("renamed.txt", Reliable("扩展名 .txt"))),
            DateTime.UnixEpoch.AddHours(2));

        Assert.IsTrue(result.Changed);
        Assert.IsFalse(inbox.ContainsKey("old.txt"));
        Assert.IsTrue(inbox.ContainsKey("renamed.txt"));
        Assert.AreEqual(InboxReviewState.Deferred, inbox["renamed.txt"].ReviewState);
        Assert.AreEqual(detected, inbox["renamed.txt"].DetectedUtc);
    }

    [TestMethod]
    public void Reconcile_SameNameReplacement_ReplacesStaleEntryAsNewItem()
    {
        Dictionary<string, InboxItemInfo> inbox = CreateInbox(
            "same.txt",
            Physical(@"C:\Desktop\same.txt", "volume:old", 30),
            ClassificationReliability.Reliable,
            InboxReviewState.Deferred,
            DateTime.UnixEpoch);
        Dictionary<string, DesktopItemIdentityInfo> previous = new()
        {
            ["same.txt"] = Physical(@"C:\Desktop\same.txt", "volume:old", 30)
        };
        Dictionary<string, DesktopItemIdentityInfo> current = new()
        {
            ["same.txt"] = Physical(@"C:\Desktop\same.txt", "volume:new", 31)
        };
        DateTime now = DateTime.UnixEpoch.AddDays(1);

        InboxQueueManager.Reconcile(
            inbox,
            baselineEstablished: true,
            completeScan: true,
            previous,
            current,
            Suggestions(("same.txt", Reliable("扩展名 .txt"))),
            now);

        Assert.AreEqual("volume:new", inbox["same.txt"].Identity.FileId);
        Assert.AreEqual(InboxReviewState.Pending, inbox["same.txt"].ReviewState);
        Assert.AreEqual(now, inbox["same.txt"].DetectedUtc);
    }

    [TestMethod]
    public void Reconcile_UnchangedCompleteScan_IsIdempotent()
    {
        DesktopItemIdentityInfo identity = Physical(
            @"C:\Desktop\unchanged.txt",
            "volume:unchanged",
            35);
        Dictionary<string, InboxItemInfo> inbox = CreateInbox(
            "unchanged.txt",
            identity,
            ClassificationReliability.Reliable);
        string before = JsonSerializer.Serialize(inbox);

        InboxReconcileResult result = InboxQueueManager.Reconcile(
            inbox,
            baselineEstablished: true,
            completeScan: true,
            previousIdentities: Current(("unchanged.txt", identity)),
            currentIdentities: Current(("unchanged.txt", identity)),
            suggestions: Suggestions(("unchanged.txt", Reliable("扩展名 .txt"))),
            utcNow: DateTime.UnixEpoch.AddDays(2));

        Assert.IsFalse(result.Changed);
        Assert.AreEqual(0, result.Updated);
        Assert.AreEqual(before, JsonSerializer.Serialize(inbox));
    }

    [TestMethod]
    public void CreateAcceptancePlan_UnreliableSuggestion_IsRejectedWithoutMutation()
    {
        DesktopItemIdentityInfo identity = Physical(@"C:\Desktop\uncertain.txt", "volume:u", 40);
        Dictionary<string, InboxItemInfo> inbox = CreateInbox(
            "uncertain.txt",
            identity,
            ClassificationReliability.Conservative);
        string before = JsonSerializer.Serialize(inbox);

        InboxActionResult result = InboxQueueManager.TryCreateAcceptancePlan(
            inbox,
            Current(("uncertain.txt", identity)),
            Array.Empty<GroupInfo>(),
            "uncertain.txt",
            out InboxAcceptancePlan? plan);

        Assert.AreEqual(InboxActionOutcome.Unreliable, result.Outcome);
        Assert.IsNull(plan);
        Assert.AreEqual(before, JsonSerializer.Serialize(inbox));
    }

    [TestMethod]
    public void CreateAcceptancePlan_ManualGroupMember_IsProtectedWithoutMutation()
    {
        DesktopItemIdentityInfo identity = Physical(@"C:\Desktop\manual.txt", "volume:m", 50);
        Dictionary<string, InboxItemInfo> inbox = CreateInbox(
            "manual.txt",
            identity,
            ClassificationReliability.Reliable);
        var manual = new GroupInfo
        {
            Id = "manual",
            IsAutoCategory = false,
            ItemNames = ["MANUAL.TXT"]
        };
        string beforeInbox = JsonSerializer.Serialize(inbox);
        string beforeGroups = JsonSerializer.Serialize(new[] { manual });

        InboxActionResult result = InboxQueueManager.TryCreateAcceptancePlan(
            inbox,
            Current(("manual.txt", identity)),
            new[] { manual },
            "manual.txt",
            out InboxAcceptancePlan? plan);

        Assert.AreEqual(InboxActionOutcome.ManualGroupProtected, result.Outcome);
        Assert.IsNull(plan);
        Assert.AreEqual(beforeInbox, JsonSerializer.Serialize(inbox));
        Assert.AreEqual(beforeGroups, JsonSerializer.Serialize(new[] { manual }));
    }

    [TestMethod]
    public void CreateAcceptancePlan_UserRuleGroupIsNotTreatedAsManualProtection()
    {
        DesktopItemIdentityInfo identity = Physical(@"C:\Desktop\rule.txt", "volume:r", 51);
        Dictionary<string, InboxItemInfo> inbox = CreateInbox(
            "rule.txt",
            identity,
            ClassificationReliability.Reliable);
        var ruleGroup = new GroupInfo
        {
            Id = "rule-group",
            UserRuleId = "rule-1",
            ItemNames = ["rule.txt"]
        };

        InboxActionResult result = InboxQueueManager.TryCreateAcceptancePlan(
            inbox,
            Current(("rule.txt", identity)),
            new[] { ruleGroup },
            "rule.txt",
            out InboxAcceptancePlan? plan);

        Assert.AreEqual(InboxActionOutcome.Planned, result.Outcome);
        Assert.IsNotNull(plan);
        Assert.AreEqual(1, inbox.Count);
    }

    [TestMethod]
    public void CreateAcceptancePlan_ManuallyAssignedAutoMember_IsProtectedWithoutMutation()
    {
        DesktopItemIdentityInfo identity = Physical(@"C:\Desktop\manual-auto.txt", "volume:ma", 52);
        Dictionary<string, InboxItemInfo> inbox = CreateInbox(
            "manual-auto.txt",
            identity,
            ClassificationReliability.Reliable);
        var automatic = new GroupInfo
        {
            Id = "automatic",
            IsAutoCategory = true,
            ItemNames = ["manual-auto.txt"],
            ManuallyAssignedItemNames = ["MANUAL-AUTO.TXT"]
        };
        string before = JsonSerializer.Serialize(new { inbox, automatic });

        InboxActionResult result = InboxQueueManager.TryCreateAcceptancePlan(
            inbox,
            Current(("manual-auto.txt", identity)),
            new[] { automatic },
            "manual-auto.txt",
            out InboxAcceptancePlan? plan);

        Assert.AreEqual(InboxActionOutcome.ManualGroupProtected, result.Outcome);
        Assert.IsNull(plan);
        Assert.AreEqual(before, JsonSerializer.Serialize(new { inbox, automatic }));
    }

    [TestMethod]
    public void ReliableAcceptance_PlanIsSideEffectFreeAndCompletionConsumesMatchingEntry()
    {
        DesktopItemIdentityInfo identity = Physical(@"C:\Desktop\accept.txt", "volume:a", 60);
        Dictionary<string, InboxItemInfo> inbox = CreateInbox(
            "accept.txt",
            identity,
            ClassificationReliability.Reliable,
            InboxReviewState.Deferred);

        InboxActionResult planned = InboxQueueManager.TryCreateAcceptancePlan(
            inbox,
            Current(("accept.txt", identity)),
            Array.Empty<GroupInfo>(),
            "accept.txt",
            out InboxAcceptancePlan? plan);

        Assert.AreEqual(InboxActionOutcome.Planned, planned.Outcome);
        Assert.IsFalse(planned.Changed);
        Assert.IsNotNull(plan);
        Assert.AreEqual("documents", plan.TargetCategory.Key);
        Assert.AreEqual(1, inbox.Count);

        InboxActionResult completed = InboxQueueManager.CompleteAcceptance(
            inbox,
            Current(("accept.txt", identity)),
            plan);

        Assert.AreEqual(InboxActionOutcome.Applied, completed.Outcome);
        Assert.IsTrue(completed.Changed);
        Assert.AreEqual(0, inbox.Count);
    }

    [TestMethod]
    public void AutomaticAcceptance_ExcludesDeferredUnreliableManualAndStaleEntries()
    {
        DesktopItemIdentityInfo ready = Physical(@"C:\Desktop\ready.txt", "volume:ready", 70);
        DesktopItemIdentityInfo deferred = Physical(@"C:\Desktop\deferred.txt", "volume:d", 71);
        DesktopItemIdentityInfo uncertain = Physical(@"C:\Desktop\uncertain.txt", "volume:u", 72);
        DesktopItemIdentityInfo manualIdentity = Physical(@"C:\Desktop\manual.txt", "volume:m", 73);
        DesktopItemIdentityInfo stale = Physical(@"C:\Desktop\stale.txt", "volume:old", 74);
        var inbox = new Dictionary<string, InboxItemInfo>(StringComparer.OrdinalIgnoreCase);
        AddInbox(inbox, "ready.txt", ready, ClassificationReliability.Reliable);
        AddInbox(inbox, "deferred.txt", deferred, ClassificationReliability.Reliable, InboxReviewState.Deferred);
        AddInbox(inbox, "uncertain.txt", uncertain, ClassificationReliability.Conservative);
        AddInbox(inbox, "manual.txt", manualIdentity, ClassificationReliability.Reliable);
        AddInbox(inbox, "stale.txt", stale, ClassificationReliability.Reliable);
        var manual = new GroupInfo { Id = "manual", ItemNames = ["manual.txt"] };

        IReadOnlyList<InboxAcceptancePlan> plans = InboxQueueManager.PlanAutomaticAcceptances(
            inbox,
            Current(
                ("ready.txt", ready),
                ("deferred.txt", deferred),
                ("uncertain.txt", uncertain),
                ("manual.txt", manualIdentity),
                ("stale.txt", Physical(@"C:\Desktop\stale.txt", "volume:new", 75))),
            new[] { manual },
            paused: false);

        Assert.AreEqual(1, plans.Count);
        Assert.AreEqual("ready.txt", plans[0].DisplayName);
        Assert.AreEqual(0, InboxQueueManager.PlanAutomaticAcceptances(
            inbox,
            Current(("ready.txt", ready)),
            Array.Empty<GroupInfo>(),
            paused: true).Count);
    }

    [TestMethod]
    public void RejectedStaleActions_DoNotMutateQueueOrGroups()
    {
        DesktopItemIdentityInfo oldIdentity = Physical(@"C:\Desktop\stale.txt", "volume:old", 80);
        Dictionary<string, InboxItemInfo> inbox = CreateInbox(
            "stale.txt",
            oldIdentity,
            ClassificationReliability.Reliable);
        var manual = new GroupInfo { Id = "manual", ItemNames = new List<string>() };
        var groups = new List<GroupInfo> { manual };
        var free = new Dictionary<string, IconPosition> { ["stale.txt"] = new() { X = 1, Y = 2 } };
        var original = new Dictionary<string, IconPosition>();
        Dictionary<string, DesktopItemIdentityInfo> current = Current(
            ("stale.txt", Physical(@"C:\Desktop\stale.txt", "volume:new", 81)));
        string before = JsonSerializer.Serialize(new { inbox, groups, free, original });

        InboxActionResult deferred = InboxQueueManager.Defer(
            inbox,
            current,
            "stale.txt",
            DateTime.UnixEpoch);
        InboxActionResult left = InboxQueueManager.LeaveOnDesktop(inbox, current, "stale.txt");
        InboxActionResult moved = InboxQueueManager.TryMoveToManualGroup(
            inbox,
            current,
            groups,
            free,
            original,
            "stale.txt",
            "manual");

        Assert.AreEqual(InboxActionOutcome.StaleIdentity, deferred.Outcome);
        Assert.AreEqual(InboxActionOutcome.StaleIdentity, left.Outcome);
        Assert.AreEqual(InboxActionOutcome.StaleIdentity, moved.Outcome);
        Assert.AreEqual(before, JsonSerializer.Serialize(new { inbox, groups, free, original }));
    }

    [TestMethod]
    public void DeferAndLeaveOnDesktop_OnlyChangeQueueState()
    {
        DesktopItemIdentityInfo identity = Physical(@"C:\Desktop\later.txt", "volume:l", 90);
        Dictionary<string, InboxItemInfo> inbox = CreateInbox(
            "later.txt",
            identity,
            ClassificationReliability.Reliable);
        DateTime now = DateTime.UnixEpoch.AddMinutes(5);

        InboxActionResult deferred = InboxQueueManager.Defer(
            inbox,
            Current(("later.txt", identity)),
            "later.txt",
            now);

        Assert.AreEqual(InboxActionOutcome.Applied, deferred.Outcome);
        Assert.AreEqual(InboxReviewState.Deferred, inbox["later.txt"].ReviewState);
        Assert.AreEqual(now, inbox["later.txt"].UpdatedUtc);
        Assert.AreEqual(InboxActionOutcome.NoChange, InboxQueueManager.Defer(
            inbox,
            Current(("later.txt", identity)),
            "later.txt",
            now.AddMinutes(1)).Outcome);

        InboxActionResult left = InboxQueueManager.LeaveOnDesktop(
            inbox,
            Current(("later.txt", identity)),
            "later.txt");

        Assert.AreEqual(InboxActionOutcome.Applied, left.Outcome);
        Assert.AreEqual(0, inbox.Count);
    }

    [TestMethod]
    public void MoveToManualGroup_UnreliableItemMovesExplicitlyAndClearsVirtualState()
    {
        DesktopItemIdentityInfo identity = Physical(@"C:\Desktop\folder", "volume:f", 100, isDirectory: true);
        Dictionary<string, InboxItemInfo> inbox = CreateInbox(
            "folder",
            identity,
            ClassificationReliability.Conservative);
        var automatic = new GroupInfo
        {
            Id = "automatic",
            IsAutoCategory = true,
            ItemNames = ["folder"]
        };
        var manual = new GroupInfo
        {
            Id = "manual",
            IsAutoCategory = false,
            ItemNames = new List<string>()
        };
        var groups = new List<GroupInfo> { automatic, manual };
        var free = new Dictionary<string, IconPosition> { ["FOLDER"] = new() { X = 10, Y = 20 } };
        var original = new Dictionary<string, IconPosition> { ["folder"] = new() { X = 1, Y = 2 } };

        InboxActionResult result = InboxQueueManager.TryMoveToManualGroup(
            inbox,
            Current(("folder", identity)),
            groups,
            free,
            original,
            "FOLDER",
            "manual");

        Assert.AreEqual(InboxActionOutcome.Applied, result.Outcome);
        Assert.AreEqual(0, inbox.Count);
        Assert.AreEqual(0, automatic.ItemNames.Count);
        CollectionAssert.AreEqual(new[] { "folder" }, manual.ItemNames);
        CollectionAssert.AreEqual(new[] { "folder" }, manual.ManuallyAssignedItemNames);
        Assert.AreEqual(GroupSortMode.Custom, manual.SortMode);
        Assert.AreEqual(0, free.Count);
        Assert.AreEqual(0, original.Count);
    }

    [TestMethod]
    public void MoveToManualGroup_AutoTargetIsRejectedWithoutAnyMutation()
    {
        DesktopItemIdentityInfo identity = Physical(@"C:\Desktop\item.txt", "volume:i", 110);
        Dictionary<string, InboxItemInfo> inbox = CreateInbox(
            "item.txt",
            identity,
            ClassificationReliability.Reliable);
        var automatic = new GroupInfo
        {
            Id = "automatic",
            IsAutoCategory = true,
            ItemNames = new List<string>()
        };
        var groups = new List<GroupInfo> { automatic };
        var free = new Dictionary<string, IconPosition> { ["item.txt"] = new() { X = 3, Y = 4 } };
        var original = new Dictionary<string, IconPosition>();
        string before = JsonSerializer.Serialize(new { inbox, groups, free, original });

        InboxActionResult result = InboxQueueManager.TryMoveToManualGroup(
            inbox,
            Current(("item.txt", identity)),
            groups,
            free,
            original,
            "item.txt",
            "automatic");

        Assert.AreEqual(InboxActionOutcome.InvalidManualGroup, result.Outcome);
        Assert.AreEqual(before, JsonSerializer.Serialize(new { inbox, groups, free, original }));
    }

    private static Dictionary<string, InboxItemInfo> CreateInbox(
        string name,
        DesktopItemIdentityInfo identity,
        ClassificationReliability reliability,
        InboxReviewState state = InboxReviewState.Pending,
        DateTime? detectedUtc = null)
    {
        var result = new Dictionary<string, InboxItemInfo>(StringComparer.OrdinalIgnoreCase);
        AddInbox(result, name, identity, reliability, state, detectedUtc);
        return result;
    }

    private static void AddInbox(
        Dictionary<string, InboxItemInfo> inbox,
        string name,
        DesktopItemIdentityInfo identity,
        ClassificationReliability reliability,
        InboxReviewState state = InboxReviewState.Pending,
        DateTime? detectedUtc = null)
    {
        inbox[name] = new InboxItemInfo
        {
            Identity = identity,
            DetectedUtc = detectedUtc ?? DateTime.UnixEpoch,
            UpdatedUtc = detectedUtc ?? DateTime.UnixEpoch,
            SuggestedCategoryKey = Documents.Key,
            SuggestedCategoryName = Documents.DisplayName,
            SuggestedCategoryOrder = Documents.Order,
            MatchReason = reliability == ClassificationReliability.Reliable
                ? "扩展名 .txt"
                : "路径状态无法确认",
            Reliability = reliability,
            ReviewState = state
        };
    }

    private static InboxClassificationSuggestion Reliable(string reason) =>
        new(Documents, ClassificationReliability.Reliable, reason);

    private static Dictionary<string, InboxClassificationSuggestion> Suggestions(
        params (string Name, InboxClassificationSuggestion Suggestion)[] values) =>
        values.ToDictionary(
            pair => pair.Name,
            pair => pair.Suggestion,
            StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, DesktopItemIdentityInfo> Current(
        params (string Name, DesktopItemIdentityInfo Identity)[] values) =>
        values.ToDictionary(
            pair => pair.Name,
            pair => pair.Identity,
            StringComparer.OrdinalIgnoreCase);

    private static DesktopItemIdentityInfo Physical(
        string path,
        string? fileId,
        long? creationTimeUtcTicks,
        bool isDirectory = false) => new()
    {
        Kind = DesktopItemKind.FileSystem,
        LastKnownPath = path,
        FileId = fileId,
        CreationTimeUtcTicks = creationTimeUtcTicks,
        IsDirectory = isDirectory
    };
}
