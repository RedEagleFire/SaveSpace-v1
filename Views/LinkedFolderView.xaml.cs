using System;
using System.Collections.Generic;
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
    public class LinkedEntry
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public bool IsFolder { get; set; }
        public string Icon { get; set; } = "\uE8A5";
        public string KindLabel { get; set; } = "";
        public string SizeLabel { get; set; } = "";
        public string ModifiedLabel { get; set; } = "";
        public Brush IconBrush { get; set; } = Brushes.Gray;
    }

    public partial class LinkedFolderView : UserControl
    {
        private readonly MainWindow _host;
        private readonly LinkedFolder? _folder;
        private readonly string _rootPath;
        private string _currentPath;
        private List<LinkedEntry> _all = new();
        private List<LinkedEntry> _shown = new();

        public LinkedFolderView(MainWindow host, LinkedFolder? folder)
        {
            InitializeComponent();
            _host = host;
            _folder = folder;
            _rootPath = folder?.Path ?? "";
            _currentPath = _rootPath;
            NameText.Text = folder?.Name ?? "Gekoppelde map";
            PathText.Text = _currentPath;
            Refresh();
        }

        public void Refresh()
        {
            _all = new List<LinkedEntry>();
            if (!Directory.Exists(_currentPath))
            {
                CountText.Text = "";
                EntriesList.Visibility = Visibility.Collapsed;
                EmptyPanel.Visibility = Visibility.Visible;
                EmptyTitle.Text = "Map niet gevonden";
                EmptyText.Text = "De gekoppelde map bestaat niet meer of is verplaatst. Verwijder de koppeling of koppel de map opnieuw via Projecten.";
                return;
            }

            try
            {
                var info = new DirectoryInfo(_currentPath);
                foreach (var dir in info.GetDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                {
                    _all.Add(new LinkedEntry
                    {
                        Name = dir.Name,
                        Path = dir.FullName,
                        IsFolder = true,
                        Icon = "\uE8B7",
                        KindLabel = "Map",
                        SizeLabel = "",
                        ModifiedLabel = dir.LastWriteTime.ToString("d MMM yyyy"),
                        IconBrush = (Brush)FindResource("Brush.Folder")
                    });
                }
                foreach (var file in info.GetFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var ext = file.Extension.ToLowerInvariant();
                    _all.Add(new LinkedEntry
                    {
                        Name = file.Name,
                        Path = file.FullName,
                        IsFolder = false,
                        Icon = IconFor(ext),
                        KindLabel = string.IsNullOrEmpty(ext) ? "Bestand" : ext.TrimStart('.').ToUpperInvariant(),
                        SizeLabel = LibraryItem.FormatSize(file.Length),
                        ModifiedLabel = file.LastWriteTime.ToString("d MMM yyyy"),
                        IconBrush = BrushFor(ext)
                    });
                }
            }
            catch (Exception ex)
            {
                FileActions.ShowError(Window.GetWindow(this), "De map kon niet worden gelezen.\n\n" + ex.Message);
            }

            PathText.Text = _currentPath;
            ApplyFilter();
        }

        private Brush BrushFor(string ext)
        {
            if (ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp") return (Brush)FindResource("Brush.Image");
            if (ext is ".mp4" or ".mov" or ".webm" or ".mkv" or ".avi") return (Brush)FindResource("Brush.Video");
            if (ext is ".mp3" or ".wav" or ".m4a" or ".ogg") return (Brush)FindResource("Brush.Video");
            if (ext == ".pdf") return (Brush)FindResource("Brush.Accent");
            return (Brush)FindResource("Brush.Document");
        }

        private static string IconFor(string ext) => ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp" => "\uEB9F",
            ".mp4" or ".mov" or ".webm" or ".mkv" or ".avi" => "\uE714",
            ".mp3" or ".wav" or ".m4a" or ".ogg" => "\uE8D6",
            ".pdf" => "\uE8A5",
            _ => "\uE8A5"
        };

        private void ApplyFilter()
        {
            var query = SearchBox?.Text?.Trim() ?? "";
            _shown = query.Length == 0
                ? _all
                : _all.Where(e => e.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            EntriesList.ItemsSource = _shown;
            CountText.Text = _shown.Count == 1 ? "1 item" : $"{_shown.Count} items";
            var empty = _shown.Count == 0;
            EmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            EntriesList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            if (empty)
            {
                EmptyTitle.Text = query.Length > 0 ? "Geen resultaten" : "Deze map is leeg";
                EmptyText.Text = query.Length > 0
                    ? "Pas je zoekopdracht aan."
                    : "SaveSpace leest de map alleen. Bestanden blijven op hun plek staan.";
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void Entry_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: LinkedEntry entry }) return;
            if (entry.IsFolder)
            {
                _currentPath = entry.Path;
                SearchBox.Clear();
                Refresh();
            }
            else
            {
                try { FileActions.OpenFile(entry.Path); }
                catch (Exception ex) { FileActions.ShowError(Window.GetWindow(this), ex.Message); }
            }
        }

        private void Up_Click(object sender, RoutedEventArgs e)
        {
            var parent = Directory.GetParent(_currentPath)?.FullName;
            if (string.IsNullOrEmpty(parent) ||
                !parent.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase)) return;
            _currentPath = parent;
            SearchBox.Clear();
            Refresh();
        }

        private void Explore_Click(object sender, RoutedEventArgs e)
        {
            try { FileActions.OpenFolder(_currentPath); }
            catch (Exception ex) { FileActions.ShowError(Window.GetWindow(this), ex.Message); }
        }

        private void Back_Click(object sender, RoutedEventArgs e) => _host.Navigate(PageKind.Folders);

        private void RemoveLink_Click(object sender, RoutedEventArgs e)
        {
            if (_folder == null) return;
            if (!ConfirmDialog.Show(Window.GetWindow(this), "Koppeling verwijderen?",
                    $"De koppeling met «{_folder.Name}» wordt verwijderd. Bestanden in de map blijven gewoon staan.",
                    "Ontkoppelen", true, "\uE838")) return;
            AppServices.Library.RemoveLinkedFolder(_folder.Id);
            _host.Navigate(PageKind.Folders);
        }
    }
}
