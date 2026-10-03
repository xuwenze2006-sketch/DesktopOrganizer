namespace DesktopOrganizer;

public partial class DesktopPetPicker
{
    private void ChangeLibrary(Action change, string message)
    {
        if (_importCancellation is not null) return;
        try
        {
            change();
            RefreshCards();
            LibraryNotice.Text = message;
            LibraryChanged?.Invoke();
        }
        catch (Exception ex) when (DesktopPetLibrary.IsAssetError(ex))
        {
            LibraryNotice.Text = "未能保存：" + ex.Message;
        }
    }

    private void Rename(Card card)
    {
        if (_importCancellation is not null) return;
        var dialog = new SimpleInputDialog("为这个素材起个名字（1–40 字）：", card.Name)
        { Title = "修改素材名称", Owner = Window.GetWindow(this) };
        dialog.InputBox.MaxLength = 40;
        if (dialog.ShowDialog() == true)
            ChangeLibrary(() => DesktopPetLibrary.Shared.SetDisplayName(card.Id, dialog.ResultText), "名称已保存；原名和素材文件保留。");
    }

    private void Card_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F2 || sender is not ListBox list || KeyboardCard(list, e.OriginalSource) is not Card card) return;
        e.Handled = true;
        Rename(card);
    }
    internal static Card? KeyboardCard(ListBox list, object source) =>
        source is FrameworkElement { DataContext: Card focused } ? focused : list.SelectedItem as Card;
    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Card card }) return;
        e.Handled = true;
        ChangeLibrary(() => DesktopPetLibrary.Shared.SetFavorite(card.Id, !card.Favorite),
            card.Favorite ? "已取消收藏。" : "已收藏，可在「收藏」中找到。");
    }
    private void SetCardHidden(Card card, bool hidden) => ChangeLibrary(
        () => DesktopPetLibrary.Shared.SetRemoved(card.Id, hidden),
        hidden ? "已隐藏“" + card.Name + "”；可在「已隐藏」中恢复，当前搭配保持不变。" : "已恢复“" + card.Name + "”。");
    private void RestoreCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Card card }) return;
        e.Handled = true;
        SetCardHidden(card, false);
    }
    internal ContextMenu CreateCardMenu(Card card)
    {
        var menu = new ContextMenu();
        void Add(string text, Action action, bool enabled = true, string hint = "")
        {
            var item = new MenuItem { Header = text, IsEnabled = enabled && _importCancellation is null, InputGestureText = hint };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Add("改名…", () => Rename(card), hint: "F2");
        Add("恢复原名", () => ChangeLibrary(() => DesktopPetLibrary.Shared.SetDisplayName(card.Id, null), "已恢复素材原名。"),
            DesktopPetLibrary.Shared.ReadSnapshot().DisplayNames.ContainsKey(card.Id));
        Add(card.FavoriteHint, () => ChangeLibrary(() => DesktopPetLibrary.Shared.SetFavorite(card.Id, !card.Favorite),
            card.Favorite ? "已取消收藏。" : "已收藏。"));
        menu.Items.Add(new Separator());
        Add(card.Hidden ? "恢复到素材库" : "隐藏素材", () => SetCardHidden(card, !card.Hidden));
        return menu;
    }
    private void CardMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Card card } target) return;
        e.Handled = true;
        var menu = CreateCardMenu(card);
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
    private void Card_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: Card card } target) return;
        e.Handled = true;
        var menu = CreateCardMenu(card);
        menu.PlacementTarget = target;
        menu.IsOpen = true;
    }
    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_importCancellation is not null) return;
        bool scene = LibraryTabs.SelectedIndex == 1;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = scene ? "添加背景图片" : "添加角色图片或动画",
            Filter = scene ? "PNG 背景|*.png" : "角色素材（PNG 或动画清单）|*.png;*.json|PNG 静态角色|*.png|动画清单|*.json"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        using var cancellation = new CancellationTokenSource();
        _importCancellation = cancellation;
        LibraryActions.IsEnabled = LibraryTabs.IsEnabled = ApplyButton.IsEnabled = false;
        PreviewButton.IsEnabled = false;
        LibraryNotice.Text = "正在导入素材…关闭窗口可取消尚未完成的导入。";
        try
        {
            var asset = await DesktopPetLibrary.Shared.ImportAsync(dialog.FileName, scene ? "scene" : "character", cancellation.Token);
            if (!IsLoaded) return;
            if (scene) { _sceneId = asset.Id; _sceneChosen = true; ShowScene.IsChecked = true; }
            else _characterId = asset.Id;
            SearchBox.Clear(); LibraryFilter.SelectedIndex = 0;
            RefreshCards();
            LibraryNotice.Text = "已添加“" + asset.Name + "”；点击应用更新桌面搭配。";
            LibraryChanged?.Invoke();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { LibraryNotice.Text = "已取消导入。"; }
        catch (Exception ex) when (DesktopPetLibrary.IsAssetError(ex))
        { if (IsLoaded) LibraryNotice.Text = "无法添加素材：" + ex.Message; }
        finally
        {
            _importCancellation = null;
            LibraryActions.IsEnabled = LibraryTabs.IsEnabled = true;
            if (IsLoaded) UpdatePreview();
        }
    }
}
