using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using SaveSpace.Models;
using SaveSpace.Services;

namespace SaveSpace.Views
{
    public partial class SearchView : UserControl, IRefreshable
    {
        private readonly MainWindow _host;
        private string _query = "";
        private string _filter = "all";
        private List<LibraryItem> _results = new();
        private List<Folder> _folders = new();

        public SearchView(MainWindow host, string query)
        {
            InitializeComponent();
            _host = host;
            _query = query ?? "";
            QueryBox.Text = _query;
            Loaded += (_, _) =>
            {
                QueryBox.Focus();
                QueryBox.CaretIndex = QueryBox.Text.Length;
                RunSearch();
            };
        }

        public void Refresh() => RunSearch();

        private void QueryBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _query = QueryBox.Text;
            RunSearch();
        }

        private void Filter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton clicked || clicked.Tag is not string tag) return;
            _filter = tag;
            foreach (var chip in new[] { FilterAll, FilterNotes, FilterFiles, FilterLinks, FilterProjects })
                chip.IsChecked = ReferenceEquals(chip, clicked);
            clicked.IsChecked = true;
            RunSearch();
        }

        private void RunSearch()
        {
            if (!IsLoaded) return;
            var query = _query.Trim();
            if (query.Length == 0)
            {
                _results.Clear();
                _folders.Clear();
                ResultsList.ItemsSource = null;
                ProjectsList.ItemsSource = null;
                ProjectsSection.Visibility = Visibility.Collapsed;
                ResultsSection.Visibility = Visibility.Collapsed;
                CountText.Text = "";
                EmptyPanel.Visibility = Visibility.Visible;
                EmptyIcon.Text = "\uE721";
                EmptyTitle.Text = "Typ om te zoeken";
                EmptyText.Text = "Doorzoek titels, notitie-inhoud, tags, weblinks, bestandsnamen en projecten. Resultaten verschijnen direct.";
                return;
            }

            var library = AppServices.Library;
            var found = library.Search(query, SaveSpace.Services.LibraryService.AllItems).ToList();
            var all = library.GetItems(limit: SaveSpace.Services.LibraryService.AllItems);
            foreach (var item in all)
            {
                if (found.Any(i => i.Id == item.Id)) continue;
                var name = Path.GetFileName(item.FilePath ?? "");
                if (name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    item.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                    found.Add(item);
            }

            _folders = library.GetFolders()
                .Where(f => f.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

            IEnumerable<LibraryItem> filtered = found;
            filtered = _filter switch
            {
                "notes" => filtered.Where(i => i.Kind == ItemKind.Note),
                "files" => filtered.Where(i => i.Kind is ItemKind.Image or ItemKind.Video or ItemKind.Document or ItemKind.Audio),
                "links" => filtered.Where(i => i.Kind == ItemKind.Link),
                "projects" => Enumerable.Empty<LibraryItem>(),
                _ => filtered
            };
            _results = filtered.ToList();

            var showProjects = _filter is "all" or "projects";
            ProjectsList.ItemsSource = showProjects ? _folders : null;
            ProjectsSection.Visibility = showProjects && _folders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ResultsList.ItemsSource = _filter == "projects" ? null : _results;
            ResultsSection.Visibility = _filter != "projects" && _results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            var total = _results.Count + (showProjects ? _folders.Count : 0);
            CountText.Text = total == 1 ? "1 resultaat" : $"{total} resultaten";
            EmptyPanel.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (total == 0)
            {
                EmptyIcon.Text = "\uE721";
                EmptyTitle.Text = "Niets gevonden";
                EmptyText.Text = $"Geen resultaten voor «{query}». Probeer een kortere zoekopdracht of een ander filter.";
            }
        }

        private void Result_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { Tag: LibraryItem item })
                _host.OpenItem(item, _results);
        }

        private void Project_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: Folder folder })
                _host.Navigate(PageKind.Folders, folder.Id);
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            QueryBox.Clear();
            QueryBox.Focus();
        }
    }
}
