namespace DesktopOrganizer
{
    internal enum DesktopSearchKeyboardAction
    {
        None,
        Locate,
        Open,
        Reveal
    }

    public partial class DesktopSearchWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private string? _explicitSelectionDisplayName;
        private bool _isRefreshingResults;

        private sealed record SmartViewChoice(DesktopSmartView View, string Name);

        private sealed record SearchListItem(DesktopSearchResult Result)
        {
            public string DisplayName => Result.DisplayName;

            public string Summary
            {
                get
                {
                    var parts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(Result.TypeDisplayName))
                    {
                        parts.Add(Result.TypeDisplayName);
                    }
                    if (!string.IsNullOrWhiteSpace(Result.GroupName))
                    {
                        parts.Add($"分组：{Result.GroupName}");
                    }
                    if (Result.Tags.Count > 0)
                    {
                        parts.Add($"标签：{string.Join("、", Result.Tags)}");
                    }
                    if (Result.IsPendingConfirmation)
                    {
                        parts.Add("待确认");
                    }
                    return parts.Count == 0 ? Result.Location : string.Join(" · ", parts);
                }
            }
        }

        internal DesktopSearchWindow(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            InitializeComponent();
            ViewSelector.ItemsSource = new[]
            {
                new SmartViewChoice(DesktopSmartView.All, "全部桌面项目"),
                new SmartViewChoice(DesktopSmartView.Unclassified, "未分类"),
                new SmartViewChoice(DesktopSmartView.RecentlyAdded, "近期新增"),
                new SmartViewChoice(DesktopSmartView.PendingConfirmation, "待确认"),
                new SmartViewChoice(DesktopSmartView.RecentlyMoved, "最近移动")
            };
            ViewSelector.SelectedIndex = 0;
            RefreshResults();
            Loaded += (_, _) => QueryBox.Focus();
        }

        private SearchListItem? Selected => ResultsList.SelectedItem as SearchListItem;

        private void DesktopSearchWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!ShouldFocusQueryFromKeyboard(
                    e.Key,
                    Keyboard.Modifiers,
                    e.IsRepeat) ||
                !QueryBox.Focus())
            {
                return;
            }

            QueryBox.SelectAll();
            e.Handled = true;
        }

        private void RefreshResults()
        {
            DesktopSmartView view = (ViewSelector.SelectedItem as SmartViewChoice)?.View ??
                                    DesktopSmartView.All;
            List<SearchListItem> items = _mainWindow
                .SearchLoadedDesktopItems(QueryBox.Text, view)
                .Select(result => new SearchListItem(result))
                .ToList();
            string? explicitSelectionDisplayName = _explicitSelectionDisplayName;
            int selectionIndex = ResolveRefreshedSelectionIndex(
                items.Select(item => item.DisplayName).ToList(),
                explicitSelectionDisplayName);
            bool preservedExplicitSelection =
                explicitSelectionDisplayName is not null &&
                selectionIndex >= 0 &&
                items[selectionIndex].DisplayName.Equals(
                    explicitSelectionDisplayName,
                    StringComparison.OrdinalIgnoreCase);

            _isRefreshingResults = true;
            try
            {
                ResultsList.ItemsSource = items;
                ResultsList.SelectedIndex = selectionIndex;
                if (preservedExplicitSelection)
                {
                    ResultsList.ScrollIntoView(items[selectionIndex]);
                }
            }
            finally
            {
                _isRefreshingResults = false;
            }
            if (!preservedExplicitSelection)
            {
                _explicitSelectionDisplayName = null;
            }
            ResultCountText.Text = $"{items.Count} 项";
            UpdateResultActionAvailability();
        }

        private void QueryBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshResults();

        private void QueryBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            int direction = e.Key switch
            {
                Key.Up => -1,
                Key.Down => 1,
                _ => 0
            };
            if (direction != 0)
            {
                int nextIndex = GetNextSelectionIndex(
                    ResultsList.SelectedIndex,
                    ResultsList.Items.Count,
                    direction);
                if (nextIndex >= 0)
                {
                    ResultsList.SelectedIndex = nextIndex;
                    ResultsList.ScrollIntoView(ResultsList.Items[nextIndex]);
                    e.Handled = true;
                }
                return;
            }

            e.Handled = TryExecuteSelectedAction(
                ResolveKeyboardAction(e.Key, Keyboard.Modifiers, e.IsRepeat));
        }

        private void ResultsList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = TryExecuteSelectedAction(
                ResolveKeyboardAction(e.Key, Keyboard.Modifiers, e.IsRepeat));
        }

        private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isRefreshingResults)
            {
                _explicitSelectionDisplayName = Selected?.DisplayName;
            }
            if (ResultActionPanel is not null)
            {
                UpdateResultActionAvailability();
            }
        }

        private void UpdateResultActionAvailability()
        {
            ResultActionPanel.IsEnabled = Selected is not null;
        }

        private void ViewSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded)
            {
                RefreshResults();
            }
        }

        private void Locate_Click(object sender, RoutedEventArgs e)
        {
            _ = TryExecuteSelectedAction(DesktopSearchKeyboardAction.Locate);
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            _ = TryExecuteSelectedAction(DesktopSearchKeyboardAction.Open);
        }

        private void Reveal_Click(object sender, RoutedEventArgs e)
        {
            _ = TryExecuteSelectedAction(DesktopSearchKeyboardAction.Reveal);
        }

        private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            bool isResultItem =
                e.OriginalSource is DependencyObject source &&
                ItemsControl.ContainerFromElement(ResultsList, source) is ListBoxItem;
            if (!ShouldLocateFromResultDoubleClick(
                    e.ChangedButton,
                    isResultItem))
            {
                return;
            }

            e.Handled = true;
            Locate_Click(sender, e);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private bool TryExecuteSelectedAction(DesktopSearchKeyboardAction action)
        {
            SearchListItem? selected = Selected;
            if (selected == null || action == DesktopSearchKeyboardAction.None)
            {
                return false;
            }

            switch (action)
            {
                case DesktopSearchKeyboardAction.Locate:
                    _mainWindow.LocateDesktopSearchResult(selected.Result.DisplayName);
                    break;
                case DesktopSearchKeyboardAction.Open:
                    _mainWindow.OpenDesktopSearchResult(selected.Result.Location);
                    break;
                case DesktopSearchKeyboardAction.Reveal:
                    _mainWindow.RevealDesktopSearchResult(selected.Result.Location);
                    break;
                default:
                    return false;
            }
            if (ShouldCloseAfterAction(action))
            {
                Close();
            }
            return true;
        }

        internal static bool ShouldCloseAfterAction(DesktopSearchKeyboardAction action) =>
            action == DesktopSearchKeyboardAction.Locate;

        internal static bool ShouldLocateFromResultDoubleClick(
            MouseButton changedButton,
            bool isResultItem) =>
            changedButton == MouseButton.Left && isResultItem;

        internal static DesktopSearchKeyboardAction ResolveKeyboardAction(
            Key key,
            ModifierKeys modifiers,
            bool isRepeat)
        {
            if (key != Key.Enter || isRepeat)
            {
                return DesktopSearchKeyboardAction.None;
            }

            return modifiers switch
            {
                ModifierKeys.None => DesktopSearchKeyboardAction.Locate,
                ModifierKeys.Control => DesktopSearchKeyboardAction.Open,
                ModifierKeys.Shift => DesktopSearchKeyboardAction.Reveal,
                _ => DesktopSearchKeyboardAction.None
            };
        }

        internal static bool ShouldFocusQueryFromKeyboard(
            Key key,
            ModifierKeys modifiers,
            bool isRepeat) =>
            key == Key.F &&
            modifiers == ModifierKeys.Control &&
            !isRepeat;

        internal static int GetNextSelectionIndex(
            int currentIndex,
            int itemCount,
            int direction)
        {
            if (itemCount <= 0)
            {
                return -1;
            }
            if (currentIndex < 0 || currentIndex >= itemCount)
            {
                return direction < 0 ? itemCount - 1 : 0;
            }
            return Math.Clamp(currentIndex + direction, 0, itemCount - 1);
        }

        internal static int ResolveRefreshedSelectionIndex(
            IReadOnlyList<string> displayNames,
            string? explicitSelectionDisplayName)
        {
            if (displayNames.Count == 0)
            {
                return -1;
            }
            if (explicitSelectionDisplayName is null)
            {
                return 0;
            }

            for (int index = 0; index < displayNames.Count; index++)
            {
                if (displayNames[index].Equals(
                        explicitSelectionDisplayName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }
            return 0;
        }
    }
}
