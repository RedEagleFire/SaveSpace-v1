using System.Windows;

namespace SaveSpace.Views.Dialogs
{
    public partial class ProgressDialog : Window
    {
        public ProgressDialog(string title, string detail, bool indeterminate = false)
        {
            InitializeComponent();
            TitleText.Text = title;
            DetailText.Text = detail;
            TaskProgress.IsIndeterminate = indeterminate;
        }

        public void UpdateProgress(int current, int total, string detail)
        {
            DetailText.Text = detail;
            TaskProgress.IsIndeterminate = false;
            TaskProgress.Value = total <= 0 ? 0 : Math.Clamp(current * 100.0 / total, 0, 100);
        }
    }
}
