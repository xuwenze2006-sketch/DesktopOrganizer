namespace DesktopOrganizer
{
    internal enum InboxKeyboardAction
    {
        None,
        AcceptSuggestion,
        LeaveOnDesktop,
        Defer
    }

    public partial class InboxWindow : Window
    {
        private readonly MainWindow _mainWindow;

        internal InboxWindow(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            InitializeComponent();
            Refresh();
        }

        private InboxListItemView? Selected => InboxList.SelectedItem as InboxListItemView;

        private void Refresh(string? selectedName = null)
        {
            List<InboxListItemView> items = _mainWindow.GetInboxItems().ToList();
            InboxList.ItemsSource = items;
            InboxList.SelectedItem = items.FirstOrDefault(item =>
                item.DisplayName.Equals(selectedName, StringComparison.OrdinalIgnoreCase)) ??
                items.FirstOrDefault();
            List<ManualGroupChoice> groups = _mainWindow.GetManualInboxGroups().ToList();
            ManualGroupSelector.ItemsSource = groups;
            ManualGroupSelector.SelectedIndex = groups.Count > 0 ? 0 : -1;
            int bulkAcceptCount = _mainWindow.GetPendingReliableInboxSuggestionCount();
            AcceptAllReliableButton.Content = $"接受全部可靠建议 ({bulkAcceptCount})";
            AcceptAllReliableButton.IsEnabled = bulkAcceptCount > 0;
            UpdateButtons();
        }

        private void InboxList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            UpdateButtons();

        private void InboxList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = TryExecuteKeyboardAction(
                ResolveKeyboardAction(e.Key, Keyboard.Modifiers));
        }

        private void TagEditorBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!ShouldSaveTagsFromKeyboard(
                    e.Key,
                    Keyboard.Modifiers,
                    e.IsRepeat))
            {
                return;
            }

            e.Handled = true;
            SaveTags_Click(sender, e);
        }

        private void UpdateButtons()
        {
            InboxListItemView? selected = Selected;
            AcceptButton.IsEnabled = selected?.CanAccept == true;
            SaveTagsButton.IsEnabled = selected != null;
            TagEditorBox.IsEnabled = selected != null;
            TagEditorBox.Text = selected?.TagsText ?? string.Empty;
            if (InboxList.Items.Count == 0)
            {
                StatusText.Text = "当前没有待整理项目。";
            }
        }

        private void Accept_Click(object sender, RoutedEventArgs e) =>
            ExecuteSelectedAction(InboxKeyboardAction.AcceptSuggestion);

        private void AcceptAllReliable_Click(object sender, RoutedEventArgs e)
        {
            _ = _mainWindow.TryAcceptPendingReliableInboxSuggestions(out string message);
            Refresh();
            StatusText.Text = message;
        }

        private void Leave_Click(object sender, RoutedEventArgs e) =>
            ExecuteSelectedAction(InboxKeyboardAction.LeaveOnDesktop);

        private void Defer_Click(object sender, RoutedEventArgs e) =>
            ExecuteSelectedAction(InboxKeyboardAction.Defer);

        private void SaveTags_Click(object sender, RoutedEventArgs e)
        {
            InboxListItemView? selected = Selected;
            if (selected == null)
            {
                StatusText.Text = "请先选择一个待整理项目。";
                return;
            }

            string selectedName = selected.DisplayName;
            bool succeeded = _mainWindow.TrySetInboxItemTags(
                selectedName,
                TagEditorBox.Text,
                out string message);
            if (succeeded)
            {
                Refresh(selectedName);
            }
            StatusText.Text = message;
        }

        private void MoveToManualGroup_Click(object sender, RoutedEventArgs e)
        {
            InboxListItemView? selected = Selected;
            ManualGroupChoice? group = ManualGroupSelector.SelectedItem as ManualGroupChoice;
            if (selected == null || group == null)
            {
                StatusText.Text = "请先选择待整理项目和一个手工分组。";
                return;
            }

            string selectedName = selected.DisplayName;
            bool succeeded = _mainWindow.TryMoveInboxItemToManualGroup(
                selectedName,
                group.Id,
                out string message);
            StatusText.Text = message;
            if (succeeded)
            {
                Refresh();
            }
        }

        private delegate bool InboxAction(string displayName, out string message);

        private bool TryExecuteKeyboardAction(InboxKeyboardAction action)
        {
            if (Selected == null || action == InboxKeyboardAction.None)
            {
                return false;
            }

            ExecuteSelectedAction(action);
            return true;
        }

        internal static InboxKeyboardAction ResolveKeyboardAction(
            Key key,
            ModifierKeys modifiers)
        {
            if (key != Key.Enter)
            {
                return InboxKeyboardAction.None;
            }

            return modifiers switch
            {
                ModifierKeys.None => InboxKeyboardAction.AcceptSuggestion,
                ModifierKeys.Control => InboxKeyboardAction.LeaveOnDesktop,
                ModifierKeys.Shift => InboxKeyboardAction.Defer,
                _ => InboxKeyboardAction.None
            };
        }

        internal static bool ShouldSaveTagsFromKeyboard(
            Key key,
            ModifierKeys modifiers,
            bool isRepeat) =>
            key == Key.S &&
            modifiers == ModifierKeys.Control &&
            !isRepeat;

        private void RunSelectedAction(InboxAction action)
        {
            InboxListItemView? selected = Selected;
            if (selected == null)
            {
                StatusText.Text = "请先选择一个待整理项目。";
                return;
            }

            bool succeeded = action(selected.DisplayName, out string message);
            StatusText.Text = message;
            if (succeeded)
            {
                Refresh();
            }
        }

        private void ExecuteSelectedAction(InboxKeyboardAction action)
        {
            InboxAction? selectedAction = action switch
            {
                InboxKeyboardAction.AcceptSuggestion => _mainWindow.TryAcceptInboxSuggestion,
                InboxKeyboardAction.LeaveOnDesktop => _mainWindow.TryLeaveInboxItemOnDesktop,
                InboxKeyboardAction.Defer => _mainWindow.TryDeferInboxItem,
                _ => null
            };
            if (selectedAction != null)
            {
                RunSelectedAction(selectedAction);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
