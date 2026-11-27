using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SaveSpace.Models;
using SaveSpace.Services;
using SaveSpace.Views.Dialogs;

namespace SaveSpace.Views
{
    public partial class HomeView : UserControl, IRefreshable
    {
        private sealed record HomeWidget(string Key, string Title, string Detail, string Icon);
        private readonly MainWindow _host;
        private List<LibraryItem> _files = new();

        public HomeView(MainWindow host)
        {
            InitializeComponent();
            _host = host;
            Refresh();
            ApplyLayout(AppServices.Library.GetSetting("home.layout") == "list");
        }

        public void Refresh()
        {
            var settings = AppServices.Settings;
            var library = AppServices.Library;
            var stats = library.GetStats();

            var hour = DateTime.Now.Hour;
            GreetingText.Text = hour < 12 ? "Goedemorgen" : hour < 18 ? "Goedemiddag" : "Goedenavond";
            var culture = new CultureInfo("nl-NL");
            var date = DateTime.Now.ToString("dddd d MMMM", culture);
            DateText.Text = (char.ToUpper(date[0], culture) + date[1..]).ToUpperInvariant();

            SummaryText.Text = stats.TotalItems == 0
                ? "Je bibliotheek is nog leeg"
                : $"{stats.Notes} notities · {stats.Folders} projecten · {stats.Documents + stats.Images + stats.Videos + stats.Audios} bestanden";

            var recentNotes = library.GetItems(ItemKind.Note, limit: 3);
            RecentNotesList.ItemsSource = recentNotes;
            NotesEmpty.Visibility = recentNotes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RecentNotesList.Visibility = recentNotes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            SaveSummary.Text = recentNotes.Count > 0
                ? $"Laatst opgeslagen: {recentNotes[0].DateLabel.ToLowerInvariant()}"
                : "Opgeslagen: nog niets";

            _files = library.GetItems(limit: 100)
                .Where(i => i.Kind != ItemKind.Note)
                .OrderByDescending(i => i.UpdatedAt)
                .Take(6).ToList();
            RecentCardsList.ItemsSource = _files;
            RecentRowsList.ItemsSource = _files;
            var filesEmpty = _files.Count == 0;
            FilesEmpty.Visibility = filesEmpty ? Visibility.Visible : Visibility.Collapsed;
            RecentCardsList.Visibility = filesEmpty ? Visibility.Collapsed : Visibility.Visible;
            if (ListToggle.IsChecked == true)
                RecentRowsList.Visibility = filesEmpty ? Visibility.Collapsed : Visibility.Visible;

            BuildProjects(library);
            BuildWidgets(stats, settings);

            var used = stats.TotalBytes;
            StorageUsed.Text = used == 0 ? "0 MB" : LibraryItem.FormatSize(used);
            try
            {
                var root = Path.GetPathRoot(AppPaths.Root);
                if (!string.IsNullOrEmpty(root))
                {
                    var drive = new DriveInfo(root);
                    var usedPercent = drive.TotalSize > 0 ? (drive.TotalSize - drive.AvailableFreeSpace) * 100.0 / drive.TotalSize : 0;
                    StorageBar.Value = Math.Clamp(usedPercent, 0, 100);
                    StorageDetail.Text = $"{LibraryItem.FormatSize(drive.AvailableFreeSpace)} vrij op schijf {drive.Name.TrimEnd('\\')}";
                }
            }
            catch
            {
                StorageBar.Value = 0;
                StorageDetail.Text = AppPaths.Root;
            }

            ApplyLayout(AppServices.Library.GetSetting("home.layout") == "list");
        }

