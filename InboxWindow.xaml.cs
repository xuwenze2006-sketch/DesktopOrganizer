namespace DesktopOrganizer
{
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
            RunSelectedAction((string name, out string message) =>
                _mainWindow.TryAcceptInboxSuggestion(name, out message));

        private void AcceptAllReliable_Click(object sender, RoutedEventArgs e)
        {
            _ = _mainWindow.TryAcceptPendingReliableInboxSuggestions(out string message);
            Refresh();
            StatusText.Text = message;
        }

        private void Leave_Click(object sender, RoutedEventArgs e) =>
            RunSelectedAction((string name, out string message) =>
                _mainWindow.TryLeaveInboxItemOnDesktop(name, out message));

        private void Defer_Click(object sender, RoutedEventArgs e) =>
            RunSelectedAction((string name, out string message) =>
                _mainWindow.TryDeferInboxItem(name, out message));

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

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
