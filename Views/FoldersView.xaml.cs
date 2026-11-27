using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SaveSpace.Models;
using SaveSpace.Services;
using SaveSpace.Views.Dialogs;

namespace SaveSpace.Views
{
    public partial class FoldersView : UserControl, IRefreshable
    {
        private readonly MainWindow _host;
        private long? _folderId;
        private Folder? _folder;
        private List<LibraryItem> _items = new();

        public FoldersView(MainWindow host, long? folderId)
        {
            InitializeComponent();
            _host = host;
            _folderId = folderId;
            Refresh();
        }

        public void Refresh()
        {
            if (_folderId.HasValue)
            {
                _folder = AppServices.Library.GetFolder(_folderId.Value);
                if (_folder == null)
                {
                    _folderId = null;
                    ShowOverview();
                    return;
                }
                ShowDetail();
            }
            else
            {
                ShowOverview();
            }
        }

        private void ShowOverview()
        {
            OverviewPanel.Visibility = Visibility.Visible;
            DetailPanel.Visibility = Visibility.Collapsed;
            BuildProjects();
            var linked = AppServices.Library.GetLinkedFolders();
            LinkedList.ItemsSource = linked;
            NoLinked.Visibility = linked.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var count = AppServices.Library.GetFolders().Count;
            OverviewSubtitle.Text = count == 0
                ? "Bundel notities en bestanden per onderwerp."
                : $"{count} projecten. Sleep je werk bij elkaar en houd het overzichtelijk.";
        }

        private void BuildProjects()
        {
            ProjectsPanel.Children.Clear();
            var folders = AppServices.Library.GetFolders().Where(f => f.ParentId == null).ToList();
            NoProjects.Visibility = folders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var folder in folders)
                ProjectsPanel.Children.Add(BuildProjectTile(folder));

            var add = new Border
            {
                Style = (Style)FindResource("ProjectTile"),
                Background = Brushes.Transparent
            };
            var addGrid = new Grid();
            addGrid.Children.Add(new TextBlock
            {
                Text = "\uE710",
                Style = (Style)FindResource("Text.Icon"),
                FontSize = 18,
                Foreground = (Brush)FindResource("Brush.Faint"),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            });
            var addText = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            addText.Children.Add(new TextBlock { Text = "Nieuw project", FontSize = 12.5, FontWeight = FontWeights.SemiBold });
            addText.Children.Add(new TextBlock
            {
                Text = "Begin met een schoon project",
                Style = (Style)FindResource("Text.Tiny"),
                Foreground = (Brush)FindResource("Brush.Faint"),
                Margin = new Thickness(0, 2, 0, 0)
            });
            addGrid.Children.Add(addText);
            add.Child = addGrid;
            add.Padding = new Thickness(14, 13, 14, 13);
            add.MouseLeftButtonUp += (_, _) => CreateProject();
            ProjectsPanel.Children.Add(add);
        }

