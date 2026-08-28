using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class OrganizationRuleEngineTests
{
    private static readonly DateTimeOffset WindowStart = new(
        2026,
        8,
        1,
        0,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd = new(
        2026,
        8,
        31,
        23,
        59,
        59,
        TimeSpan.Zero);

    [TestMethod]
    public void Create_DefaultsToDraftAndCannotEnableWithoutPreview()
    {
        OrganizationRule rule = Rule(
            [OrganizationRuleCondition.NameIncludes("报告")],
            OrganizationRuleAction.AddTag("重要"));

        Assert.AreEqual(OrganizationRuleState.Draft, rule.State);
        Assert.ThrowsExactly<InvalidOperationException>(() => rule.Enable());
    }

    [TestMethod]
    public void Preview_ReturnsNewPreviewedRuleThenAllowsStrictEnableTransition()
    {
        OrganizationRule draft = Rule(
            [OrganizationRuleCondition.NameIncludes("报告")],
            OrganizationRuleAction.AddTag("重要"));

        OrganizationRulePreview preview = OrganizationRuleEngine.Preview(
            draft,
            [Item("报告.docx", extension: ".docx")]);
        OrganizationRule enabled = preview.PreviewedRule.Enable();

        Assert.AreEqual(OrganizationRuleState.Draft, draft.State);
        Assert.AreEqual(OrganizationRuleState.Previewed, preview.PreviewedRule.State);
        Assert.AreEqual(OrganizationRuleState.Enabled, enabled.State);
        Assert.AreEqual(OrganizationRuleState.Previewed, preview.PreviewedRule.State);
        Assert.ThrowsExactly<InvalidOperationException>(() => OrganizationRuleEngine.Preview(
            preview.PreviewedRule,
            [Item("报告.docx", extension: ".docx")]));
    }

    [TestMethod]
    public void Preview_AllSupportedConditionsMatchAndExplainEveryCondition()
    {
        OrganizationRule rule = Rule(
            [
                OrganizationRuleCondition.ExtensionIs("docx", ".pdf"),
                OrganizationRuleCondition.NameIncludes("项目报告"),
                OrganizationRuleCondition.Is(OrganizationRuleItemKind.File),
                OrganizationRuleCondition.CreatedBetween(WindowStart, WindowEnd),
                OrganizationRuleCondition.ModifiedBetween(WindowStart, WindowEnd),
                OrganizationRuleCondition.HasTopLevelProjectMarker()
            ],
            OrganizationRuleAction.AddToVirtualGroup("reports", "项目报告"));
        OrganizationRuleItemContext item = Item(
            "项目报告.DOCX",
            extension: "DOCX",
            creationTimeUtc: WindowStart,
            modifiedTimeUtc: WindowEnd,
            hasMarker: true,
            marker: "package.json");

        OrganizationRulePreview preview = OrganizationRuleEngine.Preview(rule, [item]);
        OrganizationRulePreviewItem result = AssertExactlyOne(preview.Items);
        OrganizationRulePlannedAction action = AssertExactlyOne(preview.ExecutionPlan.Actions);

        Assert.IsTrue(result.IsMatch);
        Assert.IsTrue(result.WillExecute);
        Assert.IsNull(result.Conflict);
        Assert.HasCount(6, result.Conditions);
        Assert.IsTrue(result.Conditions.All(evaluation =>
            evaluation.Outcome == OrganizationRuleConditionOutcome.Matched));
        StringAssert.Contains(
            result.Conditions.Single(evaluation =>
                evaluation.ConditionKind == OrganizationRuleConditionKind.TopLevelProjectMarker).Reason,
            "package.json");
        Assert.AreEqual("虚拟分组“项目报告”", result.TargetDescription);
        Assert.AreEqual("reports", action.Action.TargetId);
        StringAssert.Contains(action.ReasonSummary, "名称包含");
    }

    [TestMethod]
    public void Preview_EvaluatesEveryConditionWhenSomeDoNotMatch()
    {
        OrganizationRule rule = Rule(
            [
                OrganizationRuleCondition.ExtensionIs(".pdf"),
                OrganizationRuleCondition.NameIncludes("报告"),
                OrganizationRuleCondition.Is(OrganizationRuleItemKind.Folder)
            ],
            OrganizationRuleAction.SendToInbox());

        OrganizationRulePreviewItem result = AssertExactlyOne(
            OrganizationRuleEngine.Preview(
                rule,
                [Item("notes.txt", extension: ".txt")])
            .Items);

        Assert.IsFalse(result.IsMatch);
        Assert.IsFalse(result.WillExecute);
        Assert.HasCount(3, result.Conditions);
        Assert.IsTrue(result.Conditions.All(evaluation =>
            evaluation.Outcome == OrganizationRuleConditionOutcome.NotMatched));
    }

    [TestMethod]
    public void Preview_MissingDateAndMarkerFactsAreUnknownAndNeverPlanned()
    {
        OrganizationRule rule = Rule(
            [
                OrganizationRuleCondition.CreatedBetween(WindowStart, WindowEnd),
                OrganizationRuleCondition.ModifiedBetween(WindowStart, WindowEnd),
                OrganizationRuleCondition.HasTopLevelProjectMarker()
            ],
            OrganizationRuleAction.SendToInbox());

        OrganizationRulePreview preview = OrganizationRuleEngine.Preview(
            rule,
            [Item("Unknown", extension: "", hasMarker: null)]);
        OrganizationRulePreviewItem result = AssertExactlyOne(preview.Items);

        Assert.IsFalse(result.IsMatch);
        Assert.IsFalse(result.WillExecute);
        Assert.IsTrue(result.Conditions.All(evaluation =>
            evaluation.Outcome == OrganizationRuleConditionOutcome.Unknown));
        Assert.IsEmpty(preview.ExecutionPlan.Actions);
    }

    [TestMethod]
    public void Preview_DateWindowIncludesBothBoundaries()
    {
        OrganizationRule rule = Rule(
            [OrganizationRuleCondition.CreatedBetween(WindowStart, WindowEnd)],
            OrganizationRuleAction.AddTag("本月"));

        OrganizationRulePreview preview = OrganizationRuleEngine.Preview(
            rule,
            [
                Item("Start", extension: "", creationTimeUtc: WindowStart),
                Item("End", extension: "", creationTimeUtc: WindowEnd),
                Item("Before", extension: "", creationTimeUtc: WindowStart - TimeSpan.FromTicks(1)),
                Item("After", extension: "", creationTimeUtc: WindowEnd + TimeSpan.FromTicks(1))
            ]);

        CollectionAssert.AreEqual(
            new[] { "End", "Start" },
            preview.ExecutionPlan.Actions.Select(action => action.DisplayName).ToArray());
    }

    [TestMethod]
    public void Preview_NameAndExtensionMatchingAreCaseInsensitiveAndNormalizeDots()
    {
        OrganizationRule rule = Rule(
            [
                OrganizationRuleCondition.ExtensionIs("PDF"),
                OrganizationRuleCondition.NameIncludes("REPORT")
            ],
            OrganizationRuleAction.AddTag("文档"));

        OrganizationRulePreviewItem result = AssertExactlyOne(
            OrganizationRuleEngine.Preview(
                rule,
                [Item("annual report.pdf", extension: "pdf")])
            .Items);

        Assert.IsTrue(result.IsMatch);
        Assert.IsTrue(result.WillExecute);
    }

    [TestMethod]
    public void Preview_ManualGroupBlocksVirtualGroupActionAndPlanExcludesConflict()
    {
        OrganizationRule rule = Rule(
            [OrganizationRuleCondition.ExtensionIs("txt")],
            OrganizationRuleAction.AddToVirtualGroup("target", "自动文档"));

        OrganizationRulePreview preview = OrganizationRuleEngine.Preview(
            rule,
            [
                Item(
                    "Manual.txt",
                    extension: ".txt",
                    manualGroupId: "manual",
                    manualGroupName: "我的分组"),
                Item("Free.txt", extension: ".txt")
            ]);
        OrganizationRulePreviewItem conflict = preview.Items.Single(item =>
            item.DisplayName == "Manual.txt");

        Assert.IsTrue(conflict.IsMatch);
        Assert.IsFalse(conflict.WillExecute);
        Assert.IsNotNull(conflict.Conflict);
        Assert.AreEqual(
            OrganizationRuleConflictKind.ManualGroupProtected,
            conflict.Conflict.Kind);
        StringAssert.Contains(conflict.Conflict.Message, "我的分组");
        Assert.HasCount(1, preview.ExecutionPlan.Actions);
        Assert.AreEqual("Free.txt", preview.ExecutionPlan.Actions[0].DisplayName);
    }

    [TestMethod]
    public void Preview_ManualGroupBlocksInboxAction()
    {
        OrganizationRule rule = Rule(
            [OrganizationRuleCondition.NameIncludes("待处理")],
            OrganizationRuleAction.SendToInbox());

        OrganizationRulePreview preview = OrganizationRuleEngine.Preview(
            rule,
            [Item(
                "待处理.txt",
                extension: ".txt",
                manualGroupId: "manual",
                manualGroupName: "手工")]);
        OrganizationRulePreviewItem item = AssertExactlyOne(preview.Items);

        Assert.IsNotNull(item.Conflict);
        Assert.IsEmpty(preview.ExecutionPlan.Actions);
        Assert.AreEqual("待整理收件箱", item.TargetDescription);
    }

    [TestMethod]
    public void Preview_TagActionDoesNotRewriteOrConflictWithManualGroup()
    {
        OrganizationRule rule = Rule(
            [OrganizationRuleCondition.NameIncludes("报告")],
            OrganizationRuleAction.AddTag("重要"));

        OrganizationRulePreview preview = OrganizationRuleEngine.Preview(
            rule,
            [Item(
                "报告.docx",
                extension: ".docx",
                manualGroupId: "manual",
                manualGroupName: "手工")]);
        OrganizationRulePreviewItem item = AssertExactlyOne(preview.Items);

        Assert.IsNull(item.Conflict);
        Assert.IsTrue(item.WillExecute);
        Assert.HasCount(1, preview.ExecutionPlan.Actions);
        Assert.AreEqual(OrganizationRuleActionKind.AddTag, preview.ExecutionPlan.Actions[0].Action.Kind);
        Assert.AreEqual("标签“重要”", item.TargetDescription);
    }

    [TestMethod]
    public void Preview_IsPureAndCopiesRuleConditions()
    {
        var mutableConditions = new List<OrganizationRuleCondition>
        {
            OrganizationRuleCondition.NameIncludes("safe")
        };
        OrganizationRule rule = OrganizationRule.Create(
            "纯函数规则",
            mutableConditions,
            OrganizationRuleAction.AddTag("local"),
            id: "pure-rule");
        var contexts = new List<OrganizationRuleItemContext>
        {
            Item("safe.txt", extension: ".txt", location: @"?:\invalid\safe.txt")
        };

        OrganizationRulePreview first = OrganizationRuleEngine.Preview(rule, contexts);
        mutableConditions.Clear();
        contexts[0] = Item("changed.txt", extension: ".txt");

        Assert.AreEqual(OrganizationRuleState.Draft, rule.State);
        Assert.HasCount(1, rule.Conditions);
        Assert.HasCount(1, first.Items);
        Assert.AreEqual("safe.txt", first.Items[0].DisplayName);
        Assert.AreEqual(@"?:\invalid\safe.txt", first.Items[0].Location);
        Assert.HasCount(1, first.ExecutionPlan.Actions);
    }

    [TestMethod]
    public void Preview_UsesStableItemOrderingForPreviewAndPlan()
    {
        OrganizationRule rule = Rule(
            [OrganizationRuleCondition.Is(OrganizationRuleItemKind.File)],
            OrganizationRuleAction.AddTag("文件"));

        OrganizationRulePreview preview = OrganizationRuleEngine.Preview(
            rule,
            [
                Item("zeta", extension: ""),
                Item("Beta", extension: ""),
                Item("alpha", extension: "")
            ]);

        string[] expected = ["alpha", "Beta", "zeta"];
        CollectionAssert.AreEqual(
            expected,
            preview.Items.Select(item => item.DisplayName).ToArray());
        CollectionAssert.AreEqual(
            expected,
            preview.ExecutionPlan.Actions.Select(action => action.DisplayName).ToArray());
    }

    [TestMethod]
    public void Preview_RejectsConditionlessRuleWithoutChangingState()
    {
        OrganizationRule rule = Rule([], OrganizationRuleAction.SendToInbox());

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            OrganizationRuleEngine.Preview(rule, [Item("anything", extension: "")]));
        Assert.AreEqual(OrganizationRuleState.Draft, rule.State);
    }

    [TestMethod]
    public void ConditionFactoriesRejectInvalidDateWindowsAndEmptyTargets()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            OrganizationRuleCondition.CreatedBetween(null, null));
        Assert.ThrowsExactly<ArgumentException>(() =>
            OrganizationRuleCondition.ModifiedBetween(WindowEnd, WindowStart));
        Assert.ThrowsExactly<ArgumentException>(() =>
            OrganizationRuleAction.AddToVirtualGroup(" ", "分组"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            OrganizationRuleAction.AddTag(" "));
    }

    private static OrganizationRule Rule(
        IEnumerable<OrganizationRuleCondition> conditions,
        OrganizationRuleAction action) =>
        OrganizationRule.Create(
            "测试规则",
            conditions,
            action,
            id: "rule-1");

    private static OrganizationRuleItemContext Item(
        string displayName,
        string extension,
        string? location = null,
        bool isDirectory = false,
        DateTimeOffset? creationTimeUtc = null,
        DateTimeOffset? modifiedTimeUtc = null,
        bool? hasMarker = false,
        string? marker = null,
        string? manualGroupId = null,
        string? manualGroupName = null) =>
        new(
            displayName,
            location ?? $"opaque:{displayName}",
            extension,
            isDirectory,
            creationTimeUtc,
            modifiedTimeUtc,
            hasMarker,
            marker,
            manualGroupId,
            manualGroupName);

    private static T AssertExactlyOne<T>(IReadOnlyList<T> values)
    {
        Assert.HasCount(1, values);
        return values[0];
    }
}
