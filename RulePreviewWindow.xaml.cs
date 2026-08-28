namespace DesktopOrganizer
{
    internal enum RulePreviewPurpose
    {
        ConfirmPreview,
        ExecuteOnce
    }

    public partial class RulePreviewWindow : Window
    {
        private readonly UserRulePreviewView _preview;
        private readonly RulePreviewPurpose _purpose;

        private sealed record PreviewRow(
            string DisplayName,
            string Status,
            string Target,
            string Explanation,
            string ConflictText);

        internal RulePreviewWindow(
            UserRulePreviewView preview,
            RulePreviewPurpose purpose)
        {
            _preview = preview ?? throw new ArgumentNullException(nameof(preview));
            _purpose = purpose;
            InitializeComponent();

            RuleTitleText.Text = $"规则：{preview.RuleName}";
            SummaryText.Text = $"当前桌面 {preview.Items.Count} 项 · 条件命中 {preview.MatchCount} 项 · 冲突 {preview.ConflictCount} 项";
            PlanCountText.Text = $"执行计划：{preview.ExecutionPlan.Actions.Count} 项";
            PreviewGrid.ItemsSource = preview.Items.Select(item => new PreviewRow(
                item.DisplayName,
                item.Conflict != null
                    ? "有冲突"
                    : item.WillExecute ? "将执行" : "未命中",
                item.Target,
                item.Explanation,
                item.Conflict ?? string.Empty)).ToList();

            if (purpose == RulePreviewPurpose.ConfirmPreview)
            {
                Title = "确认规则预览";
                PurposeText.Text = "请逐项检查命中原因、目标和冲突。只有点击“确认此预览”才会把规则推进到已预览状态；关闭或取消不会调用确认接口。";
                FooterText.Text = "确认预览本身不会执行规则或修改任何布局数据。";
                PrimaryButton.Content = "确认此预览";
            }
            else
            {
                Title = "确认执行一次";
                PurposeText.Text = "这是执行前根据当前桌面重新生成的最新预览。请再次核对；点击“执行一次”后，只有执行计划中的无冲突项目会被提交。";
                FooterText.Text = preview.ExecutionPlan.Actions.Count == 0
                    ? "当前没有可执行项目；请取消并调整规则或等待桌面内容变化。"
                    : "点击执行后将应用上方当前计划，不复用以前的预览。";
                PrimaryButton.Content = "执行一次…";
                PrimaryButton.IsEnabled = preview.ExecutionPlan.Actions.Count > 0;
            }
        }

        private void Primary_Click(object sender, RoutedEventArgs e)
        {
            if (_purpose == RulePreviewPurpose.ExecuteOnce)
            {
                int actionCount = _preview.ExecutionPlan.Actions.Count;
                if (actionCount == 0)
                {
                    return;
                }
                if (MessageBox.Show(
                        this,
                        $"按当前最新预览执行 {actionCount} 项虚拟整理动作？\n\n" +
                        "冲突项已排除。此操作不会移动、重命名或删除真实文件。",
                        "执行规则一次",
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Question) != MessageBoxResult.OK)
                {
                    return;
                }
            }

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
