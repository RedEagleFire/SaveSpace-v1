using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SaveSpace.Views.Dialogs
{
    public partial class AiResultWindow : Window
    {
        private readonly Action<string>? _insert;
        private readonly Action<string>? _replace;

        public AiResultWindow(string label, string result, Action<string>? insert, Action<string>? replace)
        {
            InitializeComponent();
            TitleText.Text = label;
            ResultText.Text = result;
            _insert = insert;
            _replace = replace;
            if (replace == null) ReplaceButton.Visibility = Visibility.Collapsed;
            if (insert == null) InsertButton.Visibility = Visibility.Collapsed;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(ResultText.Text);
                CopyButtonFeedback(sender as Button);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Kopiëren", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CopyButtonFeedback(Button? button)
        {
            if (button == null) return;
            var original = button.Content;
            button.Content = "Gekopieerd";
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.4) };
            timer.Tick += (_, _) => { button.Content = original; timer.Stop(); };
            timer.Start();
        }

        private void Insert_Click(object sender, RoutedEventArgs e)
        {
            _insert?.Invoke(ResultText.Text);
            DialogResult = true;
        }

        private void Replace_Click(object sender, RoutedEventArgs e)
        {
            _replace?.Invoke(ResultText.Text);
            DialogResult = true;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
