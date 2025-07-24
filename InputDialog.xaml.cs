using System.Windows;

namespace LogFileViewer
{
    public partial class InputDialog : Window
    {
        public string InputText { get; private set; }

        public InputDialog(string prompt, string title = "Input", string defaultValue = "")
        {
            InitializeComponent();
            Title = title;
            PromptText.Text = prompt;
            InputTextBox.Text = defaultValue;
            InputTextBox.SelectAll();
            InputTextBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            InputText = InputTextBox.Text;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        public static string ShowDialog(string prompt, string title = "Input", string defaultValue = "", Window owner = null)
        {
            var dialog = new InputDialog(prompt, title, defaultValue);
            if (owner != null)
                dialog.Owner = owner;
            
            if (dialog.ShowDialog() == true)
                return dialog.InputText;
            
            return null;
        }
    }
}