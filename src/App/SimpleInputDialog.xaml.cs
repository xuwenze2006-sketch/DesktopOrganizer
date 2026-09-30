namespace DesktopOrganizer
{
    /// <summary>用于"新建分组""重命名分组"的简单文本输入对话框。</summary>
    public partial class SimpleInputDialog : Window
    {
        public string ResultText { get; private set; } = string.Empty;
        public bool IsOptionChecked => OptionCheckBox.IsChecked == true;
        private bool _allowEmpty;
        private bool _hasCompleted;

        // Naming requires content; tag editing explicitly allows clearing all tags.
        public bool AllowEmpty
        {
            get => _allowEmpty;
            set
            {
                _allowEmpty = value;
                UpdateInputState();
            }
        }

        private void InputBox_TextChanged(object sender, TextChangedEventArgs e) =>
            UpdateInputState();

        private void UpdateInputState()
        {
            // TextChanged can run while InitializeComponent is still building the controls.
            if (OkButton is null || ValidationText is null) return;
            bool valid = AllowEmpty || !string.IsNullOrWhiteSpace(InputBox.Text);
            OkButton.IsEnabled = valid;
            ValidationText.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        }

        public void ConfigureOption(string label, bool isChecked = false)
        {
            OptionCheckBox.Content = label;
            OptionCheckBox.IsChecked = isChecked;
            OptionCheckBox.Visibility = Visibility.Visible;
        }

        public SimpleInputDialog(string prompt, string defaultValue = "")
        {
            InitializeComponent();
            PromptText.Text = prompt;
            InputBox.Text = defaultValue;
            UpdateInputState();
            Closed += (_, _) => _hasCompleted = true;
            Loaded += (_, _) =>
            {
                InputBox.Focus();
                InputBox.SelectAll();
            };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (_hasCompleted) return;
            if (!AllowEmpty && string.IsNullOrWhiteSpace(InputBox.Text))
            {
                UpdateInputState();
                InputBox.Focus();
                return;
            }
            _hasCompleted = true;
            ResultText = InputBox.Text.Trim();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
