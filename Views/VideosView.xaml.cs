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
    public partial class VideosView : UserControl, IRefreshable
    {
        private readonly MainWindow _host;
        private List<LibraryItem> _all = new();
        private List<LibraryItem> _filtered = new();
        private int _sort;

        public VideosView(MainWindow host)
        {
            InitializeComponent();
            _host = host;
            LoadVideos();
        }

        public void Refresh() => LoadVideos();

        private void LoadVideos()
        {
            _all = AppServices.Library.GetItems(ItemKind.Video, limit: SaveSpace.Services.LibraryService.AllItems);
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var query = SearchBox.Text.Trim();
            IEnumerable<LibraryItem> filtered = _all;
            if (!string.IsNullOrWhiteSpace(query))
                filtered = filtered.Where(i =>
                    i.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    i.Tags.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (i.FolderName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));

            _filtered = _sort switch
            {
                1 => filtered.OrderBy(i => i.UpdatedAt).ToList(),
                2 => filtered.OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase).ToList(),
                3 => filtered.OrderByDescending(i => i.SizeBytes).ToList(),
                _ => filtered.OrderByDescending(i => i.UpdatedAt).ToList()
            };

            VideoList.ItemsSource = _filtered;
            CountLabel.Text = _filtered.Count == 1 ? "1 video" : $"{_filtered.Count} video's";
            SubtitleText.Text = _all.Count == 0
                ? "Je clips en films, direct af te spelen."
                : $"{_all.Count} {( _all.Count == 1 ? "video" : "video's")} · {LibraryItem.FormatSize(_all.Sum(i => i.SizeBytes))}";
            EmptyPanel.Visibility = _filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyTitle.Text = _all.Count == 0 ? "Nog geen video's" : "Geen video's gevonden";
            EmptyMessage.Text = _all.Count == 0
                ? "Importeer een clip of film. SaveSpace maakt een miniatuur en bewaart een eigen kopie, zodat je origineel veilig blijft."
                : "Pas je zoekopdracht aan om andere video's te zien.";
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (IsLoaded) ApplyFilter();
        }

        private void Sort_Click(object sender, RoutedEventArgs e)
        {
            _sort = (_sort + 1) % 4;
            SortButton.Content = _sort switch
            {
                1 => "Oudste eerst",
                2 => "Titel A–Z",
                3 => "Grootste eerst",
                _ => "Nieuwste eerst"
            };
            ApplyFilter();
        }

        private void Import_Click(object sender, RoutedEventArgs e) => _host.ImportMedia(ItemKind.Video);

        private void OpenVideo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: LibraryItem item }) return;
            new VideoPlayerWindow(item) { Owner = Window.GetWindow(this) }.Show();
        }

        private void Favorite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: LibraryItem item }) return;
            item.IsFavorite = !item.IsFavorite;
            AppServices.Library.SetFavorite(item.Id, item.IsFavorite);
            item.Raise(nameof(LibraryItem.IsFavorite));
            item.Raise(nameof(LibraryItem.FavoriteGlyph));
            _host.RefreshHeader();
            e.Handled = true;
        }

        private void Folder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: LibraryItem item }) return;
            var dialog = new FolderPickerWindow(AppServices.Library.GetFolders(), item.FolderId)
            {
                Owner = Window.GetWindow(this)
            };
            if (dialog.ShowDialog() != true) return;
            AppServices.Library.SetFolder(item.Id, dialog.SelectedFolderId);
            item.FolderId = dialog.SelectedFolderId;
            item.FolderName = dialog.SelectedFolderId.HasValue
                ? AppServices.Library.GetFolder(dialog.SelectedFolderId.Value)?.Name
                : null;
            ApplyFilter();
            e.Handled = true;
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: LibraryItem item }) return;
            if (!ConfirmDialog.Show(Window.GetWindow(this), "Video naar prullenbak?",
                    $"‘{item.Title}’ wordt naar de prullenbak verplaatst. Het bestand blijft staan tot je de prullenbak leegt.",
                    "Naar prullenbak", true, "\uE74D")) return;
            AppServices.Library.SoftDelete(item.Id);
            AppServices.Notifications.Add("Video naar prullenbak", item.Title, "\uE74D", "Brush.Danger");
            LoadVideos();
            _host.RefreshHeader();
            e.Handled = true;
        }
    }
}
