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
    public partial class ImagesView : UserControl, IRefreshable
    {
        private readonly MainWindow _host;
        private List<LibraryItem> _all = new();
        private List<LibraryItem> _filtered = new();
        private long? _folderId;
        private int _sort;

        public ImagesView(MainWindow host)
        {
            InitializeComponent();
            _host = host;
            LoadPhotos();
        }

        public void Refresh() => LoadPhotos();

        private void LoadPhotos()
        {
            _all = AppServices.Library.GetItems(ItemKind.Image, limit: SaveSpace.Services.LibraryService.AllItems);
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var query = SearchBox.Text.Trim();
            IEnumerable<LibraryItem> filtered = _all;
            if (_folderId.HasValue) filtered = filtered.Where(i => i.FolderId == _folderId.Value);
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

            PhotoList.ItemsSource = _filtered;
            CountLabel.Text = _filtered.Count == 1 ? "1 foto" : $"{_filtered.Count} foto's";
            SubtitleText.Text = _all.Count == 0
                ? "Je foto's, groot en dichtbij. De originelen blijven ongemoeid."
                : $"{_all.Count} {( _all.Count == 1 ? "foto" : "foto's")} · {LibraryItem.FormatSize(_all.Sum(i => i.SizeBytes))} in je bibliotheek";
            EmptyPanel.Visibility = _filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyTitle.Text = _all.Count == 0 ? "Nog geen foto's" : "Geen foto's gevonden";
            EmptyMessage.Text = _all.Count == 0
                ? "Importeer je eerste foto's. SaveSpace maakt miniaturen, bewaart een eigen kopie en laat je originele bestanden staan."
                : "Pas je zoekopdracht of mapfilter aan om andere foto's te zien.";
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (IsLoaded) ApplyFilter();
        }

        private void FolderFilter_Click(object sender, RoutedEventArgs e)
        {
            var folders = AppServices.Library.GetFolders();
            var index = _folderId.HasValue ? folders.FindIndex(f => f.Id == _folderId.Value) : -1;
            index++;
            if (index >= folders.Count)
            {
                _folderId = null;
                FolderFilterButton.Content = "Alle mappen";
            }
            else
            {
                _folderId = folders[index].Id;
                FolderFilterButton.Content = folders[index].Name;
            }
            ApplyFilter();
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

        private void Import_Click(object sender, RoutedEventArgs e) => _host.ImportMedia(ItemKind.Image);

        private void OpenPhoto_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: LibraryItem item }) return;
            new ImageViewerWindow(item, _filtered) { Owner = Window.GetWindow(this) }.Show();
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
            if (!ConfirmDialog.Show(Window.GetWindow(this), "Foto naar prullenbak?",
                    $"‘{item.Title}’ wordt naar de prullenbak verplaatst. Het bestand in je bibliotheek blijft staan tot je de prullenbak leegt.",
                    "Naar prullenbak", true, "\uE74D")) return;
            AppServices.Library.SoftDelete(item.Id);
            AppServices.Notifications.Add("Foto naar prullenbak", item.Title, "\uE74D", "Brush.Danger");
            LoadPhotos();
            _host.RefreshHeader();
            e.Handled = true;
        }
    }
}
