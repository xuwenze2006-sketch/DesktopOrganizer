namespace DesktopOrganizer
{
    public partial class DesktopSearchWindow : Window
    {
        private readonly MainWindow _mainWindow;

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

        private void RefreshResults()
        {
            DesktopSmartView view = (ViewSelector.SelectedItem as SmartViewChoice)?.View ??
                                    DesktopSmartView.All;
            List<SearchListItem> items = _mainWindow
                .SearchLoadedDesktopItems(QueryBox.Text, view)
                .Select(result => new SearchListItem(result))
                .ToList();
            ResultsList.ItemsSource = items;
            ResultsList.SelectedIndex = items.Count > 0 ? 0 : -1;
            ResultCountText.Text = $"{items.Count} 项";
        }

        private void QueryBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshResults();

        private void ViewSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded)
            {
                RefreshResults();
            }
        }

        private void Locate_Click(object sender, RoutedEventArgs e)
        {
            if (Selected != null)
            {
                _mainWindow.LocateDesktopSearchResult(Selected.Result.DisplayName);
            }
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            if (Selected != null)
            {
                _mainWindow.OpenDesktopSearchResult(Selected.Result.Location);
            }
        }

        private void Reveal_Click(object sender, RoutedEventArgs e)
        {
            if (Selected != null)
            {
                _mainWindow.RevealDesktopSearchResult(Selected.Result.Location);
            }
        }

        private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
            Locate_Click(sender, e);

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
