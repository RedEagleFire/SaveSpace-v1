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
    public partial class LinksView : UserControl, IRefreshable
    {
        private readonly MainWindow _host;
        private List<LibraryItem> _all = new();
        private int _sort;

        public LinksView(MainWindow host)
        {
            InitializeComponent();
            _host = host;
            LoadLinks();
        }

        public void Refresh() => LoadLinks();

        public void OpenAddDialog()
        {
            var dialog = new LinkEditorDialog(null) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true || dialog.Result == null) return;
            var draft = dialog.Result;
            var item = new LibraryItem
            {
                Kind = ItemKind.Link,
                Title = draft.Title,
                Url = draft.Url,
                Content = draft.Description,
                Tags = draft.Tags,
                FolderId = draft.FolderId,
                IsFavorite = draft.IsFavorite
            };
            AppServices.Library.InsertItem(item);
            AppServices.Notifications.Add("Weblink bewaard", item.Title, "\uE71B", "Brush.Link");
            LoadLinks();
            _host.RefreshHeader();
        }

        private void LoadLinks()
        {
            _all = AppServices.Library.GetItems(ItemKind.Link, limit: SaveSpace.Services.LibraryService.AllItems);
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var query = SearchBox.Text.Trim();
            var filtered = string.IsNullOrWhiteSpace(query)
                ? _all.ToList()
                : _all.Where(i =>
                    i.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    i.Content.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    i.Url?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
                    i.Tags.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

            filtered = _sort switch
            {
                1 => filtered.OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase).ToList(),
                2 => filtered.OrderBy(i => i.Domain, StringComparer.CurrentCultureIgnoreCase).ThenBy(i => i.Title).ToList(),
                3 => filtered.OrderByDescending(i => i.IsFavorite).ThenByDescending(i => i.UpdatedAt).ToList(),
                _ => filtered.OrderByDescending(i => i.UpdatedAt).ToList()
            };

            LinksList.ItemsSource = filtered;
            CountLabel.Text = filtered.Count == 1 ? "1 link" : $"{filtered.Count} links";
            SubtitleText.Text = _all.Count == 0
                ? "Bewaar interessante pagina's met titel en omschrijving."
                : $"{_all.Count} {( _all.Count == 1 ? "pagina" : "pagina's")} bewaard · lokaal en doorzoekbaar";
            EmptyPanel.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
                1 => "Titel A–Z",
                2 => "Op domein",
                3 => "Favorieten eerst",
                _ => "Nieuwste eerst"
            };
            ApplyFilter();
        }

        private void NewLink_Click(object sender, RoutedEventArgs e) => OpenAddDialog();

        private void OpenLink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: LibraryItem item }) return;
            try { FileActions.OpenUrl(item.Url); }
            catch (Exception ex) { FileActions.ShowError(Window.GetWindow(this), ex.Message); }
        }

        private void Reveal_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: LibraryItem item }) return;
            try { FileActions.OpenUrl(item.Url); }
            catch (Exception ex) { FileActions.ShowError(Window.GetWindow(this), ex.Message); }
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: LibraryItem item }) return;
            var dialog = new LinkEditorDialog(item) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true || dialog.Result == null) return;
            var draft = dialog.Result;
            item.Url = draft.Url;
            item.Title = draft.Title;
            item.Content = draft.Description;
            item.Tags = draft.Tags;
            item.FolderId = draft.FolderId;
            item.IsFavorite = draft.IsFavorite;
            AppServices.Library.UpdateItem(item);
            LoadLinks();
            _host.RefreshHeader();
        }

        private void Favorite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: LibraryItem item }) return;
            item.IsFavorite = !item.IsFavorite;
            AppServices.Library.SetFavorite(item.Id, item.IsFavorite);
            item.Raise(nameof(LibraryItem.IsFavorite));
            item.Raise(nameof(LibraryItem.FavoriteGlyph));
            ApplyFilter();
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
            if (!ConfirmDialog.Show(Window.GetWindow(this), "Link naar prullenbak?",
                    $"‘{item.Title}’ wordt naar de prullenbak verplaatst. Je kunt hem daar later nog herstellen.",
                    "Naar prullenbak", true, "\uE74D")) return;
            AppServices.Library.SoftDelete(item.Id);
            AppServices.Notifications.Add("Link naar prullenbak", item.Title, "\uE74D", "Brush.Danger");
            LoadLinks();
            _host.RefreshHeader();
            e.Handled = true;
        }
    }
}
