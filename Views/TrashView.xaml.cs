using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SaveSpace.Models;
using SaveSpace.Services;
using SaveSpace.Views.Dialogs;

namespace SaveSpace.Views
{
    public partial class TrashView : UserControl, IRefreshable
    {
        private readonly MainWindow _host;

        public TrashView(MainWindow host)
        {
            InitializeComponent();
            _host = host;
            Refresh();
        }

        public void Refresh()
        {
            var retention = AppServices.Settings.TrashRetentionDays;
            var purged = AppServices.Library.PurgeExpiredTrash(retention);
            if (purged > 0)
                AppServices.Notifications.Add("Bewaartermijn verlopen", $"{purged} verlopen item(s) definitief verwijderd.", "\uE74D", "Brush.Amber");
            var items = AppServices.Library.GetTrash();
            TrashList.ItemsSource = items;
            CountText.Text = items.Count == 0 ? "" : items.Count == 1 ? "1 item in de prullenbak" : $"{items.Count} items in de prullenbak";
            EmptyPanel.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            TrashList.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            EmptyTrashButton.IsEnabled = items.Count > 0;
            EmptyTrashButton.Opacity = items.Count > 0 ? 1 : 0.4;
            SubtitleText.Text = retention == 0
                ? items.Count == 0 ? "Verwijderde items blijven hier tot je ze terugzet of definitief wist." : $"{items.Count} items wachten op een beslissing. Terugzetten kan altijd."
                : $"Items worden na {retention} dagen definitief verwijderd. Zet ze voor die tijd terug als je ze wilt bewaren. {items.Count} item(s) staan nu in de prullenbak.";
        }

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: LibraryItem item }) return;
            AppServices.Library.Restore(item.Id);
            AppServices.Notifications.Add("Teruggezet", item.Title, "\uE777", "Brush.Sage");
            Refresh();
            _host.RefreshHeader();
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: LibraryItem item }) return;
            if (!ConfirmDialog.Show(Window.GetWindow(this), "Definitief verwijderen?",
                    $"«{item.Title}» wordt definitief uit je bibliotheek verwijderd. Dit kan niet ongedaan worden gemaakt. Originelen buiten de opslagmap blijven staan.",
                    "Definitief verwijderen", true, "\uE74D")) return;
            AppServices.Library.DeletePermanently(item.Id);
            Refresh();
            _host.RefreshHeader();
        }

        private void EmptyTrash_Click(object sender, RoutedEventArgs e)
        {
            var count = AppServices.Library.GetTrash().Count;
            if (count == 0) return;
            if (!ConfirmDialog.Show(Window.GetWindow(this), "Prullenbak legen?",
                    $"Alle {count} items worden definitief verwijderd. Bestanden in de SaveSpace-opslag worden gewist; je originele bestanden buiten de app blijven staan.",
                    "Alles verwijderen", true, "\uE74D")) return;
            AppServices.Library.EmptyTrash();
            Refresh();
            _host.RefreshHeader();
        }
    }
}
