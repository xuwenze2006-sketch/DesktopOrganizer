namespace DesktopOrganizer
{
    internal enum OrganizationRuleState
    {
        Draft,
        Previewed,
        Enabled
    }

    internal enum OrganizationRuleConditionKind
    {
        Extension,
        NameContains,
        ItemKind,
        CreationTimeWindow,
        ModifiedTimeWindow,
        TopLevelProjectMarker
    }

    internal enum OrganizationRuleItemKind
    {
        File,
        Folder
    }

    internal enum OrganizationRuleActionKind
    {
        AddToVirtualGroup,
        AddTag,
        SendToInbox
    }

    internal enum OrganizationRuleConditionOutcome
    {
        Matched,
        NotMatched,
        Unknown
    }

    internal enum OrganizationRuleConflictKind
    {
        ManualGroupProtected
    }

    /// <summary>单条规则条件。请使用静态工厂创建，避免产生字段组合不完整的条件。</summary>
    internal sealed record OrganizationRuleCondition
    {
        private OrganizationRuleCondition(OrganizationRuleConditionKind kind)
        {
            Kind = kind;
        }

        public OrganizationRuleConditionKind Kind { get; }
        public IReadOnlyList<string> Extensions { get; private init; } = Array.Empty<string>();
        public string? Text { get; private init; }
        public OrganizationRuleItemKind? ItemKind { get; private init; }
        public DateTimeOffset? WindowStartUtc { get; private init; }
        public DateTimeOffset? WindowEndUtc { get; private init; }

        public static OrganizationRuleCondition ExtensionIs(params string[] extensions)
        {
            ArgumentNullException.ThrowIfNull(extensions);
            string[] normalized = extensions
                .Where(extension => !string.IsNullOrWhiteSpace(extension))
                .Select(NormalizeExtension)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase)
                .ThenBy(extension => extension, StringComparer.Ordinal)
                .ToArray();
            if (normalized.Length == 0)
            {
                throw new ArgumentException("扩展名条件至少需要一个扩展名。", nameof(extensions));
            }

            return new OrganizationRuleCondition(OrganizationRuleConditionKind.Extension)
            {
                Extensions = Array.AsReadOnly(normalized)
            };
        }

        public static OrganizationRuleCondition NameIncludes(string text)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(text);
            return new OrganizationRuleCondition(OrganizationRuleConditionKind.NameContains)
            {
                Text = text.Trim()
            };
        }

        public static OrganizationRuleCondition Is(OrganizationRuleItemKind itemKind)
        {
            if (!Enum.IsDefined(itemKind))
            {
                throw new ArgumentOutOfRangeException(nameof(itemKind));
            }

            return new OrganizationRuleCondition(OrganizationRuleConditionKind.ItemKind)
            {
                ItemKind = itemKind
            };
        }

        public static OrganizationRuleCondition CreatedBetween(
            DateTimeOffset? startUtc,
            DateTimeOffset? endUtc) =>
            CreateDateWindow(OrganizationRuleConditionKind.CreationTimeWindow, startUtc, endUtc);

        public static OrganizationRuleCondition ModifiedBetween(
            DateTimeOffset? startUtc,
            DateTimeOffset? endUtc) =>
            CreateDateWindow(OrganizationRuleConditionKind.ModifiedTimeWindow, startUtc, endUtc);

        public static OrganizationRuleCondition HasTopLevelProjectMarker() =>
            new(OrganizationRuleConditionKind.TopLevelProjectMarker);

        private static OrganizationRuleCondition CreateDateWindow(
            OrganizationRuleConditionKind kind,
            DateTimeOffset? startUtc,
            DateTimeOffset? endUtc)
        {
            if (!startUtc.HasValue && !endUtc.HasValue)
            {
                throw new ArgumentException("日期窗口至少需要起点或终点。");
            }

            DateTimeOffset? normalizedStart = startUtc?.ToUniversalTime();
            DateTimeOffset? normalizedEnd = endUtc?.ToUniversalTime();
            if (normalizedStart.HasValue && normalizedEnd.HasValue &&
                normalizedStart.Value > normalizedEnd.Value)
            {
                throw new ArgumentException("日期窗口起点不能晚于终点。");
            }

            return new OrganizationRuleCondition(kind)
            {
                WindowStartUtc = normalizedStart,
                WindowEndUtc = normalizedEnd
            };
        }

        private static string NormalizeExtension(string extension)
        {
            string normalized = extension.Trim();
            return normalized.StartsWith(".", StringComparison.Ordinal)
                ? normalized
                : "." + normalized;
        }
    }

    /// <summary>规则动作。虚拟分组和标签目标在创建时即固定，预览不会创建它们。</summary>
    internal sealed record OrganizationRuleAction
    {
        private OrganizationRuleAction(
            OrganizationRuleActionKind kind,
            string? targetId,
            string targetName)
        {
            Kind = kind;
            TargetId = targetId;
            TargetName = targetName;
        }

        public OrganizationRuleActionKind Kind { get; }
        public string? TargetId { get; }
        public string TargetName { get; }

        public static OrganizationRuleAction AddToVirtualGroup(string groupId, string groupName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
            ArgumentException.ThrowIfNullOrWhiteSpace(groupName);
            return new OrganizationRuleAction(
                OrganizationRuleActionKind.AddToVirtualGroup,
                groupId.Trim(),
                groupName.Trim());
        }

        public static OrganizationRuleAction AddTag(string tag)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tag);
            return new OrganizationRuleAction(
                OrganizationRuleActionKind.AddTag,
                targetId: null,
                tag.Trim());
        }

        public static OrganizationRuleAction SendToInbox() =>
            new(
                OrganizationRuleActionKind.SendToInbox,
                targetId: null,
                "待整理收件箱");
    }

    /// <summary>
    /// 不可变规则模型。外部只能创建 Draft；Preview 返回新的 Previewed 副本，
    /// 只有 Previewed 副本可以启用，因此无法跳过预览直接进入 Enabled。
    /// </summary>
    internal sealed record OrganizationRule
    {
        private OrganizationRule(
            string id,
            string name,
            IReadOnlyList<OrganizationRuleCondition> conditions,
            OrganizationRuleAction action,
            OrganizationRuleState state)
        {
            Id = id;
            Name = name;
            Conditions = Array.AsReadOnly(conditions.ToArray());
            Action = action;
            State = state;
        }

        public string Id { get; }
        public string Name { get; }
        public IReadOnlyList<OrganizationRuleCondition> Conditions { get; }
        public OrganizationRuleAction Action { get; }
        public OrganizationRuleState State { get; }

        public static OrganizationRule Create(
            string name,
            IEnumerable<OrganizationRuleCondition> conditions,
            OrganizationRuleAction action,
            string? id = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(conditions);
            ArgumentNullException.ThrowIfNull(action);

            OrganizationRuleCondition[] copiedConditions = conditions
                .Select(condition => condition ?? throw new ArgumentException(
                    "规则条件不能为 null。",
                    nameof(conditions)))
                .ToArray();
            return new OrganizationRule(
                string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim(),
                name.Trim(),
                copiedConditions,
                action,
                OrganizationRuleState.Draft);
        }

        public OrganizationRule Enable()
        {
            if (State != OrganizationRuleState.Previewed)
            {
                throw new InvalidOperationException("规则必须先完成预览，才能启用自动应用。");
            }

            return CopyWithState(OrganizationRuleState.Enabled);
        }

        internal OrganizationRule MarkPreviewed()
        {
            if (State != OrganizationRuleState.Draft)
            {
                throw new InvalidOperationException("只有草稿规则可以进入预览状态。");
            }

            return CopyWithState(OrganizationRuleState.Previewed);
        }

        private OrganizationRule CopyWithState(OrganizationRuleState state) =>
            new(Id, Name, Conditions, Action, state);
    }

    /// <summary>调用方完成桌面扫描后提供的规则事实；Location 仅作为不透明标识返回。</summary>
    internal sealed record OrganizationRuleItemContext(
        string DisplayName,
        string Location,
        string Extension,
        bool IsDirectory,
        DateTimeOffset? CreationTimeUtc,
        DateTimeOffset? ModifiedTimeUtc,
        bool? HasTopLevelProjectMarker,
        string? MatchedTopLevelProjectMarker,
        string? ManualGroupId = null,
        string? ManualGroupName = null);

    internal sealed record OrganizationRuleConditionEvaluation(
        OrganizationRuleConditionKind ConditionKind,
        OrganizationRuleConditionOutcome Outcome,
        string Reason);

    internal sealed record OrganizationRuleConflict(
        OrganizationRuleConflictKind Kind,
        string Message,
        string? ManualGroupId,
        string? ManualGroupName);

    internal sealed record OrganizationRulePreviewItem(
        string DisplayName,
        string Location,
        bool IsMatch,
        IReadOnlyList<OrganizationRuleConditionEvaluation> Conditions,
        OrganizationRuleAction Target,
        string TargetDescription,
        OrganizationRuleConflict? Conflict,
        bool WillExecute);

    internal sealed record OrganizationRulePlannedAction(
        string DisplayName,
        string Location,
        OrganizationRuleAction Action,
        string ReasonSummary);

    internal sealed record OrganizationRuleExecutionPlan(
        string RuleId,
        IReadOnlyList<OrganizationRulePlannedAction> Actions);

    internal sealed record OrganizationRulePreview(
        OrganizationRule PreviewedRule,
        IReadOnlyList<OrganizationRulePreviewItem> Items,
        OrganizationRuleExecutionPlan ExecutionPlan);

    /// <summary>
    /// 纯规则评估器。它只读取传入模型，逐项评估全部条件并生成解释；
    /// 不创建分组、不修改标签或收件箱，也不调用任何文件系统 API。
    /// </summary>
    internal static class OrganizationRuleEngine
    {
        public static OrganizationRulePreview Preview(
            OrganizationRule rule,
            IEnumerable<OrganizationRuleItemContext> items)
        {
            ArgumentNullException.ThrowIfNull(rule);
            ArgumentNullException.ThrowIfNull(items);
            if (rule.State != OrganizationRuleState.Draft)
            {
                throw new InvalidOperationException("只有草稿规则可以预览。");
            }
            if (rule.Conditions.Count == 0)
            {
                throw new InvalidOperationException("规则至少需要一个条件，防止意外匹配整个桌面。");
            }

            OrganizationRule previewedRule = rule.MarkPreviewed();
            var previewItems = new List<OrganizationRulePreviewItem>();
            var plannedActions = new List<OrganizationRulePlannedAction>();

            foreach (OrganizationRuleItemContext item in items
                         .Select(item => item ?? throw new ArgumentException(
                             "项目上下文不能为 null。",
                             nameof(items)))
                         .OrderBy(item => item.DisplayName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.DisplayName ?? string.Empty, StringComparer.Ordinal)
                         .ThenBy(item => item.Location ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Location ?? string.Empty, StringComparer.Ordinal))
            {
                IReadOnlyList<OrganizationRuleConditionEvaluation> evaluations = Array.AsReadOnly(
                    rule.Conditions
                        .Select(condition => EvaluateCondition(condition, item))
                        .ToArray());
                bool isMatch = evaluations.All(evaluation =>
                    evaluation.Outcome == OrganizationRuleConditionOutcome.Matched);
                OrganizationRuleConflict? conflict = isMatch
                    ? DetectConflict(rule.Action, item)
                    : null;
                bool willExecute = isMatch && conflict == null;
                string targetDescription = DescribeTarget(rule.Action);

                previewItems.Add(new OrganizationRulePreviewItem(
                    item.DisplayName ?? string.Empty,
                    item.Location ?? string.Empty,
                    isMatch,
                    evaluations,
                    rule.Action,
                    targetDescription,
                    conflict,
                    willExecute));

                if (willExecute)
                {
                    plannedActions.Add(new OrganizationRulePlannedAction(
                        item.DisplayName ?? string.Empty,
                        item.Location ?? string.Empty,
                        rule.Action,
                        string.Join("；", evaluations.Select(evaluation => evaluation.Reason))));
                }
            }

            return new OrganizationRulePreview(
                previewedRule,
                previewItems.AsReadOnly(),
                new OrganizationRuleExecutionPlan(rule.Id, plannedActions.AsReadOnly()));
        }

        private static OrganizationRuleConditionEvaluation EvaluateCondition(
            OrganizationRuleCondition condition,
            OrganizationRuleItemContext item)
        {
            return condition.Kind switch
            {
                OrganizationRuleConditionKind.Extension => EvaluateExtension(condition, item),
                OrganizationRuleConditionKind.NameContains => EvaluateName(condition, item),
                OrganizationRuleConditionKind.ItemKind => EvaluateItemKind(condition, item),
                OrganizationRuleConditionKind.CreationTimeWindow => EvaluateDateWindow(
                    condition,
                    item.CreationTimeUtc,
                    "创建时间"),
                OrganizationRuleConditionKind.ModifiedTimeWindow => EvaluateDateWindow(
                    condition,
                    item.ModifiedTimeUtc,
                    "修改时间"),
                OrganizationRuleConditionKind.TopLevelProjectMarker => EvaluateProjectMarker(item),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(condition),
                    condition.Kind,
                    "未知的规则条件。")
            };
        }

        private static OrganizationRuleConditionEvaluation EvaluateExtension(
            OrganizationRuleCondition condition,
            OrganizationRuleItemContext item)
        {
            string actual = NormalizeExtension(item.Extension);
            bool matched = condition.Extensions.Contains(actual, StringComparer.OrdinalIgnoreCase);
            string expected = string.Join("、", condition.Extensions);
            return Evaluation(
                OrganizationRuleConditionKind.Extension,
                matched,
                matched
                    ? $"扩展名“{actual}”命中“{expected}”"
                    : $"扩展名“{actual}”未命中“{expected}”");
        }

        private static OrganizationRuleConditionEvaluation EvaluateName(
            OrganizationRuleCondition condition,
            OrganizationRuleItemContext item)
        {
            string expected = condition.Text ?? string.Empty;
            bool matched = (item.DisplayName ?? string.Empty).Contains(
                expected,
                StringComparison.OrdinalIgnoreCase);
            return Evaluation(
                OrganizationRuleConditionKind.NameContains,
                matched,
                matched
                    ? $"名称包含“{expected}”"
                    : $"名称不包含“{expected}”");
        }

        private static OrganizationRuleConditionEvaluation EvaluateItemKind(
            OrganizationRuleCondition condition,
            OrganizationRuleItemContext item)
        {
            OrganizationRuleItemKind actual = item.IsDirectory
                ? OrganizationRuleItemKind.Folder
                : OrganizationRuleItemKind.File;
            OrganizationRuleItemKind expected = condition.ItemKind
                ?? throw new InvalidOperationException("项目类型条件缺少目标类型。");
            bool matched = actual == expected;
            string actualText = actual == OrganizationRuleItemKind.Folder ? "文件夹" : "文件";
            string expectedText = expected == OrganizationRuleItemKind.Folder ? "文件夹" : "文件";
            return Evaluation(
                OrganizationRuleConditionKind.ItemKind,
                matched,
                matched
                    ? $"项目是{expectedText}"
                    : $"项目是{actualText}，不是{expectedText}");
        }

        private static OrganizationRuleConditionEvaluation EvaluateDateWindow(
            OrganizationRuleCondition condition,
            DateTimeOffset? timestamp,
            string label)
        {
            if (!timestamp.HasValue)
            {
                return new OrganizationRuleConditionEvaluation(
                    condition.Kind,
                    OrganizationRuleConditionOutcome.Unknown,
                    $"{label}不可用，未据此执行规则");
            }

            DateTimeOffset actual = timestamp.Value.ToUniversalTime();
            bool afterStart = !condition.WindowStartUtc.HasValue ||
                actual >= condition.WindowStartUtc.Value;
            bool beforeEnd = !condition.WindowEndUtc.HasValue ||
                actual <= condition.WindowEndUtc.Value;
            bool matched = afterStart && beforeEnd;
            string window = DescribeWindow(condition.WindowStartUtc, condition.WindowEndUtc);
            return Evaluation(
                condition.Kind,
                matched,
                matched
                    ? $"{label}“{FormatUtc(actual)}”位于{window}"
                    : $"{label}“{FormatUtc(actual)}”不在{window}");
        }

        private static OrganizationRuleConditionEvaluation EvaluateProjectMarker(
            OrganizationRuleItemContext item)
        {
            if (!item.HasTopLevelProjectMarker.HasValue)
            {
                return new OrganizationRuleConditionEvaluation(
                    OrganizationRuleConditionKind.TopLevelProjectMarker,
                    OrganizationRuleConditionOutcome.Unknown,
                    "顶层项目标记信息不可用，未据此执行规则");
            }

            if (item.HasTopLevelProjectMarker.Value)
            {
                string marker = string.IsNullOrWhiteSpace(item.MatchedTopLevelProjectMarker)
                    ? "已识别标记"
                    : item.MatchedTopLevelProjectMarker.Trim();
                return Evaluation(
                    OrganizationRuleConditionKind.TopLevelProjectMarker,
                    matched: true,
                    $"顶层项目标记“{marker}”已命中");
            }

            return Evaluation(
                OrganizationRuleConditionKind.TopLevelProjectMarker,
                matched: false,
                "未发现顶层项目标记");
        }

        private static OrganizationRuleConditionEvaluation Evaluation(
            OrganizationRuleConditionKind kind,
            bool matched,
            string reason) =>
            new(
                kind,
                matched
                    ? OrganizationRuleConditionOutcome.Matched
                    : OrganizationRuleConditionOutcome.NotMatched,
                reason);

        private static OrganizationRuleConflict? DetectConflict(
            OrganizationRuleAction action,
            OrganizationRuleItemContext item)
        {
            bool changesPlacement = action.Kind is
                OrganizationRuleActionKind.AddToVirtualGroup or
                OrganizationRuleActionKind.SendToInbox;
            if (!changesPlacement ||
                (string.IsNullOrWhiteSpace(item.ManualGroupId) &&
                 string.IsNullOrWhiteSpace(item.ManualGroupName)))
            {
                return null;
            }

            string groupName = string.IsNullOrWhiteSpace(item.ManualGroupName)
                ? "未命名手工分组"
                : item.ManualGroupName.Trim();
            return new OrganizationRuleConflict(
                OrganizationRuleConflictKind.ManualGroupProtected,
                $"项目已在手工分组“{groupName}”中，自动规则不会改写其归属",
                item.ManualGroupId,
                item.ManualGroupName);
        }

        private static string DescribeTarget(OrganizationRuleAction action) => action.Kind switch
        {
            OrganizationRuleActionKind.AddToVirtualGroup => $"虚拟分组“{action.TargetName}”",
            OrganizationRuleActionKind.AddTag => $"标签“{action.TargetName}”",
            OrganizationRuleActionKind.SendToInbox => "待整理收件箱",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action.Kind, "未知的规则动作。")
        };

        private static string DescribeWindow(
            DateTimeOffset? startUtc,
            DateTimeOffset? endUtc)
        {
            if (startUtc.HasValue && endUtc.HasValue)
            {
                return $"“{FormatUtc(startUtc.Value)}”至“{FormatUtc(endUtc.Value)}”";
            }

            return startUtc.HasValue
                ? $"“{FormatUtc(startUtc.Value)}”之后"
                : $"“{FormatUtc(endUtc!.Value)}”之前";
        }

        private static string FormatUtc(DateTimeOffset value) =>
            value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'");

        private static string NormalizeExtension(string? extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                return string.Empty;
            }

            string normalized = extension.Trim();
            return normalized.StartsWith(".", StringComparison.Ordinal)
                ? normalized
                : "." + normalized;
        }
    }
}
