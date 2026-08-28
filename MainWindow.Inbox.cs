// 待整理收件箱：所有动作仅修改本地虚拟布局
namespace DesktopOrganizer
{
    internal sealed record InboxListItemView(
        string DisplayName,
        string SuggestedCategory,
        string MatchReason,
        string Reliability,
        string ReviewState,
        bool CanAccept);

    internal sealed record ManualGroupChoice(string Id, string Name);

    public partial class MainWindow
    {
        private void InboxButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InboxWindow(this);
            if (_isAttachedToDesktop)
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            else
            {
                dialog.Owner = this;
            }
            dialog.ShowDialog();
        }

        internal IReadOnlyList<InboxListItemView> GetInboxItems() =>
            _appLayout.InboxItems
                .OrderBy(pair => pair.Value.ReviewState)
                .ThenBy(pair => pair.Value.DetectedUtc)
                .ThenBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(pair => new InboxListItemView(
                    pair.Key,
                    string.IsNullOrWhiteSpace(pair.Value.SuggestedCategoryName)
                        ? "待确认"
                        : pair.Value.SuggestedCategoryName,
                    pair.Value.MatchReason,
                    pair.Value.Reliability == ClassificationReliability.Reliable
                        ? "可靠"
                        : "待确认",
                    pair.Value.ReviewState == InboxReviewState.Deferred
                        ? "以后处理"
                        : "待处理",
                    pair.Value.Reliability == ClassificationReliability.Reliable))
                .ToList();

        internal IReadOnlyList<ManualGroupChoice> GetManualInboxGroups() =>
            _appLayout.Groups
                .Where(group => !group.IsAutoCategory && string.IsNullOrWhiteSpace(group.UserRuleId))
                .OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(group => new ManualGroupChoice(group.Id, group.Name))
                .ToList();

        internal bool TryAcceptInboxSuggestion(string displayName, out string message)
        {
            InboxActionResult result = InboxQueueManager.TryCreateAcceptancePlan(
                _appLayout.InboxItems,
                _appLayout.ItemIdentities,
                _appLayout.Groups,
                displayName,
                out InboxAcceptancePlan? plan);
            if (result.Outcome != InboxActionOutcome.Planned || plan == null)
            {
                message = FormatInboxActionError(result.Outcome);
                return false;
            }

            if (!ApplyInboxAcceptancePlan(plan))
            {
                message = "项目身份已经变化，未修改收件箱或布局。";
                return false;
            }

            RebuildDesktopIconsAndSaveLayout();
            UpdateInboxButton();
            message = $"已按建议把“{plan.DisplayName}”加入虚拟分类“{plan.TargetCategory.DisplayName}”；真实文件未移动。";
            StatusText.Text = message;
            return true;
        }

        internal bool TryLeaveInboxItemOnDesktop(string displayName, out string message)
        {
            InboxActionResult result = InboxQueueManager.LeaveOnDesktop(
                _appLayout.InboxItems,
                _appLayout.ItemIdentities,
                displayName);
            if (!result.Succeeded)
            {
                message = FormatInboxActionError(result.Outcome);
                return false;
            }
            if (result.Changed)
            {
                SaveLayout();
            }
            UpdateInboxButton();
            message = $"“{displayName}”保留在桌面原位，不再显示为待整理。";
            StatusText.Text = message;
            return true;
        }

        internal bool TryDeferInboxItem(string displayName, out string message)
        {
            InboxActionResult result = InboxQueueManager.Defer(
                _appLayout.InboxItems,
                _appLayout.ItemIdentities,
                displayName,
                DateTime.UtcNow);
            if (!result.Succeeded)
            {
                message = FormatInboxActionError(result.Outcome);
                return false;
            }
            if (result.Changed)
            {
                SaveLayout();
            }
            UpdateInboxButton();
            message = $"已把“{displayName}”标记为以后处理；布局和真实文件保持不变。";
            StatusText.Text = message;
            return true;
        }