        private void BuildWidgets(LibraryStats stats, SettingsService settings)
        {
            var widgets = new List<HomeWidget>();
            if (settings.HomeWidgetFavorites) widgets.Add(new("favorites", "Favorieten", $"{stats.Favorites} opgeslagen", "\uE734"));
            if (settings.HomeWidgetProjects) widgets.Add(new("projects", "Projecten", $"{stats.Folders} projecten", "\uE8B7"));
            if (settings.HomeWidgetNotes) widgets.Add(new("notes", "Notities", $"{stats.Notes} notities", "\uE70B"));
            if (settings.HomeWidgetDocuments) widgets.Add(new("documents", "Documenten", $"{stats.Documents} bestanden", "\uE8A5"));
            if (settings.HomeWidgetImages) widgets.Add(new("images", "Afbeeldingen", $"{stats.Images} foto's", "\uEB9F"));
            if (settings.HomeWidgetVideos) widgets.Add(new("videos", "Video's", $"{stats.Videos} video's", "\uE714"));
            if (settings.HomeWidgetAudio) widgets.Add(new("audio", "Muziek", $"{stats.Audios} audiobestanden", "\uE8D6"));
            if (settings.HomeWidgetLinks) widgets.Add(new("links", "Weblinks", $"{stats.Links} links", "\uE71B"));
            if (settings.HomeWidgetTrash) widgets.Add(new("trash", "Prullenbak", $"{stats.Trash} items", "\uE74D"));
            if (settings.HomeWidgetBackup)
            {
                var backupDetail = settings.LastBackup is DateTime last ? $"Laatst gemaakt: {last:dd-MM-yyyy}" : "Nog geen back-up";
                widgets.Add(new("backup", "Back-up", backupDetail, "\uE72C"));
            }
            HomeWidgetsList.ItemsSource = widgets;
            WidgetsPanel.Visibility = widgets.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void HomeWidget_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: string key }) return;
            if (key == "backup") { Backup_Click(sender, e); return; }
            if (Enum.TryParse<PageKind>(key, true, out var page)) _host.Navigate(page);
        }

        private void OpenWidgetSettings_Click(object sender, RoutedEventArgs e) => _host.Navigate(PageKind.Settings);

        private void BuildProjects(LibraryService library)
        {
            ProjectsPanel.Children.Clear();
            foreach (var folder in library.GetFolders().Where(f => f.ParentId == null).Take(5))
            {
                var button = new Button
                {
                    Style = (Style)FindResource("ProjectCard"),
                    Tag = folder,
                    ToolTip = $"Open project {folder.Name}"
                };
                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition());

                var top = new Grid();
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                top.ColumnDefinitions.Add(new ColumnDefinition());
                var dot = new Border
                {
                    Width = 26,
                    Height = 26,
                    CornerRadius = (CornerRadius)FindResource("Radius.S"),
                    Background = new SolidColorBrush(((SolidColorBrush)folder.ColorBrush).Color) { Opacity = 0.18 },
                    Margin = new Thickness(0, 0, 8, 0)
                };
                dot.Child = new TextBlock
                {
                    Text = "\uE8B7",
                    Style = (Style)FindResource("Text.Icon"),
                    FontSize = 12,
                    Foreground = folder.ColorBrush
                };
                top.Children.Add(dot);

                var title = new TextBlock
                {
                    Text = folder.Name,
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(title, 1);
                top.Children.Add(title);
                grid.Children.Add(top);

                var sub = new TextBlock
                {
                    Text = folder.ItemCount == 1 ? "1 item" : $"{folder.ItemCount} items",
                    Style = (Style)FindResource("Text.Tiny"),
                    Foreground = (Brush)FindResource("Brush.Faint"),
                    Margin = new Thickness(0, 10, 0, 0)
                };
                Grid.SetRow(sub, 1);
                grid.Children.Add(sub);

                button.Content = grid;
                button.Click += Project_Click;
                ProjectsPanel.Children.Add(button);
            }

            var add = new Button
            {
                Style = (Style)FindResource("ProjectCard"),
                ToolTip = "Nieuw project"
            };
            var addContent = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            addContent.Children.Add(new TextBlock
            {
                Text = "\uE710",
                Style = (Style)FindResource("Text.Icon"),
                FontSize = 14,
                Foreground = (Brush)FindResource("Brush.Faint"),
                HorizontalAlignment = HorizontalAlignment.Left
            });
            addContent.Children.Add(new TextBlock
            {
                Text = "Nieuw project",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 9, 0, 1)
            });
            addContent.Children.Add(new TextBlock
            {
                Text = "Groep voor je werk",
                Style = (Style)FindResource("Text.Tiny"),
                Foreground = (Brush)FindResource("Brush.Faint")
            });
            add.Content = addContent;
            add.Click += (_, _) => _host.CreateFolder();
            ProjectsPanel.Children.Add(add);
        }

        private void ApplyLayout(bool list)
        {
            var hasItems = _files.Count > 0;
            CardsToggle.IsChecked = !list;
            ListToggle.IsChecked = list;
            RecentCardsList.Visibility = !list && hasItems ? Visibility.Visible : Visibility.Collapsed;
            RecentRowsList.Visibility = list && hasItems ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CardsToggle_Click(object sender, RoutedEventArgs e) => SetLayout(false);
        private void ListToggle_Click(object sender, RoutedEventArgs e) => SetLayout(true);

        private void SetLayout(bool list)
        {
            AppServices.Library.SetSetting("home.layout", list ? "list" : "cards");
            ApplyLayout(list);
        }

        private void RecentNote_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: LibraryItem note })
                _host.Navigate(PageKind.Notes, note.Id);
        }

        private void RecentFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: LibraryItem item })
                _host.OpenItem(item, _files);
        }

        private void Project_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: Folder folder })
                _host.Navigate(PageKind.Folders, folder.Id);
        }

        private void NewNote_Click(object sender, RoutedEventArgs e) => _host.CreateNote();
        private void NewProject_Click(object sender, RoutedEventArgs e) => _host.CreateFolder();
        private void NewImage_Click(object sender, RoutedEventArgs e) => _host.ImportMedia(ItemKind.Image);
        private void NewVideo_Click(object sender, RoutedEventArgs e) => _host.ImportMedia(ItemKind.Video);
        private void NewDocument_Click(object sender, RoutedEventArgs e) => _host.ImportMedia(ItemKind.Document);
        private void AllProjects_Click(object sender, RoutedEventArgs e) => _host.Navigate(PageKind.Folders);
        private void AllNotes_Click(object sender, RoutedEventArgs e) => _host.Navigate(PageKind.Notes);
        private void AllFiles_Click(object sender, RoutedEventArgs e) => _host.Navigate(PageKind.Documents);

        private void LibraryCategory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<PageKind>(tag, out var page))
                _host.Navigate(page);
        }

        private void Ai_Click(object sender, RoutedEventArgs e)
        {
            if (!AppServices.Ai.IsConfigured)
            {
                new AiAssistantWindow { Owner = Window.GetWindow(this) }.Show();
                return;
            }
            new AiAssistantWindow { Owner = Window.GetWindow(this) }.Show();
        }

        private void OpenStorage_Click(object sender, RoutedEventArgs e)
        {
            try { FileActions.OpenFolder(AppPaths.Root); }
            catch (Exception ex) { FileActions.ShowError(Window.GetWindow(this), ex.Message); }
        }

        private void Backup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var path = AppServices.Backups.CreateBackup(false);
                Mouse.OverrideCursor = null;
                Refresh();
                _host.RefreshHeader();
                MessageBox.Show(Window.GetWindow(this),
                    "De database is veilig opgeslagen:\n\n" + path +
                    "\n\nEen volledige back-up met foto's en video's maak je via Instellingen → Back-ups.",
                    "Back-up gemaakt", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                FileActions.ShowError(Window.GetWindow(this), "De back-up kon niet worden gemaakt.\n\n" + ex.Message);
            }
        }
    }

    internal static class VisualTreeHelpers
    {
        public static T? FindVisualParent<T>(this DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T typed) return typed;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }
    }
}