        private Border BuildProjectTile(Folder folder)
        {
            var color = ((SolidColorBrush)folder.ColorBrush).Color;
            var icon = IconFor(folder);
            var border = new Border { Style = (Style)FindResource("ProjectTile"), Tag = folder };
            border.Padding = new Thickness(14, 13, 14, 13);

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconBadge = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = (CornerRadius)FindResource("Radius.M"),
                Background = new SolidColorBrush(color) { Opacity = 0.16 },
                HorizontalAlignment = HorizontalAlignment.Left
            };
            iconBadge.Child = new TextBlock
            {
                Text = icon,
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 15,
                Foreground = new SolidColorBrush(color),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(iconBadge);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var favorite = new Button
            {
                Style = (Style)FindResource("Btn.IconSmall"),
                Content = folder.IsFavorite ? "\uE735" : "\uE734",
                Foreground = (Brush)FindResource(folder.IsFavorite ? "Brush.DarkYellow" : "Brush.Muted"),
                Tag = folder,
                ToolTip = folder.IsFavorite ? "Project uit favorieten halen" : "Project als favoriet bewaren"
            };
            favorite.Click += (_, e) =>
            {
                e.Handled = true;
                AppServices.Library.SetFolderFavorite(folder.Id, !folder.IsFavorite);
                Refresh();
            };
            actions.Children.Add(favorite);

            var edit = new Button
            {
                Style = (Style)FindResource("Btn.IconSmall"),
                Content = "\uE70F",
                Tag = folder,
                ToolTip = "Project bewerken",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top
            };
            edit.Click += (_, e) =>
            {
                e.Handled = true;
                EditProject(folder);
            };
            actions.Children.Add(edit);
            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            text.Children.Add(new TextBlock
            {
                Text = folder.Name,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            text.Children.Add(new TextBlock
            {
                Text = folder.SubfolderCount > 0
                    ? $"{folder.CountLabel} · {folder.SubfolderCount} subprojecten"
                    : folder.CountLabel,
                Style = (Style)FindResource("Text.Tiny"),
                Foreground = (Brush)FindResource("Brush.Faint"),
                Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetRow(text, 1);
            Grid.SetColumnSpan(text, 2);
            grid.Children.Add(text);

            border.Child = grid;
            border.MouseLeftButtonUp += (_, e) =>
            {
                if (e.ChangedButton == MouseButton.Left)
                    _host.Navigate(PageKind.Folders, folder.Id);
            };
            return border;
        }

        private void ShowDetail()
        {
            if (_folder == null) return;
            OverviewPanel.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Visible;
            _items = AppServices.Library.GetItems(folderId: _folder.Id, limit: SaveSpace.Services.LibraryService.AllItems);
            ProjectItemsList.ItemsSource = _items;
            var color = ((SolidColorBrush)_folder.ColorBrush).Color;
            DetailBadge.Background = new SolidColorBrush(color) { Opacity = 0.16 };
            DetailIcon.Text = IconFor(_folder);
            DetailIcon.Foreground = new SolidColorBrush(color);
            DetailName.Text = _folder.Name;
            DetailMeta.Text = _items.Count == 1 ? "1 item in dit project" : $"{_items.Count} items in dit project";
            FavoriteFolderButton.Content = _folder.IsFavorite ? "\uE735" : "\uE734";
            FavoriteFolderButton.Foreground = (Brush)FindResource(_folder.IsFavorite ? "Brush.DarkYellow" : "Brush.Muted");
            FavoriteFolderButton.ToolTip = _folder.IsFavorite ? "Project uit favorieten halen" : "Project als favoriet bewaren";
            ProjectEmpty.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ProjectItemsList.Visibility = _items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void FavoriteFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_folder == null) return;
            AppServices.Library.SetFolderFavorite(_folder.Id, !_folder.IsFavorite);
            _folder = AppServices.Library.GetFolder(_folder.Id);
            ShowDetail();
        }

        private string IconFor(Folder folder) =>
            AppServices.Library.GetSetting($"folder.icon.{folder.Id}") ?? "\uE8B7";

        public void OpenCreateDialog() => CreateProject();

        private void CreateProject()
        {
            var dialog = new ProjectEditorDialog("", "#E32636", "\uE8B7") { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true) return;
            var id = AppServices.Library.CreateFolder(dialog.ProjectName, dialog.SelectedColor, null);
            AppServices.Library.SetSetting($"folder.icon.{id}", dialog.SelectedIcon);
            AppServices.Notifications.Add("Project gemaakt", dialog.ProjectName, dialog.SelectedIcon, "Brush.Folder");
            _host.Navigate(PageKind.Folders, id);
        }

        private void EditProject(Folder folder)
        {
            var dialog = new ProjectEditorDialog(folder.Name, folder.Color, IconFor(folder)) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true) return;
            AppServices.Library.UpdateFolder(folder.Id, dialog.ProjectName, dialog.SelectedColor, folder.ParentId);
            AppServices.Library.SetSetting($"folder.icon.{folder.Id}", dialog.SelectedIcon);
            Refresh();
            _host.RefreshHeader();
        }

        private void DeleteProject(Folder folder)
        {
            if (!ConfirmDialog.Show(Window.GetWindow(this), "Project verwijderen?",
                    $"«{folder.Name}» wordt verwijderd. De notities en bestanden blijven bestaan, maar staan daarna los in je bibliotheek.",
                    "Project verwijderen", true, "\uE74D")) return;
            AppServices.Library.DeleteFolder(folder.Id);
            _host.Navigate(PageKind.Folders);
        }

        private void NewProject_Click(object sender, RoutedEventArgs e) => CreateProject();
        private void EditProject_Click(object sender, RoutedEventArgs e) { if (_folder != null) EditProject(_folder); }
        private void DeleteProject_Click(object sender, RoutedEventArgs e) { if (_folder != null) DeleteProject(_folder); }
        private void Back_Click(object sender, RoutedEventArgs e) => _host.Navigate(PageKind.Folders);

        private void AddNote_Click(object sender, RoutedEventArgs e)
        {
            if (_folder == null) return;
            var note = new LibraryItem
            {
                Kind = ItemKind.Note,
                Title = "Nieuwe notitie",
                Content = "",
                FolderId = _folder.Id
            };
            AppServices.Library.InsertItem(note);
            _host.Navigate(PageKind.Notes, note.Id);
        }

        private void AddFile_Click(object sender, RoutedEventArgs e)
        {
            if (_folder == null) return;
            var dialog = new OpenFileDialog
            {
                Multiselect = true,
                CheckFileExists = true,
                Title = "Bestanden toevoegen aan project"
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            AddFiles(dialog.FileNames);
        }

        private void AddFiles(IEnumerable<string> files)
        {
            if (_folder == null) return;
            Mouse.OverrideCursor = Cursors.Wait;
            var errors = new List<string>();
            var added = 0;
            try
            {
                var imports = new MediaImportService();
                var imageFiles = files.Where(f => IsExt(f, ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".tif", ".tiff")).ToList();
                var videoFiles = files.Where(f => IsExt(f, ".mp4", ".mov", ".webm", ".m4v", ".wmv", ".avi", ".mkv")).ToList();
                var audioFiles = files.Where(f => IsExt(f, ".mp3", ".wav", ".m4a", ".ogg", ".flac", ".aac", ".wma")).ToList();
                var docFiles = files.Except(imageFiles).Except(videoFiles).Except(audioFiles).ToList();
                foreach (var group in new[] { (Files: imageFiles, Kind: ItemKind.Image), (Files: videoFiles, Kind: ItemKind.Video), (Files: docFiles, Kind: ItemKind.Document), (Files: audioFiles, Kind: ItemKind.Audio) })
                {
                    if (group.Files.Count == 0) continue;
                    var r = imports.Import(group.Files, group.Kind, _folder.Id);
                    added += r.Items.Count;
                    errors.AddRange(r.Errors);
                }
                AppServices.Notifications.Add($"{added} bestand(en) toegevoegd", _folder.Name, "\uE8E5", "Brush.Folder");
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
            Refresh();
            _host.RefreshHeader();
            if (errors.Count > 0)
                FileActions.ShowError(Window.GetWindow(this), string.Join("\n", errors.Take(5)));
        }

        private static bool IsExt(string path, params string[] extensions) =>
            extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

        private void AddExisting_Click(object sender, RoutedEventArgs e)
        {
            if (_folder == null) return;
            var candidates = AppServices.Library.GetItems(limit: SaveSpace.Services.LibraryService.AllItems)
                .Where(i => i.FolderId != _folder.Id).ToList();
            var picker = new ItemPickerWindow(candidates, "Item aan project toevoegen") { Owner = Window.GetWindow(this) };
            if (picker.ShowDialog() != true || picker.SelectedItem == null) return;
            AppServices.Library.SetFolder(picker.SelectedItem.Id, _folder.Id);
            Refresh();
        }

        private void Item_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { Tag: LibraryItem item })
                _host.OpenItem(item, _items);
        }

        private void RemoveFromProject_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not FrameworkElement { Tag: LibraryItem item }) return;
            AppServices.Library.SetFolder(item.Id, null);
            Refresh();
        }

        private void AddLinkedFolder_Click(object sender, RoutedEventArgs e)
        {
            var path = FileActions.PickFolder(Window.GetWindow(this), "Kies een map om te koppelen");
            if (string.IsNullOrWhiteSpace(path)) return;
            AppServices.Library.AddLinkedFolder(Path.GetFileName(path.TrimEnd('\\')), path);
            Refresh();
        }

        private void OpenLinked_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: LinkedFolder folder })
                _host.Navigate(PageKind.LinkedFolder, folder);
        }

        private void RemoveLinked_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: LinkedFolder folder }) return;
            if (!ConfirmDialog.Show(Window.GetWindow(this), "Koppeling verwijderen?",
                    $"De koppeling met «{folder.Name}» wordt verwijderd. Bestanden in de map blijven gewoon staan.",
                    "Koppeling verwijderen", true, "\uE838")) return;
            AppServices.Library.RemoveLinkedFolder(folder.Id);
            Refresh();
        }
    }
}
