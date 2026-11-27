using System.Windows;
using System.Windows.Input;

namespace SaveSpace.Views.Dialogs
{
    public partial class ConfirmDialog : Window
    {
        public ConfirmDialog(string title, string message, string confirmLabel = "Verwijderen", bool danger = true,
            string? icon = null)
        {
            InitializeComponent();
            TitleText.Text = title;
            MessageText.Text = message;
            ConfirmButton.Content = confirmLabel;
            if (!danger)
            {
                ConfirmButton.Style = (Style)FindResource("Btn.Primary");
                IconBadge.Background = (System.Windows.Media.Brush)FindResource("Brush.AccentSoft");
                IconText.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Accent");
            }
            if (!string.IsNullOrEmpty(icon)) IconText.Text = icon;
        }

        public static bool Show(Window? owner, string title, string message, string confirmLabel = "Verwijderen",
            bool danger = true, string? icon = null)
        {
            var dialog = new ConfirmDialog(title, message, confirmLabel, danger, icon);
            if (owner != null) dialog.Owner = owner;
            return dialog.ShowDialog() == true;
        }

        private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
