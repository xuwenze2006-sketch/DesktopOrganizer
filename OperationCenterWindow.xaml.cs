namespace DesktopOrganizer
{
    public partial class OperationCenterWindow : Window
    {
        private readonly Func<FileOperationJournalData> _journalProvider;
        private readonly Func<string?> _protectionWarningProvider;
        private readonly DispatcherTimer _refreshTimer;

        private sealed record OperationRow(
            string TimeText,
            string TimeDetails,
            string KindText,
            string ItemText,
            string SourceText,
            string TargetText,
            string StateText,
            string ReversibilityText,
            string ErrorText);

        internal OperationCenterWindow(
            FileOperationJournalData journal,
            string? protectionWarning = null)
            : this(() => journal, () => protectionWarning)
        {
        }

        internal OperationCenterWindow(
            Func<FileOperationJournalData> journalProvider,
            Func<string?> protectionWarningProvider)
        {
            _journalProvider = journalProvider ??
                throw new ArgumentNullException(nameof(journalProvider));
            _protectionWarningProvider = protectionWarningProvider ??
                throw new ArgumentNullException(nameof(protectionWarningProvider));
            InitializeComponent();

            RefreshView();
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();
            Closed += (_, _) => _refreshTimer.Stop();
        }

        private void RefreshTimer_Tick(object? sender, EventArgs e) => RefreshView();

        private void RefreshView()
        {
            FileOperationJournalData journal = _journalProvider() ??
                new FileOperationJournalData();

            List<FileOperationJournalEntry> entries = journal.Entries?
                .Where(entry => entry != null)
                .OrderByDescending(GetSortTime)
                .ThenByDescending(entry => entry.RequestedUtc)
                .ToList() ?? new List<FileOperationJournalEntry>();
            HashSet<string> successfullyUndoneEntryIds = entries
                .Where(entry =>
                    entry.Kind == FileOperationJournalKind.UndoMove &&
                    entry.State == FileOperationJournalState.Succeeded &&
                    !string.IsNullOrWhiteSpace(entry.UndoOfEntryId))
                .Select(entry => entry.UndoOfEntryId!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            JournalGrid.ItemsSource = entries
                .Select(entry => CreateRow(entry, successfullyUndoneEntryIds))
                .ToList();

            int succeededCount = entries.Count(entry =>
                entry.State == FileOperationJournalState.Succeeded);
            int attentionCount = entries.Count(entry => entry.State is
                FileOperationJournalState.Interrupted or
                FileOperationJournalState.Uncertain);
            int failedOrCanceledCount = entries.Count(entry => entry.State is
                FileOperationJournalState.Failed or
                FileOperationJournalState.Canceled);
            SummaryText.Text =
                $"共 {entries.Count} 项 · 成功 {succeededCount} · " +
                $"失败或取消 {failedOrCanceledCount} · 需人工核对 {attentionCount}";
            EmptyText.Visibility = entries.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            string? protectionWarning = _protectionWarningProvider();
            if (!string.IsNullOrWhiteSpace(protectionWarning))
            {
                ProtectionWarningText.Text = protectionWarning.Trim();
                ProtectionWarningBorder.Visibility = Visibility.Visible;
                EmptyText.Text = "操作记录当前无法安全读取，不能判断是否存在历史记录。";
            }
            else
            {
                ProtectionWarningText.Text = string.Empty;
                ProtectionWarningBorder.Visibility = Visibility.Collapsed;
                EmptyText.Text = "尚无真实文件操作记录。";
            }
        }

        private static OperationRow CreateRow(
            FileOperationJournalEntry entry,
            HashSet<string> successfullyUndoneEntryIds)
        {
            return new OperationRow(
                FormatPrimaryTime(entry),
                FormatTimeDetails(entry),
                FormatKind(entry.Kind),
                string.IsNullOrWhiteSpace(entry.DisplayName) ? "（未命名项目）" : entry.DisplayName,
                string.IsNullOrWhiteSpace(entry.SourcePath) ? "—" : entry.SourcePath,
                string.IsNullOrWhiteSpace(entry.DestinationPath) ? "—" : entry.DestinationPath,
                FormatState(entry.State),
                FormatReversibility(entry, successfullyUndoneEntryIds),
                FormatError(entry));
        }

        private static DateTime GetSortTime(FileOperationJournalEntry entry) =>
            entry.CompletedUtc ?? entry.StartedUtc ?? entry.RequestedUtc;

        private static string FormatPrimaryTime(FileOperationJournalEntry entry)
        {
            DateTime time = GetSortTime(entry).ToLocalTime();
            string prefix = entry.CompletedUtc.HasValue
                ? "完成"
                : entry.StartedUtc.HasValue ? "开始" : "请求";
            return $"{prefix} {time:yyyy-MM-dd HH:mm:ss}";
        }

        private static string FormatTimeDetails(FileOperationJournalEntry entry)
        {
            var parts = new List<string>
            {
                $"请求：{entry.RequestedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
            };
            if (entry.StartedUtc.HasValue)
            {
                parts.Add($"开始：{entry.StartedUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            }
            if (entry.CompletedUtc.HasValue)
            {
                parts.Add($"完成：{entry.CompletedUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            }
            return string.Join("\n", parts);
        }

        private static string FormatKind(FileOperationJournalKind kind) => kind switch
        {
            FileOperationJournalKind.MoveIntoFolder => "移入真实文件夹",
            FileOperationJournalKind.MoveToRecycleBin => "移到回收站",
            FileOperationJournalKind.UndoMove => "撤销移动",
            FileOperationJournalKind.EmptyRecycleBin => "清空回收站",
            _ => kind.ToString()
        };

        private static string FormatState(FileOperationJournalState state) => state switch
        {
            FileOperationJournalState.Queued => "排队中",
            FileOperationJournalState.Running => "执行中",
            FileOperationJournalState.Succeeded => "已成功",
            FileOperationJournalState.Failed => "失败",
            FileOperationJournalState.Canceled => "已取消",
            FileOperationJournalState.Interrupted => "已中断",
            FileOperationJournalState.Uncertain => "状态不确定",
            _ => state.ToString()
        };

        private static string FormatReversibility(
            FileOperationJournalEntry entry,
            HashSet<string> successfullyUndoneEntryIds)
        {
            return entry.State switch
            {
                FileOperationJournalState.Failed or FileOperationJournalState.Canceled =>
                    "不可自动重试",
                FileOperationJournalState.Interrupted =>
                    "不可自动重试，需人工核对",
                FileOperationJournalState.Uncertain =>
                    "需人工核对，禁止自动重试",
                FileOperationJournalState.Queued or FileOperationJournalState.Running =>
                    "处理中，请勿重复发起",
                FileOperationJournalState.Succeeded => FormatSuccessfulReversibility(
                    entry,
                    successfullyUndoneEntryIds),
                _ => "需人工核对"
            };
        }

        private static string FormatSuccessfulReversibility(
            FileOperationJournalEntry entry,
            HashSet<string> successfullyUndoneEntryIds) => entry.Kind switch
        {
            FileOperationJournalKind.MoveIntoFolder when
                successfullyUndoneEntryIds.Contains(entry.Id) =>
                "已成功撤销，不可再次撤销",
            FileOperationJournalKind.MoveIntoFolder =>
                "可完整撤销（执行时重新核验）",
            FileOperationJournalKind.MoveToRecycleBin =>
                "部分可撤销（需 Windows 回收站）",
            FileOperationJournalKind.EmptyRecycleBin =>
                "不可撤销",
            FileOperationJournalKind.UndoMove =>
                "不可再次撤销",
            _ => "需人工核对"
        };

        private static string FormatError(FileOperationJournalEntry entry)
        {
            string code = entry.ErrorCode?.Trim() ?? string.Empty;
            string message = entry.ErrorMessage?.Trim() ?? string.Empty;
            if (code.Length == 0)
            {
                return message.Length == 0 ? "—" : message;
            }
            if (message.Length == 0)
            {
                return code;
            }
            return $"{code}：{message}";
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