        internal bool TryMoveInboxItemToManualGroup(
            string displayName,
            string targetGroupId,
            out string message)
        {
            InboxActionResult result = InboxQueueManager.TryMoveToManualGroup(
                _appLayout.InboxItems,
                _appLayout.ItemIdentities,
                _appLayout.Groups,
                _appLayout.FreeIcons,
                _appLayout.AutoClassificationOriginalPositions,
                displayName,
                targetGroupId);
            if (!result.Succeeded)
            {
                message = FormatInboxActionError(result.Outcome);
                return false;
            }

            _appLayout.ItemLastMovedUtcTicks[displayName] = DateTime.UtcNow.Ticks;
            foreach (GroupInfo group in _appLayout.Groups)
            {
                if (!group.IsSizeLocked)
                {
                    _ = AutoFitGroup(group, clampPosition: true);
                }
            }
            RebuildDesktopIconsAndSaveLayout();
            UpdateInboxButton();
            GroupInfo? target = _appLayout.Groups.FirstOrDefault(group =>
                group.Id.Equals(targetGroupId, StringComparison.OrdinalIgnoreCase));
            message = $"已把“{displayName}”加入手工分组“{target?.Name ?? "未命名分组"}”；真实文件未移动。";
            StatusText.Text = message;
            return true;
        }

        private static IReadOnlyDictionary<string, InboxClassificationSuggestion> BuildInboxSuggestions(
            DesktopScanSnapshot snapshot)
        {
            var suggestions = new Dictionary<string, InboxClassificationSuggestion>(
                StringComparer.OrdinalIgnoreCase);
            foreach ((string name, DesktopCategoryDefinition category) in snapshot.Categories)
            {
                snapshot.Identities.TryGetValue(name, out DesktopItemIdentityInfo? identity);
                bool reliable = snapshot.ReliableCategoryNames.Contains(name);
                string reason = !reliable
                    ? "路径或目录标记暂时无法确认，保持原位"
                    : identity?.Kind == DesktopItemKind.ShellNamespace
                        ? "Windows Shell 系统项目"
                        : identity?.IsDirectory == true
                            ? category.Key.Equals("development-projects", StringComparison.OrdinalIgnoreCase)
                                ? "顶层项目标记命中"
                                : "文件夹"
                            : BuildExtensionReason(identity?.LastKnownPath);
                suggestions[name] = new InboxClassificationSuggestion(
                    category,
                    reliable ? ClassificationReliability.Reliable : ClassificationReliability.Conservative,
                    reason);
            }
            return suggestions;
        }

        private static string BuildExtensionReason(string? path)
        {
            string extension = string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetExtension(path);
            return string.IsNullOrWhiteSpace(extension)
                ? "本地类型规则命中"
                : $"扩展名 {extension.ToLowerInvariant()}";
        }

        private bool ReconcileDesktopOrganizationMetadata(
            DesktopScanSnapshot snapshot,
            IReadOnlySet<string> newItemNames,
            bool baselineWasEstablished,
            DateTime utcNow)
        {
            bool changed = false;
            if (snapshot.PhysicalScanComplete && snapshot.ShellScanComplete)
            {
                changed |= RemoveMissingMetadata(_appLayout.ItemTags, snapshot.Items.Keys);
                changed |= RemoveMissingMetadata(_appLayout.ItemFirstSeenUtcTicks, snapshot.Items.Keys);
                changed |= RemoveMissingMetadata(_appLayout.ItemLastMovedUtcTicks, snapshot.Items.Keys);
            }

            if (!baselineWasEstablished)
            {
                return changed;
            }

            foreach (string name in newItemNames)
            {
                // 同名替换必须丢弃旧项目的组织元数据，避免标签串到新文件。
                changed |= _appLayout.ItemTags.Remove(name);
                changed |= _appLayout.ItemLastMovedUtcTicks.Remove(name);
                long ticks = utcNow.Ticks;
                if (!_appLayout.ItemFirstSeenUtcTicks.TryGetValue(name, out long previous) || previous != ticks)
                {
                    _appLayout.ItemFirstSeenUtcTicks[name] = ticks;
                    changed = true;
                }
            }
            return changed;
        }

