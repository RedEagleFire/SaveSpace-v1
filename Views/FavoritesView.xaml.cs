using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SaveSpace.Models;
using SaveSpace.Services;

namespace SaveSpace.Views
{
    public partial class FavoritesView : UserControl, IRefreshable
    {
        private readonly MainWindow _host;
        private List<LibraryItem> _all = new();
        private List<LibraryItem> _shown = new();
        private List<Folder> _favoriteFolders = new();
        private string _filter = "all";

        public FavoritesView(MainWindow host)
        {
            InitializeComponent();
            _host = host;
            Refresh();
        }

        public void Refresh()
        {
            _all = AppServices.Library.GetFavorites().ToList();
            _favoriteFolders = AppServices.Library.GetFolders().Where(folder => folder.IsFavorite).ToList();
            FavoriteProjectsList.ItemsSource = _favoriteFolders;
            FavoriteProjectsPanel.Visibility = _favoriteFolders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            IEnumerable<LibraryItem> items = _all;
            items = _filter switch
            {
                "notes" => items.Where(i => i.Kind == ItemKind.Note),
                "links" => items.Where(i => i.Kind == ItemKind.Link),
                "files" => items.Where(i => i.Kind is ItemKind.Image or ItemKind.Video or ItemKind.Document or ItemKind.Audio),
                _ => items
            };
            _shown = items.ToList();
            FavoritesList.ItemsSource = _shown;
            var total = _shown.Count + (_filter == "all" ? _favoriteFolders.Count : 0);
            CountText.Text = total == 1 ? "1 favoriet" : $"{total} favorieten";
            SubtitleText.Text = _all.Count == 0
                ? "De dingen die je altijd binnen handbereik wilt hebben."
                : $"{_all.Count} favorieten, gekozen door jou.";

            var empty = _shown.Count == 0 && (_filter != "all" || _favoriteFolders.Count == 0);
            EmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            FavoritesList.Visibility = _shown.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (empty && _all.Count > 0)
            {
                EmptyTitle.Text = "Geen favorieten in deze selectie";
                EmptyText.Text = "Kies een ander filter of voeg iets toe aan je favorieten.";
            }
        }

        private void ProjectFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: Folder folder }) _host.Navigate(PageKind.Folders, folder.Id);
        }

        private void Filter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton clicked || clicked.Tag is not string tag) return;
            _filter = tag;
            foreach (var chip in new[] { FilterAll, FilterNotes, FilterLinks, FilterFiles })
                chip.IsChecked = ReferenceEquals(chip, clicked);
            clicked.IsChecked = true;
            ApplyFilter();
        }

        private void Item_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { Tag: LibraryItem item })
                _host.OpenItem(item, _shown);
        }

        private void Unfavorite_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not FrameworkElement { Tag: LibraryItem item }) return;
            AppServices.Library.SetFavorite(item.Id, false);
            Refresh();
        }

        private void OpenSearch_Click(object sender, RoutedEventArgs e) => _host.Navigate(PageKind.Search, "");
        private void OpenNotes_Click(object sender, RoutedEventArgs e) => _host.Navigate(PageKind.Notes);
    }
}
