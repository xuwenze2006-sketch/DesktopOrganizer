namespace DesktopOrganizer
{
    /// <summary>用于"新建分组""重命名分组"的简单文本输入对话框。</summary>
    public partial class SimpleInputDialog : Window
    {
        public string ResultText { get; private set; } = string.Empty;

        public SimpleInputDialog(string prompt, string defaultValue = "")
        {
            InitializeComponent();
            PromptText.Text = prompt;
            InputBox.Text = defaultValue;
            Loaded += (_, _) =>
            {
                InputBox.Focus();
                InputBox.SelectAll();
            };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            ResultText = InputBox.Text.Trim();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
