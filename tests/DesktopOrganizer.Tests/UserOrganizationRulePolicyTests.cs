using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopOrganizer.Tests;

[TestClass]
public sealed class UserOrganizationRulePolicyTests
{
    [TestMethod]
    public void FromEditor_NewRuleIsDisabledDraftAndNormalizesExtensions()
    {
        UserOrganizationRuleInfo rule = UserOrganizationRulePolicy.FromEditor(
            new UserRuleEditorData(
                Id: null,
                Name: " 文档规则 ",
                Extensions: "PDF; docx, .PDF",
                NameContains: string.Empty,
                ItemKind: UserRuleItemKindFilter.File,
                CreatedWithinDays: null,
                ModifiedWithinDays: 7,
                RequireTopLevelProjectMarker: false,
                ActionKind: OrganizationRuleActionKind.AddTag,
                ActionTargetName: " 重要 "),
            DateTime.UnixEpoch);

        Assert.AreEqual(UserRuleLifecycle.Draft, rule.Lifecycle);
        Assert.AreEqual("文档规则", rule.Name);
        CollectionAssert.AreEqual(new[] { ".docx", ".pdf" }, rule.Extensions);
        Assert.AreEqual("重要", rule.ActionTargetName);
    }

    [TestMethod]
    public void FromEditor_RejectsConditionlessOrTargetlessRule()
    {
        var conditionless = new UserRuleEditorData(
            null, "空规则", string.Empty, string.Empty, UserRuleItemKindFilter.Any,
            null, null, false, OrganizationRuleActionKind.SendToInbox, string.Empty);
        var targetless = conditionless with
        {
            Extensions = ".txt",
            ActionKind = OrganizationRuleActionKind.AddToVirtualGroup
        };

        Assert.ThrowsExactly<ArgumentException>(() =>
            UserOrganizationRulePolicy.FromEditor(conditionless, DateTime.UnixEpoch));
        Assert.ThrowsExactly<ArgumentException>(() =>
            UserOrganizationRulePolicy.FromEditor(targetless, DateTime.UnixEpoch));
    }

    [TestMethod]
    public void CreateEngineRule_ProducesExplainableDateAndMarkerConditions()
    {
        UserOrganizationRuleInfo persisted = UserOrganizationRulePolicy.FromEditor(
            new UserRuleEditorData(
                null, "项目", string.Empty, string.Empty, UserRuleItemKindFilter.Folder,
                CreatedWithinDays: 30, ModifiedWithinDays: null,
                RequireTopLevelProjectMarker: true,
                OrganizationRuleActionKind.AddToVirtualGroup, "开发"),
            DateTime.UnixEpoch);
        DateTimeOffset now = new(2026, 8, 28, 0, 0, 0, TimeSpan.Zero);

        OrganizationRule rule = UserOrganizationRulePolicy.CreateEngineRule(persisted, now);
        OrganizationRulePreview preview = OrganizationRuleEngine.Preview(
            rule,
            [
                new OrganizationRuleItemContext(
                    "repo", "opaque", string.Empty, true,
                    now.AddDays(-2), now, true, "package.json")
            ]);

        Assert.HasCount(3, rule.Conditions);
        Assert.HasCount(1, preview.ExecutionPlan.Actions);
        Assert.AreEqual("开发", preview.ExecutionPlan.Actions[0].Action.TargetName);
    }

