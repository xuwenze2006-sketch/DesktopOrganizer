// 当前桌面快照内的快速搜索与定位
namespace DesktopOrganizer
{
    public partial class MainWindow
    {
        private void DesktopSearchButton_Click(object sender, RoutedEventArgs e)
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
        }

        internal IReadOnlyList<DesktopSearchResult> SearchLoadedDesktopItems(
            string? text,
            DesktopSmartView view)
        {
            var metadata = _desktopItems.Keys.ToDictionary(
                name => name,
                name => new DesktopSearchItemMetadata(
                    _appLayout.ItemTags.TryGetValue(name, out List<string>? tags)
                        ? tags
                        : Array.Empty<string>(),
                    TryReadMetadataTime(_appLayout.ItemFirstSeenUtcTicks, name),
                    TryReadMetadataTime(_appLayout.ItemLastMovedUtcTicks, name),
                    _appLayout.InboxItems.ContainsKey(name)),
                StringComparer.OrdinalIgnoreCase);
            DesktopSearchIndex index = DesktopSearchIndex.Build(
                _desktopItems,
                _desktopCategories,
                _appLayout.Groups,
                metadata);
            return index.Search(new DesktopSearchRequest(
                text,
                view,
                DateTimeOffset.UtcNow));
        }

        internal void LocateDesktopSearchResult(string displayName)
        {
            if (!_desktopItems.ContainsKey(displayName))
            {
                StatusText.Text = $"“{displayName}”已不在当前桌面，请刷新后重试";
                return;
            }

            GroupInfo? group = _appLayout.Groups.FirstOrDefault(candidate =>
                candidate.ItemNames.Contains(displayName, StringComparer.OrdinalIgnoreCase));
            bool layoutChanged = group?.IsCollapsed == true;
            if (group != null)
            {
                group.IsCollapsed = false;
            }

            _selectedItemNames.Clear();
            _selectedItemNames.Add(displayName);
            RebuildDesktopIcons();
            if (layoutChanged)
            {
                SaveLayout();
            }

            if (group != null)
            {
                List<string> orderedNames = GetSortedGroupItemNames(group, _desktopItems).ToList();
                int index = orderedNames.FindIndex(name =>
                    name.Equals(displayName, StringComparison.OrdinalIgnoreCase));
                int columns = Math.Max(1, GetDesiredGroupColumnCount(group));
                double offset = Math.Max(0, index / columns) * GroupedIconRowHeight;
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

        private static DateTimeOffset? TryReadMetadataTime(
            IReadOnlyDictionary<string, long> source,
            string name)
        {
            if (!source.TryGetValue(name, out long ticks) ||
                ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
            {
                return null;
            }
            return new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc));
        }
    }
}
