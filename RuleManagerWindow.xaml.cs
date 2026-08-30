namespace DesktopOrganizer
{
    public partial class RuleManagerWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private bool _suppressEditorChanges;
        private bool _editorDirty;
        private string? _editingRuleId;

        private sealed record ItemKindChoice(UserRuleItemKindFilter Value, string Name);
        private sealed record ActionChoice(OrganizationRuleActionKind Value, string Name);

        internal RuleManagerWindow(MainWindow mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            InitializeComponent();
            ItemKindSelector.ItemsSource = new[]
            {
                new ItemKindChoice(UserRuleItemKindFilter.Any, "文件或文件夹"),
                new ItemKindChoice(UserRuleItemKindFilter.File, "仅文件"),
                new ItemKindChoice(UserRuleItemKindFilter.Folder, "仅文件夹")
            };
            ActionSelector.ItemsSource = new[]
            {
                new ActionChoice(OrganizationRuleActionKind.AddToVirtualGroup, "加入虚拟分组"),
                new ActionChoice(OrganizationRuleActionKind.AddTag, "添加标签"),
                new ActionChoice(OrganizationRuleActionKind.SendToInbox, "进入待整理收件箱")
            };
            RefreshList();
        }

        private UserRuleSummary? Selected => RuleList.SelectedItem as UserRuleSummary;

        private void RefreshList(string? selectedId = null)
        {
            List<UserRuleSummary> summaries = _mainWindow.GetUserRuleSummaries().ToList();
            RuleList.ItemsSource = summaries;
            UserRuleSummary? selection = summaries.FirstOrDefault(summary =>
                summary.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase)) ??
                summaries.FirstOrDefault();
            RuleList.SelectedItem = selection;
            if (selection == null)
            {
                LoadEditor(NewEditorData());
            }
            else
            {
                LoadSelectedEditor(selection);
            }
        }

        private void RuleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UserRuleSummary? selection = Selected;
            if (selection == null)
            {
                return;
            }
            LoadSelectedEditor(selection);
        }

        private void LoadSelectedEditor(UserRuleSummary summary)
        {
            if (!_mainWindow.TryGetUserRuleEditor(
                    summary.Id,
                    out UserRuleEditorData? editor) ||
                editor == null)
            {
                SetStatus("无法读取所选规则，列表已刷新。", isError: true);
                RefreshList();
                return;
            }
            LoadEditor(editor);
        }

        private void LoadEditor(UserRuleEditorData editor)
        {
            _suppressEditorChanges = true;
            try
            {
                _editingRuleId = editor.Id;
                RuleNameBox.Text = editor.Name;
                ExtensionsBox.Text = editor.Extensions;
                NameContainsBox.Text = editor.NameContains;
                CreatedDaysBox.Text = editor.CreatedWithinDays?.ToString() ?? string.Empty;
                ModifiedDaysBox.Text = editor.ModifiedWithinDays?.ToString() ?? string.Empty;
                ProjectMarkerCheckBox.IsChecked = editor.RequireTopLevelProjectMarker;
                ItemKindSelector.SelectedItem = ItemKindSelector.Items
                    .OfType<ItemKindChoice>()
                    .First(choice => choice.Value == editor.ItemKind);
                ActionSelector.SelectedItem = ActionSelector.Items
                    .OfType<ActionChoice>()
                    .First(choice => choice.Value == editor.ActionKind);
                ActionTargetBox.Text = editor.ActionTargetName;
                _editorDirty = false;
                UpdateActionTargetEditor();
                UpdateLifecycleButtons();
            }
            finally
            {
                _suppressEditorChanges = false;
            }
        }

        private static UserRuleEditorData NewEditorData() => new(
            Id: null,
            Name: "新规则",
            Extensions: string.Empty,
            NameContains: string.Empty,
            ItemKind: UserRuleItemKindFilter.Any,
            CreatedWithinDays: null,
            ModifiedWithinDays: null,
            RequireTopLevelProjectMarker: false,
            ActionKind: OrganizationRuleActionKind.SendToInbox,
            ActionTargetName: string.Empty);

        private void NewRule_Click(object sender, RoutedEventArgs e)
        {
            RuleList.SelectedItem = null;
            LoadEditor(NewEditorData());
            RuleNameBox.Focus();
            RuleNameBox.SelectAll();
            SetStatus("正在编辑新规则；保存后为草稿且不会自动执行。", isError: false);
        }

        private void EditorChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressEditorChanges)
            {
                return;
            }
            _editorDirty = true;
            UpdateLifecycleButtons();
            SetStatus("编辑内容尚未保存；保存后规则将回到草稿（禁用）。", isError: false);
        }

        private void ActionSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateActionTargetEditor();
            EditorChanged(sender, e);
        }

        private void RuleEditor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool hasUnsavedEditor = _editorDirty ||
                string.IsNullOrWhiteSpace(_editingRuleId);
            if (!ShouldSaveDraftFromKeyboard(
                    e.Key,
                    Keyboard.Modifiers,
                    e.IsRepeat,
                    hasUnsavedEditor))
            {
                return;
            }

            e.Handled = true;
            SaveDraft_Click(sender, e);
        }

        internal static bool ShouldSaveDraftFromKeyboard(
            Key key,
            ModifierKeys modifiers,
            bool isRepeat,
            bool hasUnsavedEditor) =>
            key == Key.S &&
            modifiers == ModifierKeys.Control &&
            !isRepeat &&
            hasUnsavedEditor;

        private void UpdateActionTargetEditor()
        {
            OrganizationRuleActionKind action = SelectedActionKind;
            bool needsTarget = action != OrganizationRuleActionKind.SendToInbox;
            ActionTargetBox.IsEnabled = needsTarget;
            ActionTargetLabel.Text = action switch
            {
                OrganizationRuleActionKind.AddToVirtualGroup => "虚拟分组名称",
                OrganizationRuleActionKind.AddTag => "标签名称",
                OrganizationRuleActionKind.SendToInbox => "目标（固定为待整理收件箱）",
                _ => "目标名称"
            };
            if (!needsTarget)
            {
                ActionTargetBox.Text = string.Empty;
            }
        }

        private OrganizationRuleActionKind SelectedActionKind =>
            (ActionSelector.SelectedItem as ActionChoice)?.Value ??
            OrganizationRuleActionKind.SendToInbox;

        private void SaveDraft_Click(object sender, RoutedEventArgs e)
        {
            if (!TryBuildEditor(out UserRuleEditorData? editor, out string validationError) ||
                editor == null)
            {
                SetStatus(validationError, isError: true);
                return;
            }

            if (!_mainWindow.TrySaveUserRule(
                    editor,
                    out string ruleId,
                    out string error))
            {
                SetStatus(error, isError: true);
                return;
            }

            RefreshList(ruleId);
            SetStatus("规则已保存为草稿并保持禁用；请先逐项预览。", isError: false);
        }

        private bool TryBuildEditor(out UserRuleEditorData? editor, out string error)
        {
            editor = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(RuleNameBox.Text))
            {
                error = "规则名称不能为空。";
                return false;
            }
            if (!TryParseDays(CreatedDaysBox.Text, "创建日期", out int? createdDays, out error) ||
                !TryParseDays(ModifiedDaysBox.Text, "修改日期", out int? modifiedDays, out error))
            {
                return false;
            }

            UserRuleItemKindFilter itemKind =
                (ItemKindSelector.SelectedItem as ItemKindChoice)?.Value ??
                UserRuleItemKindFilter.Any;
            string target = SelectedActionKind == OrganizationRuleActionKind.SendToInbox
                ? string.Empty
                : ActionTargetBox.Text.Trim();
            editor = new UserRuleEditorData(
                _editingRuleId,
                RuleNameBox.Text.Trim(),
                ExtensionsBox.Text,
                NameContainsBox.Text,
                itemKind,
                createdDays,
                modifiedDays,
                ProjectMarkerCheckBox.IsChecked == true,
                SelectedActionKind,
                target);
            return true;
        }

        private static bool TryParseDays(
            string? text,
            string label,
            out int? value,
            out string error)
        {
            value = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }
            if (!int.TryParse(text.Trim(), out int parsed) || parsed is < 1 or > 3650)
            {
                error = $"{label}天数必须是 1 到 3650 的整数，或留空表示不限制。";
                return false;
            }
            value = parsed;
            return true;
        }

        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            UserRuleSummary? rule = RequireSelection();
            if (rule == null)
            {
                return;
            }
            if (!_mainWindow.TryBuildUserRulePreview(
                    rule.Id,
                    out UserRulePreviewView? preview,
                    out string error) ||
                preview == null)
            {
                SetStatus(error, isError: true);
                return;
            }

            var window = new RulePreviewWindow(preview, RulePreviewPurpose.ConfirmPreview)
            {
                Owner = this
            };
            if (window.ShowDialog() != true)
            {
                SetStatus("已取消预览确认；规则仍为草稿且没有任何状态变化。", isError: false);
                return;
            }
            if (!_mainWindow.ConfirmUserRulePreview(rule.Id, out error))
            {
                SetStatus(error, isError: true);
                return;
            }
            RefreshList(rule.Id);
            SetStatus("已确认逐项预览；现在可以再次生成预览并执行一次。", isError: false);
        }

        private void ExecuteOnce_Click(object sender, RoutedEventArgs e)
        {
            UserRuleSummary? rule = RequireSelection();
            if (rule == null)
            {
                return;
            }

            // 执行前必须重新获取当前桌面快照的预览，绝不复用先前确认时的旧计划。
            if (!_mainWindow.TryBuildUserRulePreview(
                    rule.Id,
                    out UserRulePreviewView? preview,
                    out string error) ||
                preview == null)
            {
                SetStatus(error, isError: true);
                return;
            }
            var window = new RulePreviewWindow(preview, RulePreviewPurpose.ExecuteOnce)
            {
                Owner = this
            };
            if (window.ShowDialog() != true)
            {
                SetStatus("已取消执行；当前布局、标签和收件箱均未改变。", isError: false);
                return;
            }
            if (!_mainWindow.TryExecuteUserRuleOnce(rule.Id, preview, out error))
            {
                SetStatus(error, isError: true);
                return;
            }
            RefreshList(rule.Id);
            SetStatus("规则已按刚刚确认的当前预览执行一次；现在可以启用自动应用。", isError: false);
        }

        private void Enable_Click(object sender, RoutedEventArgs e)
        {
            UserRuleSummary? rule = RequireSelection();
            if (rule == null)
            {
                return;
            }
            if (MessageBox.Show(
                    this,
                    $"启用“{rule.Name}”的自动应用？\n\n之后命中的新桌面项目会自动更新虚拟分组、标签或收件箱；不会移动、重命名或删除真实文件。",
                    "启用自动应用",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Question) != MessageBoxResult.OK)
            {
                return;
            }
            if (!_mainWindow.TryEnableUserRule(rule.Id, out string error))
            {
                SetStatus(error, isError: true);
                return;
            }
            RefreshList(rule.Id);
            SetStatus("规则自动应用已启用。", isError: false);
        }

        private void Disable_Click(object sender, RoutedEventArgs e)
        {
            UserRuleSummary? rule = RequireSelection();
            if (rule == null)
            {
                return;
            }
            if (!_mainWindow.TryDisableUserRule(rule.Id, out string error))
            {
                SetStatus(error, isError: true);
                return;
            }
            RefreshList(rule.Id);
            SetStatus("规则自动应用已停用；已有虚拟整理结果保持不变。", isError: false);
        }

        private void DisableAllRules_Click(object sender, RoutedEventArgs e)
        {
            if (_editorDirty)
            {
                SetStatus("请先保存或重新选择规则，再停用全部自动应用规则。", isError: true);
                return;
            }

            string? selectedId = Selected?.Id;
            bool succeeded = _mainWindow.TryDisableAllUserRules(out string message);
            if (succeeded)
            {
                RefreshList(selectedId);
            }
            SetStatus(message, isError: false);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            UserRuleSummary? rule = RequireSelection();
            if (rule == null)
            {
                return;
            }
            if (MessageBox.Show(
                    this,
                    $"删除规则“{rule.Name}”？\n\n规则创建的已有标签和虚拟布局不会被自动删除，真实文件也不会改变。",
                    "删除规则",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                return;
            }
            if (!_mainWindow.TryDeleteUserRule(rule.Id, out string error))
            {
                SetStatus(error, isError: true);
                return;
            }
            RefreshList();
            SetStatus("规则已删除。", isError: false);
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            bool succeeded = _mainWindow.TryExportOrganizationData(this, out string message);
            SetStatus(message, isError: !succeeded);
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            bool succeeded = _mainWindow.TryImportOrganizationData(this, out string message);
            if (succeeded)
            {
                RefreshList();
            }
            SetStatus(message, isError: !succeeded);
        }

        private UserRuleSummary? RequireSelection()
        {
            UserRuleSummary? rule = Selected;
            if (rule == null)
            {
                SetStatus("请先选择并保存一个规则。", isError: true);
            }
            return rule;
        }

        private void UpdateLifecycleButtons()
        {
            UserRuleSummary? selected = Selected;
            bool persistedSelection = selected != null &&
                string.Equals(selected.Id, _editingRuleId, StringComparison.OrdinalIgnoreCase);
            UserRuleLifecycle? lifecycle = persistedSelection ? selected!.Lifecycle : null;
            bool canRunLifecycleAction = persistedSelection && !_editorDirty;

            LifecycleText.Text = lifecycle.HasValue
                ? UserOrganizationRulePolicy.DescribeLifecycle(lifecycle.Value)
                : "尚未保存";
            PreviewButton.IsEnabled = canRunLifecycleAction && lifecycle == UserRuleLifecycle.Draft;
            ExecuteOnceButton.IsEnabled = canRunLifecycleAction && lifecycle == UserRuleLifecycle.Previewed;
            EnableButton.IsEnabled = canRunLifecycleAction && lifecycle == UserRuleLifecycle.TrialApplied;
            DisableButton.IsEnabled = canRunLifecycleAction && lifecycle == UserRuleLifecycle.Enabled;
            DeleteButton.IsEnabled = persistedSelection;
            int enabledCount = _mainWindow.GetEnabledUserRuleCount();
            DisableAllRulesButton.Content = $"全部停用 ({enabledCount})";
            DisableAllRulesButton.IsEnabled = enabledCount > 0 && !_editorDirty;
        }

        private void SetStatus(string? message, bool isError)
        {
            StatusText.Text = string.IsNullOrWhiteSpace(message) ? "操作未返回详细信息。" : message;
            StatusText.Foreground = isError
                ? new SolidColorBrush(Color.FromRgb(185, 28, 28))
                : new SolidColorBrush(Color.FromRgb(71, 85, 105));
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
