using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SaveSpace.Models;
using SaveSpace.Services;
using SaveSpace.Views.Dialogs;

namespace SaveSpace.Views
{
    public partial class DocumentsView : UserControl, IRefreshable
    {
        private readonly MainWindow _host;
        private readonly string _initialFilter;
        private List<LibraryItem> _all = new();
        private List<LibraryItem> _shown = new();
        private string _filter = "all";

        public DocumentsView(MainWindow host, string initialFilter = "all")
        {
            InitializeComponent();
            _host = host;
            _initialFilter = initialFilter;
            _filter = initialFilter;
            if (initialFilter == "audio")
            {
                EyebrowText.Text = "BIBLIOTHEEK";
                HeadingText.Text = "Muziek";
                ImportButtonLabel.Text = "Muziek toevoegen";
                FilterAudio.IsChecked = true;
                FilterAll.IsChecked = false;
            }
            Refresh();
        }

        public void Refresh()
        {
            _all = _filter == "audio"
                ? AppServices.Library.GetItems(ItemKind.Audio, limit: SaveSpace.Services.LibraryService.AllItems)
                : AppServices.Library.GetItems(ItemKind.Document, limit: SaveSpace.Services.LibraryService.AllItems);
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var query = SearchBox?.Text?.Trim() ?? "";
            IEnumerable<LibraryItem> items = _all;
            items = _filter switch
            {
                "pdf" => items.Where(i => Ext(i) == ".pdf"),
                "office" => items.Where(i => Ext(i) is ".doc" or ".docx" or ".ppt" or ".pptx" or ".xls" or ".xlsx" or ".odt" or ".odp" or ".ods"),
                "text" => items.Where(i => Ext(i) is ".txt" or ".md" or ".rtf" or ".csv" or ".json" or ".xml" or ".html" or ".htm" or ".log"),
                "media" => items.Where(i => Ext(i) is ".mp3" or ".wav" or ".m4a" or ".ogg" or ".mp4" or ".mov" or ".webm"),
                "audio" => items.Where(i => Ext(i) is ".mp3" or ".wav" or ".m4a" or ".ogg" or ".flac" or ".aac" or ".wma"),
                _ => items
            };
            if (query.Length > 0)
                items = items.Where(i => i.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                         (i.FilePath?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));

            _shown = items.ToList();
            DocsList.ItemsSource = _shown;
            CountText.Text = _shown.Count == 1 ? "1 document" : $"{_shown.Count} documenten";
            SubtitleText.Text = _filter == "audio"
                ? (_all.Count == 0 ? "Je muziek wordt lokaal bewaard." : $"{_all.Count} muziekbestanden in je bibliotheek.")
                : (_all.Count == 0
                    ? "PDF's, tekst en Office-bestanden, lokaal bewaard."
                    : $"{_all.Count} documenten in je bibliotheek. Originelen blijven ongewijzigd.");

            var empty = _shown.Count == 0;
            EmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            DocsList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            if (empty)
            {
                if (_all.Count == 0)
                {
                    EmptyTitle.Text = _filter == "audio" ? "Nog geen muziek" : "Nog geen documenten";
                    EmptyText.Text = _filter == "audio"
                        ? "Voeg muziek toe om je audiobestanden hier terug te vinden."
                        : "Voeg een pdf, Word-bestand of tekst toe. SaveSpace maakt een veilige kopie en laat je origineel staan.";
                }
                else
                {
                    EmptyTitle.Text = "Geen resultaten";
                    EmptyText.Text = "Pas je zoekopdracht of filter aan.";
                }
            }
        }

        private static string Ext(LibraryItem item) =>
            string.IsNullOrWhiteSpace(item.FilePath) ? "" : Path.GetExtension(item.FilePath).ToLowerInvariant();

        private void Filter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton clicked || clicked.Tag is not string tag) return;
            _filter = tag;
            foreach (var chip in new[] { FilterAll, FilterPdf, FilterOffice, FilterText, FilterMedia, FilterAudio })
                chip.IsChecked = ReferenceEquals(chip, clicked);
            clicked.IsChecked = true;
            ApplyFilter();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
        private void Import_Click(object sender, RoutedEventArgs e) => _host.ImportMedia(_filter == "audio" ? ItemKind.Audio : ItemKind.Document);

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try { FileActions.OpenFolder(Path.Combine(AppPaths.Root, "Attachments")); }
            catch (Exception ex) { FileActions.ShowError(Window.GetWindow(this), ex.Message); }
        }

        private void Doc_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount < 2) return;
            if (sender is FrameworkElement { Tag: LibraryItem item }) _host.OpenItem(item, _shown);
        }

        private void Doc_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.ContextMenu is ContextMenu menu)
                menu.DataContext = border.DataContext;
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: LibraryItem item }) _host.OpenItem(item, _shown);
        }

        private void Fav_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: LibraryItem item }) return;
            AppServices.Library.SetFavorite(item.Id, !item.IsFavorite);
            Refresh();
        }

        private void MenuOpen_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: LibraryItem item }) _host.OpenItem(item, _shown);
        }

        private void MenuExternal_Click(object sender, RoutedEventArgs e) => OpenExternal(TagItem(sender));

        private void MenuFolder_Click(object sender, RoutedEventArgs e) => ShowInFolder(TagItem(sender));

        private void MenuFavorite_Click(object sender, RoutedEventArgs e)
        {
            var item = TagItem(sender);
            if (item == null) return;
            AppServices.Library.SetFavorite(item.Id, !item.IsFavorite);
            Refresh();
        }

        private void MenuTrash_Click(object sender, RoutedEventArgs e)
        {
            var item = TagItem(sender);
            if (item == null) return;
            if (!ConfirmDialog.Show(Window.GetWindow(this), "Naar prullenbak?",
                    $"«{item.Title}» wordt naar de prullenbak verplaatst. Je kunt het later terugzetten.",
                    "Naar prullenbak", true, "\uE74D")) return;
            AppServices.Library.SoftDelete(item.Id);
            AppServices.Notifications.Add("Naar prullenbak", item.Title, "\uE74D", "Brush.Amber");
            Refresh();
            _host.RefreshHeader();
        }

        private static LibraryItem? TagItem(object sender) => (sender as FrameworkElement)?.Tag as LibraryItem;

        private void OpenExternal(LibraryItem? item)
        {
            if (item == null) return;
            try { FileActions.OpenFile(item.FilePath); }
            catch (Exception ex) { FileActions.ShowError(Window.GetWindow(this), ex.Message); }
        }

        private void ShowInFolder(LibraryItem? item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.FilePath)) return;
            try
            {
                if (!File.Exists(item.FilePath))
                {
                    FileActions.ShowError(Window.GetWindow(this), "Het bestand is niet meer gevonden in de opslagmap.");
                    return;
                }
                SaveSpace.Services.FileActions.RevealInExplorer(item.FilePath);
            }
            catch (Exception ex) { FileActions.ShowError(Window.GetWindow(this), ex.Message); }
        }
    }
}
