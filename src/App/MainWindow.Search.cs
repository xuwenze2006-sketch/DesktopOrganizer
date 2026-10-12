// 当前桌面快照内的快速搜索与定位
namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private readonly DesktopSearchIndexCache _desktopSearchIndexCache = new();

        private void DesktopSearchButton_Click(object sender, RoutedEventArgs e)
        {
            TryShowDesktopSearchDialog();
        }

        private bool TryShowDesktopSearchDialog()
        {
            if (!TryReserveDesktopSearchDialog(ref _desktopSearchDialogReservation))
            {
                return false;
            }

            return ShowReservedDesktopSearchDialog();
        }

        private bool ShowReservedDesktopSearchDialog()
        {
            try
            {
                var dialog = new DesktopSearchWindow(this);
                if (_isAttachedToDesktop)
                {
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
                else
                {
                    dialog.Owner = this;
                }
                dialog.ShowDialog();
                return true;
            }
            finally
            {
                _desktopSearchIndexCache.Clear();
                ReleaseDesktopSearchDialog(ref _desktopSearchDialogReservation);
            }
        }

        internal static bool TryReserveDesktopSearchDialog(ref int reservation) =>
            Interlocked.CompareExchange(ref reservation, 1, 0) == 0;

        internal static void ReleaseDesktopSearchDialog(ref int reservation) =>
            Interlocked.Exchange(ref reservation, 0);

        internal IReadOnlyList<DesktopSearchResult> SearchLoadedDesktopItems(
            string? text,
            DesktopSmartView view)
        {
            DesktopSearchIndex index = _desktopSearchIndexCache.GetOrBuild(
                _desktopItems,
                _desktopCategories,
                _appLayout);
            return index.Search(new DesktopSearchRequest(
                text,
                view,
                DateTimeOffset.UtcNow));
        }

        internal bool LocateDesktopSearchResult(string displayName) =>
            LocateDesktopSearchResult(displayName, out _);

        internal bool LocateDesktopSearchResult(string displayName, out string? failureMessage)
        {
            failureMessage = null;
            if (_organizerPaused)
            {
                failureMessage = "整理已暂停，无法在桌面定位。\n可先打开文件或在资源管理器中查看；要定位，请关闭搜索，点击工具条“继续”，再打开搜索重试。";
                return false;
            }
            if (!_desktopItems.ContainsKey(displayName))
            {
                failureMessage = $"“{displayName}”已不在当前桌面。\n请更换搜索条件，或关闭搜索并刷新桌面后重试。";
                StatusText.Text = $"“{displayName}”已不在当前桌面，请刷新后重试";
                return false;
            }

            GroupInfo? group = _appLayout.Groups.FirstOrDefault(candidate =>
                candidate.ItemNames.Contains(displayName, StringComparer.OrdinalIgnoreCase));
            bool isEntry = group != null && IsLightDesktopEntry(group);
            bool layoutChanged = group?.IsCollapsed == true && !isEntry;
            if (group != null && !isEntry)
            {
                if (!TryExpandGroupWithoutOverlap(group))
                {
                    ReportGroupExpansionNoSpace(group.Name);
                    failureMessage = $"桌面空间不足，无法展开“{group.Name}”。\n请先腾出空白区域，再尝试定位。";
                    return false;
                }
            }

            _selectedItemNames.Clear();
            _selectedItemNames.Add(displayName);
            _groupRangeSelectionAnchor = group == null
                ? null
                : new GroupRangeSelectionAnchor(group.Id, displayName);
            RebuildDesktopIcons();
            if (isEntry) OpenLightDesktopDrawer(group!);
            if (layoutChanged)
            {
                _lastSmartLayoutSnapshot = null;
                UndoSmartLayoutButton.IsEnabled = false;
                SaveLayout();
            }

            if (group != null)
            {
                List<string> orderedNames = GetSortedGroupItemNames(group, _desktopItems).ToList();
                int index = orderedNames.FindIndex(name =>
                    name.Equals(displayName, StringComparison.OrdinalIgnoreCase));
                double offset = GetGroupSearchVerticalOffset(group, index);
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    if (_groupItemPanels.TryGetValue(group.Id, out VirtualizingGroupPanel? panel))
                    {
                        panel.SetVerticalOffset(offset);
                    }
                }));
            }

            StatusText.Text = group == null
                ? $"已在桌面高亮“{displayName}”"
                : $"已展开“{group.Name}”并高亮“{displayName}”";
            return true;
        }

        private double GetGroupSearchVerticalOffset(GroupInfo group, int itemIndex)
        {
            int columns = Math.Max(1, GetDesiredGroupColumnCount(group));
            return Math.Max(0, itemIndex / columns) * GetGroupedIconRowHeight();
        }

        internal void OpenDesktopSearchResult(string location) => OpenDesktopItem(location);

        internal void RevealDesktopSearchResult(string location)
        {
            if (ShellItemLocation.TryDecode(location, out _, out _))
            {
                OpenDesktopItem(location);
                return;
            }
            RevealInExplorer(location);
        }
    }
}