    [TestMethod]
    public void Normalize_CorruptEnabledRuleFailsClosedToDraftAndRepairsIds()
    {
        var layout = new AppLayoutData
        {
            UserRules =
            [
                new UserOrganizationRuleInfo
                {
                    Id = "same",
                    Name = " ",
                    Lifecycle = UserRuleLifecycle.Enabled,
                    Extensions = new List<string>()
                },
                new UserOrganizationRuleInfo
                {
                    Id = "same",
                    Lifecycle = UserRuleLifecycle.Enabled,
                    Extensions = ["TXT"],
                    ActionKind = OrganizationRuleActionKind.AddTag,
                    ActionTargetName = "标签"
                }
            ]
        };

        UserOrganizationRulePolicy.Normalize(layout);

        Assert.AreEqual(2, layout.UserRules.Select(rule => rule.Id).Distinct().Count());
        Assert.AreEqual("未命名规则", layout.UserRules[0].Name);
        Assert.AreEqual(UserRuleLifecycle.Draft, layout.UserRules[0].Lifecycle);
        Assert.AreEqual(UserRuleLifecycle.Enabled, layout.UserRules[1].Lifecycle);
        CollectionAssert.AreEqual(new[] { ".txt" }, layout.UserRules[1].Extensions);
    }

    [TestMethod]
    public void DisableEnabledRules_ChangesOnlyEnabledRulesWithSharedTimestamp()
    {
        DateTime oldUpdated = DateTime.UnixEpoch.AddHours(1);
        DateTime lastTrial = DateTime.UnixEpoch.AddHours(2);
        DateTime disabledUtc = DateTime.UnixEpoch.AddHours(3);
        var draft = new UserOrganizationRuleInfo
        {
            Lifecycle = UserRuleLifecycle.Draft,
            UpdatedUtc = oldUpdated
        };
        var enabledOne = new UserOrganizationRuleInfo
        {
            Lifecycle = UserRuleLifecycle.Enabled,
            UpdatedUtc = oldUpdated,
            LastTrialRunUtc = lastTrial
        };
        var previewed = new UserOrganizationRuleInfo
        {
            Lifecycle = UserRuleLifecycle.Previewed,
            UpdatedUtc = oldUpdated
        };
        var enabledTwo = new UserOrganizationRuleInfo
        {
            Lifecycle = UserRuleLifecycle.Enabled,
            UpdatedUtc = oldUpdated,
            LastTrialRunUtc = lastTrial
        };

        int changedCount = UserOrganizationRulePolicy.DisableEnabledRules(
            [draft, enabledOne, previewed, enabledTwo],
            disabledUtc);

        Assert.AreEqual(2, changedCount);
        Assert.AreEqual(UserRuleLifecycle.Draft, draft.Lifecycle);
        Assert.AreEqual(UserRuleLifecycle.Previewed, previewed.Lifecycle);
        Assert.AreEqual(oldUpdated, draft.UpdatedUtc);
        Assert.AreEqual(oldUpdated, previewed.UpdatedUtc);
        Assert.AreEqual(UserRuleLifecycle.TrialApplied, enabledOne.Lifecycle);
        Assert.AreEqual(UserRuleLifecycle.TrialApplied, enabledTwo.Lifecycle);
        Assert.AreEqual(disabledUtc, enabledOne.UpdatedUtc);
        Assert.AreEqual(disabledUtc, enabledTwo.UpdatedUtc);
        Assert.AreEqual(lastTrial, enabledOne.LastTrialRunUtc);
        Assert.AreEqual(lastTrial, enabledTwo.LastTrialRunUtc);
    }

    [TestMethod]
    public void DisableEnabledRules_NoEnabledRulesHasNoEffect()
    {
        var rule = new UserOrganizationRuleInfo
        {
            Lifecycle = UserRuleLifecycle.TrialApplied,
            UpdatedUtc = DateTime.UnixEpoch,
            LastTrialRunUtc = DateTime.UnixEpoch
        };

        int changedCount = UserOrganizationRulePolicy.DisableEnabledRules(
            [rule],
            DateTime.UnixEpoch.AddDays(1));

        Assert.AreEqual(0, changedCount);
        Assert.AreEqual(UserRuleLifecycle.TrialApplied, rule.Lifecycle);
        Assert.AreEqual(DateTime.UnixEpoch, rule.UpdatedUtc);
        Assert.AreEqual(DateTime.UnixEpoch, rule.LastTrialRunUtc);
    }
}
