using System.Windows;
using System.Windows.Input;

namespace SaveSpace.Views.Dialogs
{
    public partial class PromptDialog : Window
    {
        public string Value => ValueBox.Text.Trim();

        public PromptDialog(string title, string hint, string value = "")
        {
            InitializeComponent();
            TitleText.Text = title;
            HintText.Text = hint;
            ValueBox.Text = value;
            Loaded += (_, _) =>
            {
                ValueBox.Focus();
                ValueBox.SelectAll();
            };
        }

        public static string? Show(Window? owner, string title, string hint, string value = "")
        {
            var dialog = new PromptDialog(title, hint, value);
            if (owner != null) dialog.Owner = owner;
            return dialog.ShowDialog() == true ? dialog.Value : null;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ValueBox.Text))
            {
                ErrorText.Text = "Vul een waarde in.";
                ErrorText.Visibility = Visibility.Visible;
                ValueBox.Focus();
                return;
            }
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) DialogResult = false;
            else if (e.Key == Key.Enter)
            {
                Ok_Click(sender, e);
                e.Handled = true;
            }
        }
    }
}
