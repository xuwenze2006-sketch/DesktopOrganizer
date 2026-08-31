// 可解释、自定义且只执行虚拟动作的本地规则
namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private void RuleManagerButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new RuleManagerWindow(this);
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

        internal IReadOnlyList<UserRuleSummary> GetUserRuleSummaries() =>
            _appLayout.UserRules
                .OrderBy(rule => rule.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(rule => new UserRuleSummary(
                    rule.Id,
                    rule.Name,
                    rule.Lifecycle,
                    UserOrganizationRulePolicy.DescribeLifecycle(rule.Lifecycle),
                    UserOrganizationRulePolicy.DescribeConditions(rule),
                    UserOrganizationRulePolicy.DescribeAction(rule),
                    rule.UpdatedUtc))
                .ToList();

        internal int GetEnabledUserRuleCount() =>
            _appLayout.UserRules.Count(rule => rule.Lifecycle == UserRuleLifecycle.Enabled);

        internal bool TryGetUserRuleEditor(string id, out UserRuleEditorData? editor)
        {
            UserOrganizationRuleInfo? rule = FindUserRule(id);
            if (rule == null)
            {
                editor = null;
                return false;
            }
            editor = new UserRuleEditorData(
                rule.Id,
                rule.Name,
                string.Join("; ", rule.Extensions),
                rule.NameContains ?? string.Empty,
                rule.ItemKind,
                rule.CreatedWithinDays,
                rule.ModifiedWithinDays,
                rule.RequireTopLevelProjectMarker,
                rule.ActionKind,
                rule.ActionTargetName ?? string.Empty);
            return true;
        }

        internal bool TrySaveUserRule(
            UserRuleEditorData editor,
            out string ruleId,
            out string error)
        {
            ruleId = string.Empty;
            error = string.Empty;
            try
            {
                UserOrganizationRuleInfo? existing = string.IsNullOrWhiteSpace(editor.Id)
                    ? null
                    : FindUserRule(editor.Id);
                string requestedName = editor.Name?.Trim() ?? string.Empty;
                if (_appLayout.UserRules.Any(rule =>
                        !string.Equals(rule.Id, existing?.Id, StringComparison.OrdinalIgnoreCase) &&
                        rule.Name.Equals(requestedName, StringComparison.CurrentCultureIgnoreCase)))
                {
                    error = "已经存在同名规则。";
                    return false;
                }

                UserOrganizationRuleInfo saved = UserOrganizationRulePolicy.FromEditor(
                    editor,
                    DateTime.UtcNow,
                    existing);
                if (existing == null)
                {
                    _appLayout.UserRules.Add(saved);
                }
                else
                {
                    int index = _appLayout.UserRules.IndexOf(existing);
                    _appLayout.UserRules[index] = saved;
                }
                ruleId = saved.Id;
                SaveLayout();
                StatusText.Text = $"规则“{saved.Name}”已保存为禁用草稿；尚未执行";
                return true;
            }
            catch (ArgumentException exception)
            {
                error = exception.Message;
                return false;
            }
        }

        internal bool TryBuildUserRulePreview(
            string id,
            out UserRulePreviewView? preview,
            out string error)
        {
            preview = null;
            error = string.Empty;
            UserOrganizationRuleInfo? persisted = FindUserRule(id);
            if (persisted == null)
            {
                error = "规则已不存在。";
                return false;
            }

            try
            {
                OrganizationRule rule = UserOrganizationRulePolicy.CreateEngineRule(
                    persisted,
                    DateTimeOffset.UtcNow);
                OrganizationRulePreview enginePreview = OrganizationRuleEngine.Preview(
                    rule,
                    BuildOrganizationRuleContexts());
                IReadOnlyList<RulePreviewItemView> items = enginePreview.Items
                    .Select(item => new RulePreviewItemView(
                        item.DisplayName,
                        item.WillExecute,
                        item.TargetDescription,
                        string.Join("；", item.Conditions.Select(condition => condition.Reason)),
                        item.Conflict?.Message))
                    .ToList();
                preview = new UserRulePreviewView(
                    persisted.Id,
                    persisted.Name,
                    items,
                    enginePreview.ExecutionPlan.Actions.Count,
                    enginePreview.Items.Count(item => item.Conflict != null),
                    enginePreview.ExecutionPlan);
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidOperationException)
            {
                error = exception.Message;
                return false;
            }
        }

        internal bool ConfirmUserRulePreview(string id, out string error)
        {
            error = string.Empty;
            UserOrganizationRuleInfo? rule = FindUserRule(id);
            if (rule == null)
            {
                error = "规则已不存在。";
                return false;
            }
            if (rule.Lifecycle != UserRuleLifecycle.Draft)
            {
                error = "只有草稿规则可以确认预览；编辑规则会重新回到草稿。";
                return false;
            }
            rule.Lifecycle = UserRuleLifecycle.Previewed;
            rule.LastPreviewUtc = DateTime.UtcNow;
            rule.UpdatedUtc = rule.LastPreviewUtc.Value;
            SaveLayout();
            StatusText.Text = $"规则“{rule.Name}”已完成预览，仍未自动执行";
            return true;
        }

        internal bool TryExecuteUserRuleOnce(
            string id,
            UserRulePreviewView preview,
            out string error)
        {
            error = string.Empty;
            UserOrganizationRuleInfo? rule = FindUserRule(id);
            if (rule == null)
            {
                error = "规则已不存在。";
                return false;
            }
            if (rule.Lifecycle != UserRuleLifecycle.Previewed)
            {
                error = "规则必须先确认预览，才能执行一次。";
                return false;
            }
            if (!TryBuildUserRulePreview(id, out UserRulePreviewView? current, out error) || current == null)
            {
                return false;
            }
            if (!RulePlansEquivalent(preview.ExecutionPlan, current.ExecutionPlan))
            {
                error = "桌面状态在确认期间发生变化，请重新预览；本次没有修改。";
                return false;
            }

            if (!TryApplyRulePlan(rule, current.ExecutionPlan, out bool changed, out error))
            {
                return false;
            }
            rule.Lifecycle = UserRuleLifecycle.TrialApplied;
            rule.LastTrialRunUtc = DateTime.UtcNow;
            rule.UpdatedUtc = rule.LastTrialRunUtc.Value;
            if (changed)
            {
                RebuildDesktopIcons();
            }
            UpdateInboxButton();
            SaveLayout();
            StatusText.Text = $"规则“{rule.Name}”已执行一次：应用 {current.MatchCount} 项；真实文件未修改";
            return true;
        }

        internal bool TryEnableUserRule(string id, out string error)
        {
            error = string.Empty;
            UserOrganizationRuleInfo? rule = FindUserRule(id);
            if (rule == null)
            {
                error = "规则已不存在。";
                return false;
            }
            if (rule.Lifecycle != UserRuleLifecycle.TrialApplied)
            {
                error = "规则必须先预览并成功执行一次，才能启用自动应用。";
                return false;
            }
            rule.Lifecycle = UserRuleLifecycle.Enabled;
            rule.UpdatedUtc = DateTime.UtcNow;
            SaveLayout();
            StatusText.Text = $"规则“{rule.Name}”已启用；以后只自动执行虚拟分组、标签或收件箱动作";
            return true;
        }

        internal bool TryDisableUserRule(string id, out string error)
        {
            error = string.Empty;
            UserOrganizationRuleInfo? rule = FindUserRule(id);
            if (rule == null)
            {
                error = "规则已不存在。";
                return false;
            }
            if (rule.Lifecycle != UserRuleLifecycle.Enabled)
            {
                error = "该规则当前没有启用自动应用。";
                return false;
            }
            rule.Lifecycle = UserRuleLifecycle.TrialApplied;
            rule.UpdatedUtc = DateTime.UtcNow;
            SaveLayout();
            StatusText.Text = $"规则“{rule.Name}”已停用；已有虚拟整理结果保持不变";
            return true;
        }

        internal bool TryDisableAllUserRules(out string message)
        {
            int disabledCount = UserOrganizationRulePolicy.DisableEnabledRules(
                _appLayout.UserRules,
                DateTime.UtcNow);
            if (disabledCount == 0)
            {
                message = "当前没有已启用自动应用的规则。";
                StatusText.Text = message;
                return false;
            }

            SaveLayout();
            message = $"已停用 {disabledCount} 条自动应用规则；已有虚拟整理结果保持不变。";
            StatusText.Text = message;
            return true;
        }

        internal bool TryDeleteUserRule(string id, out string error)
        {
            error = string.Empty;
            UserOrganizationRuleInfo? rule = FindUserRule(id);
            if (rule == null)
            {
                error = "规则已不存在。";
                return false;
            }
            _appLayout.UserRules.Remove(rule);
            foreach (GroupInfo group in _appLayout.Groups.Where(group =>
                         string.Equals(group.UserRuleId, id, StringComparison.OrdinalIgnoreCase)))
            {
                group.UserRuleId = null;
            }
            RebuildDesktopIconsAndSaveLayout();
            StatusText.Text = $"规则“{rule.Name}”已删除；已有虚拟分组保留为手工分组";
            return true;
        }

        internal bool TryExportOrganizationData(Window owner, out string message)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出本地规则与标签",
                Filter = "JSON 文件 (*.json)|*.json",
                FileName = $"DesktopOrganizer-organization-{DateTime.Now:yyyyMMdd-HHmmss}.json",
                AddExtension = true,
                DefaultExt = ".json"
            };
            if (dialog.ShowDialog(owner) != true)
            {
                message = "已取消导出。";
                return false;
            }

            try
            {
                var export = new OrganizationDataExport
                {
                    Rules = _appLayout.UserRules.Select(UserOrganizationRulePolicy.Clone).ToList(),
                    Tags = _appLayout.ItemTags.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value.ToList(),
                        StringComparer.OrdinalIgnoreCase),
                    TagIdentities = _appLayout.ItemTags.Keys
                        .Where(_appLayout.ItemIdentities.ContainsKey)
                        .ToDictionary(
                            name => name,
                            name => CloneRuleIdentity(_appLayout.ItemIdentities[name]),
                            StringComparer.OrdinalIgnoreCase)
                };
                string json = JsonSerializer.Serialize(export, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(dialog.FileName, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                message = $"已导出 {_appLayout.UserRules.Count} 条规则和 {_appLayout.ItemTags.Count} 个项目的标签。";
                return true;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException)
            {
                message = $"导出失败：{exception.Message}";
                return false;
            }
        }

        internal bool TryImportOrganizationData(Window owner, out string message)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "导入本地规则与标签",
                Filter = "JSON 文件 (*.json)|*.json",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(owner) != true)
            {
                message = "已取消导入。";
                return false;
            }

            try
            {
                OrganizationDataExport data = JsonSerializer.Deserialize<OrganizationDataExport>(
                    File.ReadAllText(dialog.FileName, Encoding.UTF8)) ??
                    throw new JsonException("导入文件为空。");
                if (data.Version != 1)
                {
                    throw new JsonException($"不支持的导入版本：{data.Version}。");
                }

                var staging = new AppLayoutData
                {
                    UserRules = data.Rules ?? new List<UserOrganizationRuleInfo>()
                };
                UserOrganizationRulePolicy.Normalize(staging);
                foreach (UserOrganizationRuleInfo rule in staging.UserRules)
                {
                    // 导入永不继承自动执行授权，必须在本机重新预览和试运行。
                    rule.Lifecycle = UserRuleLifecycle.Draft;
                    rule.LastPreviewUtc = null;
                    rule.LastTrialRunUtc = null;
                }

                var safeTags = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach ((string name, List<string>? tags) in data.Tags ?? new Dictionary<string, List<string>>())
                {
                    if (tags == null ||
                        !_appLayout.ItemIdentities.TryGetValue(name, out DesktopItemIdentityInfo? currentIdentity) ||
                        data.TagIdentities == null ||
                        !data.TagIdentities.TryGetValue(name, out DesktopItemIdentityInfo? exportedIdentity) ||
                        !InboxQueueManager.IdentitiesMatch(currentIdentity, exportedIdentity))
                    {
                        continue;
                    }
                    List<string> normalized = ItemTagPolicy.Normalize(tags);
                    if (normalized.Count > 0)
                    {
                        safeTags[name] = normalized;
                    }
                }

                if (MessageBox.Show(
                        owner,
                        $"将用导入文件替换当前 {_appLayout.UserRules.Count} 条规则和 {_appLayout.ItemTags.Count} 个标签项目。\n\n" +
                        $"可导入 {staging.UserRules.Count} 条规则（全部重置为禁用草稿）和 {safeTags.Count} 个身份匹配项目的标签。\n\n" +
                        "不会修改真实文件。是否继续？",
                        "导入规则与标签",
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Warning) != MessageBoxResult.OK)
                {
                    message = "已取消导入，当前数据保持不变。";
                    return false;
                }

                foreach (GroupInfo group in _appLayout.Groups.Where(group =>
                             !string.IsNullOrWhiteSpace(group.UserRuleId)))
                {
                    group.UserRuleId = null;
                }
                _appLayout.UserRules = staging.UserRules;
                _appLayout.ItemTags = safeTags;
                RebuildDesktopIconsAndSaveLayout();
                message = $"已导入 {staging.UserRules.Count} 条禁用草稿规则和 {safeTags.Count} 个项目的标签。";
                StatusText.Text = message;
                return true;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                message = $"导入失败，当前数据未改变：{exception.Message}";
                return false;
            }
        }

        private bool ApplyEnabledOrganizationRules()
        {
            if (_isSafeModeActive || HasPendingFileOperations)
            {
                return false;
            }
            bool changed = false;
            foreach (UserOrganizationRuleInfo rule in _appLayout.UserRules
                         .Where(rule => rule.Lifecycle == UserRuleLifecycle.Enabled)
                         .ToList())
            {
                string failure = string.Empty;
                bool applied = TryBuildUserRulePreview(
                    rule.Id,
                    out UserRulePreviewView? preview,
                    out failure) &&
                    preview != null;
                bool ruleChanged = false;
                if (applied)
                {
                    applied = TryApplyRulePlan(
                        rule,
                        preview!.ExecutionPlan,
                        out ruleChanged,
                        out failure);
                }
                if (!applied)
                {
                    rule.Lifecycle = UserRuleLifecycle.TrialApplied;
                    rule.UpdatedUtc = DateTime.UtcNow;
                    changed = true;
                    _diagnostics.Log($"RULE disabledAfterFailure id={rule.Id}, error={failure}");
                    StatusText.Text = $"规则“{rule.Name}”自动应用失败，已停用且不会自动重试";
                    continue;
                }
                changed |= ruleChanged;
            }
            return changed;
        }

        private IReadOnlyList<OrganizationRuleItemContext> BuildOrganizationRuleContexts()
        {
            var contexts = new List<OrganizationRuleItemContext>(_desktopItems.Count);
            foreach ((string name, string location) in _desktopItems)
            {
                _appLayout.ItemIdentities.TryGetValue(name, out DesktopItemIdentityInfo? identity);
                GroupInfo? manualGroup = _appLayout.Groups.FirstOrDefault(group =>
                    ((!group.IsAutoCategory &&
                      string.IsNullOrWhiteSpace(group.UserRuleId) &&
                      group.ItemNames.Contains(name, StringComparer.OrdinalIgnoreCase)) ||
                     group.ManuallyAssignedItemNames.Contains(
                         name,
                         StringComparer.OrdinalIgnoreCase)));
                bool reliable = _reliableDesktopCategoryNames.Contains(name);
                bool isDirectory = identity?.IsDirectory == true;
                bool? hasMarker = !isDirectory || identity?.Kind == DesktopItemKind.ShellNamespace
                    ? false
                    : !reliable
                        ? null
                        : _desktopCategories.TryGetValue(name, out DesktopCategoryDefinition? category) &&
                          category.Key.Equals("development-projects", StringComparison.OrdinalIgnoreCase);
                contexts.Add(new OrganizationRuleItemContext(
                    name,
                    location,
                    Path.GetExtension(identity?.LastKnownPath ?? location),
                    isDirectory,
                    ToDateTimeOffset(identity?.CreationTimeUtcTicks),
                    ToDateTimeOffset(identity?.LastWriteTimeUtcTicks),
                    hasMarker,
                    hasMarker == true ? "本地顶层项目标记" : null,
                    manualGroup?.Id,
                    manualGroup?.Name));
            }
            return contexts;
        }

        private bool TryApplyRulePlan(
            UserOrganizationRuleInfo rule,
            OrganizationRuleExecutionPlan plan,
            out bool changed,
            out string error)
        {
            changed = false;
            error = string.Empty;
            foreach (OrganizationRulePlannedAction action in plan.Actions)
            {
                if (!_desktopItems.TryGetValue(action.DisplayName, out string? currentLocation) ||
                    !ShellItemLocation.AreEquivalent(currentLocation, action.Location))
                {
                    error = "桌面项目在执行前发生变化；规则没有应用。";
                    return false;
                }
                if (action.Action.Kind != OrganizationRuleActionKind.AddTag &&
                    _appLayout.Groups.Any(group =>
                        group.ManuallyAssignedItemNames.Contains(
                            action.DisplayName,
                            StringComparer.OrdinalIgnoreCase)))
                {
                    error = "桌面项目在预览后被手工归组；规则没有应用。";
                    return false;
                }
            }

            WorkspaceLayoutState visualBackup = WorkspaceLayoutManager.Capture(_appLayout);
            Dictionary<string, List<string>> tagsBackup = _appLayout.ItemTags.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToList(),
                StringComparer.OrdinalIgnoreCase);
            Dictionary<string, InboxItemInfo> inboxBackup = CloneRuleInbox(_appLayout.InboxItems);
            var movedBackup = new Dictionary<string, long>(
                _appLayout.ItemLastMovedUtcTicks,
                StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (OrganizationRulePlannedAction action in plan.Actions)
                {
                    changed |= ApplyRuleAction(rule, action);
                }
                if (_lastSmartLayoutSnapshot != null &&
                    HasRuleGroupStateChanged(visualBackup, _appLayout.Groups))
                {
                    _lastSmartLayoutSnapshot = null;
                    UndoSmartLayoutButton.IsEnabled = false;
                }
                return true;
            }
            catch (Exception exception)
            {
                WorkspaceLayoutManager.Apply(_appLayout, visualBackup);
                _appLayout.ItemTags = tagsBackup;
                _appLayout.InboxItems = inboxBackup;
                _appLayout.ItemLastMovedUtcTicks = movedBackup;
                changed = false;
                error = $"规则执行失败：{exception.Message}";
                return false;
            }
        }

        private static bool HasRuleGroupStateChanged(
            WorkspaceLayoutState before,
            IReadOnlyList<GroupInfo> currentGroups)
        {
            if (before.Groups.Count != currentGroups.Count)
            {
                return true;
            }

            foreach (GroupInfo current in currentGroups)
            {
                GroupInfo? previous = before.Groups.FirstOrDefault(group =>
                    group.Id.Equals(current.Id, StringComparison.OrdinalIgnoreCase));
                if (previous == null ||
                    Math.Abs(previous.X - current.X) > 0.01 ||
                    Math.Abs(previous.Y - current.Y) > 0.01 ||
                    Math.Abs(previous.Width - current.Width) > 0.01 ||
                    Math.Abs(previous.Height - current.Height) > 0.01 ||
                    previous.IsCollapsed != current.IsCollapsed ||
                    previous.IsSizeLocked != current.IsSizeLocked ||
                    !new HashSet<string>(
                        previous.ItemNames,
                        StringComparer.OrdinalIgnoreCase).SetEquals(current.ItemNames) ||
                    !new HashSet<string>(
                        previous.ManuallyAssignedItemNames,
                        StringComparer.OrdinalIgnoreCase).SetEquals(
                            current.ManuallyAssignedItemNames))
                {
                    return true;
                }
            }

            return false;
        }

        private bool ApplyRuleAction(
            UserOrganizationRuleInfo rule,
            OrganizationRulePlannedAction planned)
        {
            string name = planned.DisplayName;
            switch (planned.Action.Kind)
            {
                case OrganizationRuleActionKind.AddTag:
                    return ItemTagPolicy.AddTag(
                        _appLayout.ItemTags,
                        name,
                        planned.Action.TargetName);

                case OrganizationRuleActionKind.AddToVirtualGroup:
                    return ApplyRuleVirtualGroup(rule, planned);

                case OrganizationRuleActionKind.SendToInbox:
                    return ApplyRuleInbox(rule, planned);

                default:
                    throw new InvalidOperationException("规则包含未知动作。");
            }
        }

        private bool ApplyRuleVirtualGroup(
            UserOrganizationRuleInfo rule,
            OrganizationRulePlannedAction planned)
        {
            string name = planned.DisplayName;
            bool changed = false;
            GroupInfo? target = _appLayout.Groups.FirstOrDefault(group =>
                string.Equals(group.UserRuleId, rule.Id, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                target = new GroupInfo
                {
                    Name = planned.Action.TargetName,
                    UserRuleId = rule.Id,
                    SortMode = GroupSortMode.Name,
                    IsSizeLocked = false
                };
                AutoFitGroup(target, clampPosition: false);
                Point position = FindAvailableAutoGroupPosition(target.Width, GetGroupDisplayHeight(target));
                target.X = position.X;
                target.Y = position.Y;
                _appLayout.Groups.Add(target);
                changed = true;
            }
            else if (!target.Name.Equals(planned.Action.TargetName, StringComparison.Ordinal))
            {
                target.Name = planned.Action.TargetName;
                changed = true;
            }

            foreach (GroupInfo group in _appLayout.Groups.Where(group =>
                         group.IsAutoCategory || !string.IsNullOrWhiteSpace(group.UserRuleId)))
            {
                if (ReferenceEquals(group, target))
                {
                    continue;
                }
                changed |= group.ItemNames.RemoveAll(item =>
                    item.Equals(name, StringComparison.OrdinalIgnoreCase)) > 0;
                changed |= group.ManuallyAssignedItemNames.RemoveAll(item =>
                    item.Equals(name, StringComparison.OrdinalIgnoreCase)) > 0;
            }
            if (!target.ItemNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                target.ItemNames.Add(name);
                changed = true;
            }
            changed |= _appLayout.FreeIcons.Remove(name);
            if (changed)
            {
                _appLayout.ItemLastMovedUtcTicks[name] = DateTime.UtcNow.Ticks;
                UpdateAutoCategoryGroupSize(target);
            }
            return changed;
        }

        private bool ApplyRuleInbox(
            UserOrganizationRuleInfo rule,
            OrganizationRulePlannedAction planned)
        {
            string name = planned.DisplayName;
            bool changed = false;
            foreach (GroupInfo group in _appLayout.Groups.Where(group =>
                         group.IsAutoCategory || !string.IsNullOrWhiteSpace(group.UserRuleId)))
            {
                changed |= group.ItemNames.RemoveAll(item =>
                    item.Equals(name, StringComparison.OrdinalIgnoreCase)) > 0;
                changed |= group.ManuallyAssignedItemNames.RemoveAll(item =>
                    item.Equals(name, StringComparison.OrdinalIgnoreCase)) > 0;
            }
            if (!_appLayout.InboxItems.ContainsKey(name) &&
                _appLayout.ItemIdentities.TryGetValue(name, out DesktopItemIdentityInfo? identity))
            {
                _desktopCategories.TryGetValue(name, out DesktopCategoryDefinition? category);
                bool reliable = _reliableDesktopCategoryNames.Contains(name) && category != null;
                _appLayout.InboxItems[name] = new InboxItemInfo
                {
                    Identity = CloneRuleIdentity(identity),
                    DetectedUtc = DateTime.UtcNow,
                    UpdatedUtc = DateTime.UtcNow,
                    SuggestedCategoryKey = category?.Key ?? string.Empty,
                    SuggestedCategoryName = category?.DisplayName ?? string.Empty,
                    SuggestedCategoryOrder = category?.Order ?? int.MaxValue,
                    MatchReason = $"用户规则“{rule.Name}”命中：{planned.ReasonSummary}",
                    Reliability = reliable
                        ? ClassificationReliability.Reliable
                        : ClassificationReliability.Conservative,
                    ReviewState = InboxReviewState.Pending
                };
                changed = true;
            }
            if (changed)
            {
                _appLayout.ItemLastMovedUtcTicks[name] = DateTime.UtcNow.Ticks;
            }
            return changed;
        }

        private static bool RulePlansEquivalent(
            OrganizationRuleExecutionPlan first,
            OrganizationRuleExecutionPlan second)
        {
            if (!first.RuleId.Equals(second.RuleId, StringComparison.OrdinalIgnoreCase) ||
                first.Actions.Count != second.Actions.Count)
            {
                return false;
            }
            for (int index = 0; index < first.Actions.Count; index++)
            {
                OrganizationRulePlannedAction left = first.Actions[index];
                OrganizationRulePlannedAction right = second.Actions[index];
                if (!left.DisplayName.Equals(right.DisplayName, StringComparison.Ordinal) ||
                    !ShellItemLocation.AreEquivalent(left.Location, right.Location) ||
                    left.Action.Kind != right.Action.Kind ||
                    !left.Action.TargetName.Equals(right.Action.TargetName, StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        private static DateTimeOffset? ToDateTimeOffset(long? ticks)
        {
            if (!ticks.HasValue || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
            {
                return null;
            }
            return new DateTimeOffset(new DateTime(ticks.Value, DateTimeKind.Utc));
        }

        private UserOrganizationRuleInfo? FindUserRule(string? id) =>
            string.IsNullOrWhiteSpace(id)
                ? null
                : _appLayout.UserRules.FirstOrDefault(rule =>
                    rule.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        private static Dictionary<string, InboxItemInfo> CloneRuleInbox(
            IReadOnlyDictionary<string, InboxItemInfo> source) => source.ToDictionary(
                pair => pair.Key,
                pair => new InboxItemInfo
                {
                    Identity = CloneRuleIdentity(pair.Value.Identity),
                    DetectedUtc = pair.Value.DetectedUtc,
                    UpdatedUtc = pair.Value.UpdatedUtc,
                    SuggestedCategoryKey = pair.Value.SuggestedCategoryKey,
                    SuggestedCategoryName = pair.Value.SuggestedCategoryName,
                    SuggestedCategoryOrder = pair.Value.SuggestedCategoryOrder,
                    MatchReason = pair.Value.MatchReason,
                    Reliability = pair.Value.Reliability,
                    ReviewState = pair.Value.ReviewState
                },
                StringComparer.OrdinalIgnoreCase);

        private static DesktopItemIdentityInfo CloneRuleIdentity(DesktopItemIdentityInfo identity) => new()
        {
            Kind = identity.Kind,
            LastKnownPath = identity.LastKnownPath,
            FileId = identity.FileId,
            ShellParsingName = identity.ShellParsingName,
            CreationTimeUtcTicks = identity.CreationTimeUtcTicks,
            LastWriteTimeUtcTicks = identity.LastWriteTimeUtcTicks,
            IsDirectory = identity.IsDirectory
        };
    }
}
