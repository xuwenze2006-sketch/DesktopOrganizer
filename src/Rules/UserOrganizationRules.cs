namespace DesktopOrganizer
{
    internal enum UserRuleLifecycle
    {
        Draft,
        Previewed,
        TrialApplied,
        Enabled
    }

    internal enum UserRuleItemKindFilter
    {
        Any,
        File,
        Folder
    }

    internal sealed class UserOrganizationRuleInfo
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "新规则";
        public UserRuleLifecycle Lifecycle { get; set; } = UserRuleLifecycle.Draft;
        public List<string> Extensions { get; set; } = new();
        public string? NameContains { get; set; }
        public UserRuleItemKindFilter ItemKind { get; set; } = UserRuleItemKindFilter.Any;
        public int? CreatedWithinDays { get; set; }
        public int? ModifiedWithinDays { get; set; }
        public bool RequireTopLevelProjectMarker { get; set; }
        public OrganizationRuleActionKind ActionKind { get; set; } = OrganizationRuleActionKind.SendToInbox;
        public string? ActionTargetName { get; set; }
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime? LastPreviewUtc { get; set; }
        public DateTime? LastTrialRunUtc { get; set; }
    }

    internal sealed record UserRuleEditorData(
        string? Id,
        string Name,
        string Extensions,
        string NameContains,
        UserRuleItemKindFilter ItemKind,
        int? CreatedWithinDays,
        int? ModifiedWithinDays,
        bool RequireTopLevelProjectMarker,
        OrganizationRuleActionKind ActionKind,
        string ActionTargetName);

    internal sealed record UserRuleSummary(
        string Id,
        string Name,
        UserRuleLifecycle Lifecycle,
        string LifecycleText,
        string ConditionsText,
        string ActionText,
        DateTime UpdatedUtc);

    internal sealed record RulePreviewItemView(
        string DisplayName,
        bool WillExecute,
        string Target,
        string Explanation,
        string? Conflict);

    internal sealed record UserRulePreviewView(
        string RuleId,
        string RuleName,
        IReadOnlyList<RulePreviewItemView> Items,
        int MatchCount,
        int ConflictCount,
        OrganizationRuleExecutionPlan ExecutionPlan);

    internal sealed class OrganizationDataExport
    {
        public int Version { get; set; } = 1;
        public DateTime ExportedUtc { get; set; } = DateTime.UtcNow;
        public List<UserOrganizationRuleInfo> Rules { get; set; } = new();
        public Dictionary<string, List<string>> Tags { get; set; } = new();
        public Dictionary<string, DesktopItemIdentityInfo> TagIdentities { get; set; } = new();
    }

    internal static class UserOrganizationRulePolicy
    {
        public static void Normalize(AppLayoutData layout)
        {
            ArgumentNullException.ThrowIfNull(layout);
            layout.UserRules ??= new List<UserOrganizationRuleInfo>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            var normalized = new List<UserOrganizationRuleInfo>();
            foreach (UserOrganizationRuleInfo rule in layout.UserRules.OfType<UserOrganizationRuleInfo>())
            {
                rule.Id = rule.Id?.Trim() ?? string.Empty;
                while (string.IsNullOrWhiteSpace(rule.Id) || !ids.Add(rule.Id))
                {
                    rule.Id = Guid.NewGuid().ToString("N");
                }

                string baseName = string.IsNullOrWhiteSpace(rule.Name) ? "未命名规则" : rule.Name.Trim();
                string uniqueName = baseName;
                int suffix = 2;
                while (!names.Add(uniqueName))
                {
                    uniqueName = $"{baseName} ({suffix++})";
                }
                rule.Name = uniqueName;
                rule.Extensions = NormalizeExtensions(rule.Extensions);
                rule.NameContains = string.IsNullOrWhiteSpace(rule.NameContains) ? null : rule.NameContains.Trim();
                if (!Enum.IsDefined(rule.ItemKind)) rule.ItemKind = UserRuleItemKindFilter.Any;
                rule.CreatedWithinDays = NormalizeDays(rule.CreatedWithinDays);
                rule.ModifiedWithinDays = NormalizeDays(rule.ModifiedWithinDays);
                if (!Enum.IsDefined(rule.ActionKind)) rule.ActionKind = OrganizationRuleActionKind.SendToInbox;
                rule.ActionTargetName = string.IsNullOrWhiteSpace(rule.ActionTargetName)
                    ? null
                    : rule.ActionTargetName.Trim();
                if (!Enum.IsDefined(rule.Lifecycle) || !HasConditions(rule) ||
                    (rule.ActionKind != OrganizationRuleActionKind.SendToInbox &&
                     string.IsNullOrWhiteSpace(rule.ActionTargetName)))
                {
                    rule.Lifecycle = UserRuleLifecycle.Draft;
                }
                if (rule.CreatedUtc == default) rule.CreatedUtc = DateTime.UnixEpoch;
                if (rule.UpdatedUtc == default) rule.UpdatedUtc = rule.CreatedUtc;
                normalized.Add(rule);
            }
            layout.UserRules = normalized;
        }

        public static UserOrganizationRuleInfo FromEditor(
            UserRuleEditorData editor,
            DateTime utcNow,
            UserOrganizationRuleInfo? existing = null)
        {
            ArgumentNullException.ThrowIfNull(editor);
            var rule = new UserOrganizationRuleInfo
            {
                Id = existing?.Id ?? Guid.NewGuid().ToString("N"),
                Name = editor.Name?.Trim() ?? string.Empty,
                Lifecycle = UserRuleLifecycle.Draft,
                Extensions = ParseExtensions(editor.Extensions),
                NameContains = string.IsNullOrWhiteSpace(editor.NameContains) ? null : editor.NameContains.Trim(),
                ItemKind = editor.ItemKind,
                CreatedWithinDays = NormalizeDays(editor.CreatedWithinDays),
                ModifiedWithinDays = NormalizeDays(editor.ModifiedWithinDays),
                RequireTopLevelProjectMarker = editor.RequireTopLevelProjectMarker,
                ActionKind = editor.ActionKind,
                ActionTargetName = string.IsNullOrWhiteSpace(editor.ActionTargetName)
                    ? null
                    : editor.ActionTargetName.Trim(),
                CreatedUtc = existing?.CreatedUtc ?? utcNow,
                UpdatedUtc = utcNow
            };
            Validate(rule);
            return rule;
        }

        public static OrganizationRule CreateEngineRule(
            UserOrganizationRuleInfo persisted,
            DateTimeOffset utcNow)
        {
            ArgumentNullException.ThrowIfNull(persisted);
            Validate(persisted);
            var conditions = new List<OrganizationRuleCondition>();
            if (persisted.Extensions.Count > 0)
                conditions.Add(OrganizationRuleCondition.ExtensionIs(persisted.Extensions.ToArray()));
            if (!string.IsNullOrWhiteSpace(persisted.NameContains))
                conditions.Add(OrganizationRuleCondition.NameIncludes(persisted.NameContains));
            if (persisted.ItemKind != UserRuleItemKindFilter.Any)
                conditions.Add(OrganizationRuleCondition.Is(
                    persisted.ItemKind == UserRuleItemKindFilter.Folder
                        ? OrganizationRuleItemKind.Folder
                        : OrganizationRuleItemKind.File));
            if (persisted.CreatedWithinDays.HasValue)
                conditions.Add(OrganizationRuleCondition.CreatedBetween(
                    utcNow.AddDays(-persisted.CreatedWithinDays.Value), utcNow));
            if (persisted.ModifiedWithinDays.HasValue)
                conditions.Add(OrganizationRuleCondition.ModifiedBetween(
                    utcNow.AddDays(-persisted.ModifiedWithinDays.Value), utcNow));
            if (persisted.RequireTopLevelProjectMarker)
                conditions.Add(OrganizationRuleCondition.HasTopLevelProjectMarker());

            OrganizationRuleAction action = persisted.ActionKind switch
            {
                OrganizationRuleActionKind.AddToVirtualGroup => OrganizationRuleAction.AddToVirtualGroup(
                    "rule-" + persisted.Id, persisted.ActionTargetName!),
                OrganizationRuleActionKind.AddTag => OrganizationRuleAction.AddTag(persisted.ActionTargetName!),
                OrganizationRuleActionKind.SendToInbox => OrganizationRuleAction.SendToInbox(),
                _ => throw new InvalidOperationException("未知的规则动作。")
            };
            return OrganizationRule.Create(persisted.Name, conditions, action, persisted.Id);
        }

        public static List<string> ParseExtensions(string? text) => NormalizeExtensions(
            (text ?? string.Empty).Split(
                [',', ';', '，', '；', ' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        public static string DescribeConditions(UserOrganizationRuleInfo rule)
        {
            var parts = new List<string>();
            if (rule.Extensions.Count > 0) parts.Add($"扩展名 {string.Join("/", rule.Extensions)}");
            if (!string.IsNullOrWhiteSpace(rule.NameContains)) parts.Add($"名称含“{rule.NameContains}”");
            if (rule.ItemKind != UserRuleItemKindFilter.Any)
                parts.Add(rule.ItemKind == UserRuleItemKindFilter.Folder ? "文件夹" : "文件");
            if (rule.CreatedWithinDays.HasValue) parts.Add($"{rule.CreatedWithinDays} 天内创建");
            if (rule.ModifiedWithinDays.HasValue) parts.Add($"{rule.ModifiedWithinDays} 天内修改");
            if (rule.RequireTopLevelProjectMarker) parts.Add("有顶层项目标记");
            return string.Join(" 且 ", parts);
        }

        public static string DescribeAction(UserOrganizationRuleInfo rule) => rule.ActionKind switch
        {
            OrganizationRuleActionKind.AddToVirtualGroup => $"加入虚拟分组“{rule.ActionTargetName}”",
            OrganizationRuleActionKind.AddTag => $"添加标签“{rule.ActionTargetName}”",
            OrganizationRuleActionKind.SendToInbox => "进入待整理收件箱",
            _ => "未知动作"
        };

        public static string DescribeLifecycle(UserRuleLifecycle lifecycle) => lifecycle switch
        {
            UserRuleLifecycle.Draft => "草稿（禁用）",
            UserRuleLifecycle.Previewed => "已预览",
            UserRuleLifecycle.TrialApplied => "已执行一次",
            UserRuleLifecycle.Enabled => "自动应用已启用",
            _ => "草稿（禁用）"
        };

        public static int DisableEnabledRules(
            IEnumerable<UserOrganizationRuleInfo> rules,
            DateTime utcNow)
        {
            ArgumentNullException.ThrowIfNull(rules);

            int changedCount = 0;
            foreach (UserOrganizationRuleInfo rule in rules)
            {
                if (rule.Lifecycle != UserRuleLifecycle.Enabled)
                {
                    continue;
                }

                rule.Lifecycle = UserRuleLifecycle.TrialApplied;
                rule.UpdatedUtc = utcNow;
                changedCount++;
            }
            return changedCount;
        }

        public static UserOrganizationRuleInfo Clone(UserOrganizationRuleInfo rule) => new()
        {
            Id = rule.Id,
            Name = rule.Name,
            Lifecycle = rule.Lifecycle,
            Extensions = rule.Extensions.ToList(),
            NameContains = rule.NameContains,
            ItemKind = rule.ItemKind,
            CreatedWithinDays = rule.CreatedWithinDays,
            ModifiedWithinDays = rule.ModifiedWithinDays,
            RequireTopLevelProjectMarker = rule.RequireTopLevelProjectMarker,
            ActionKind = rule.ActionKind,
            ActionTargetName = rule.ActionTargetName,
            CreatedUtc = rule.CreatedUtc,
            UpdatedUtc = rule.UpdatedUtc,
            LastPreviewUtc = rule.LastPreviewUtc,
            LastTrialRunUtc = rule.LastTrialRunUtc
        };

        private static void Validate(UserOrganizationRuleInfo rule)
        {
            if (string.IsNullOrWhiteSpace(rule.Name))
                throw new ArgumentException("规则名称不能为空。", nameof(rule));
            if (!HasConditions(rule))
                throw new ArgumentException("至少设置一个规则条件，避免意外匹配整个桌面。", nameof(rule));
            if (!Enum.IsDefined(rule.ItemKind) || !Enum.IsDefined(rule.ActionKind))
                throw new ArgumentException("规则包含未知选项。", nameof(rule));
            if (rule.ActionKind != OrganizationRuleActionKind.SendToInbox &&
                string.IsNullOrWhiteSpace(rule.ActionTargetName))
                throw new ArgumentException("虚拟分组或标签动作必须填写目标名称。", nameof(rule));
        }

        private static bool HasConditions(UserOrganizationRuleInfo rule) =>
            rule.Extensions.Count > 0 ||
            !string.IsNullOrWhiteSpace(rule.NameContains) ||
            rule.ItemKind != UserRuleItemKindFilter.Any ||
            rule.CreatedWithinDays.HasValue ||
            rule.ModifiedWithinDays.HasValue ||
            rule.RequireTopLevelProjectMarker;

        private static int? NormalizeDays(int? value) => value is >= 1 and <= 3650 ? value : null;

        private static List<string> NormalizeExtensions(IEnumerable<string>? extensions) =>
            (extensions ?? Array.Empty<string>())
                .Where(extension => !string.IsNullOrWhiteSpace(extension))
                .Select(extension => extension.Trim())
                .Select(extension => extension.StartsWith(".", StringComparison.Ordinal)
                    ? extension.ToLowerInvariant()
                    : "." + extension.ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase)
                .ToList();
    }
}