        private static bool RemoveMissingMetadata<T>(
            Dictionary<string, T> metadata,
            IEnumerable<string> currentNames)
        {
            var current = new HashSet<string>(currentNames, StringComparer.OrdinalIgnoreCase);
            int before = metadata.Count;
            foreach (string name in metadata.Keys.Where(name => !current.Contains(name)).ToList())
            {
                metadata.Remove(name);
            }
            return before != metadata.Count;
        }

        private bool ApplyAutomaticInboxAcceptances()
        {
            IReadOnlyList<InboxAcceptancePlan> plans = InboxQueueManager.PlanAutomaticAcceptances(
                _appLayout.InboxItems,
                _appLayout.ItemIdentities,
                _appLayout.Groups,
                paused: !IsAutoClassificationActive);
            bool changed = false;
            foreach (InboxAcceptancePlan plan in plans)
            {
                changed |= ApplyInboxAcceptancePlan(plan);
            }
            return changed;
        }

        private bool ApplyInboxAcceptancePlan(InboxAcceptancePlan plan)
        {
            InboxActionResult completion = InboxQueueManager.CompleteAcceptance(
                _appLayout.InboxItems,
                _appLayout.ItemIdentities,
                plan);
            if (completion.Outcome != InboxActionOutcome.Applied)
            {
                return false;
            }

            if (_appLayout.FreeIcons.TryGetValue(plan.DisplayName, out IconPosition? position) &&
                position != null &&
                !_appLayout.AutoClassificationOriginalPositions.ContainsKey(plan.DisplayName))
            {
                _appLayout.AutoClassificationOriginalPositions[plan.DisplayName] =
                    ClonePosition(position);
            }

            foreach (GroupInfo candidate in _appLayout.Groups.Where(group => group.IsAutoCategory))
            {
                candidate.ItemNames.RemoveAll(name =>
                    name.Equals(plan.DisplayName, StringComparison.OrdinalIgnoreCase));
            }

            GroupInfo? target = _appLayout.Groups.LastOrDefault(candidate =>
                candidate.IsAutoCategory &&
                string.Equals(
                    candidate.AutoCategoryKey,
                    plan.TargetCategory.Key,
                    StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                target = CreateAutoCategoryGroup(plan.TargetCategory, Array.Empty<string>());
                _appLayout.Groups.Add(target);
            }
            if (!target.ItemNames.Contains(plan.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                target.ItemNames.Add(plan.DisplayName);
            }
            _appLayout.FreeIcons.Remove(plan.DisplayName);
            _appLayout.ItemLastMovedUtcTicks[plan.DisplayName] = DateTime.UtcNow.Ticks;
            UpdateAutoCategoryGroupSize(target);
            return true;
        }

        private void UpdateInboxButton()
        {
            int count = _appLayout.InboxItems.Count;
            InboxButton.Content = count switch
            {
                0 => "待整理",
                > 99 => "待整理 99+",
                _ => $"待整理 {count}"
            };
            InboxButton.ToolTip = count == 0
                ? "新出现的桌面项目会先进入本地待整理收件箱"
                : $"有 {count} 个桌面项目等待确认";
        }

        private static string FormatInboxActionError(InboxActionOutcome outcome) => outcome switch
        {
            InboxActionOutcome.NotFound => "该项目已不在待整理收件箱。",
            InboxActionOutcome.StaleIdentity => "项目身份已经变化，请先刷新桌面；本次未修改布局。",
            InboxActionOutcome.Unreliable => "当前建议证据不足，只能保持原位或明确加入手工分组。",
            InboxActionOutcome.ManualGroupProtected => "项目已在手工分组中，自动建议不会改写手工选择。",
            InboxActionOutcome.InvalidManualGroup => "目标手工分组已不存在。",
            _ => "操作未完成，收件箱和真实文件保持不变。"
        };
    }
}
