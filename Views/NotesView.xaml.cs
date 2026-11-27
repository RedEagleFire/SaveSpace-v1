using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SaveSpace.Models;
using SaveSpace.Services;
using SaveSpace.Views.Dialogs;
using SaveSpace.Views.Workspace;

namespace SaveSpace.Views
{
    public partial class NotesView : UserControl, IRefreshable
    {
        private enum WorkspaceKind { Browser, Ai, File }

        private sealed class WorkspaceTab
        {
            public WorkspaceKind Kind { get; init; }
            public string Title { get; set; } = "";
            public string Icon { get; set; } = "\uE774";
            public FrameworkElement View { get; init; } = null!;
            public bool IsPinned { get; set; }
            public string? Url { get; set; }
            public string? Path { get; set; }
            public BrowserPane? Browser { get; init; }
            public FilePane? File { get; init; }
            public AiPane? Ai { get; init; }
        }

        private sealed class WorkspaceState
        {
            public bool Visible { get; set; } = true;
            public double Width { get; set; } = 400;
            public int Active { get; set; }
            public List<WorkspaceTabState> Tabs { get; set; } = new();
        }

        private sealed class WorkspaceTabState
        {
            public string Kind { get; set; } = "browser";
            public string Title { get; set; } = "";
            public string? Url { get; set; }
            public string? Path { get; set; }
            public bool Pinned { get; set; }
        }

        private readonly MainWindow _host;
        private readonly DispatcherTimer _saveTimer;
        private readonly DispatcherTimer _statusTimer;
        private readonly List<WorkspaceTab> _tabs = new();
        private List<LibraryItem> _allNotes = new();
        private LibraryItem? _current;
        private bool _dirty;
        private bool _suppress;
        private bool _suppressSelection;
        private long? _pendingSelectId;
        private WorkspaceTab? _activeTab;
        private AiPane? _aiPane;
        private WorkspaceTab? _draggingTab;
        private Point _dragStart;
        private bool _workspaceVisible;
        private double _workspaceWidth = 400;
        private bool _restoringWorkspace;
        private DateTime _savedAt = DateTime.MinValue;

        public NotesView(MainWindow host, long? noteId)
        {
            InitializeComponent();
            _host = host;
            _pendingSelectId = noteId;
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
            _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveCurrent(); };
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _statusTimer.Tick += (_, _) => UpdateSaveStatus();
            _statusTimer.Start();
            Editor.SpellCheck.IsEnabled = AppServices.Settings.EditorSpellcheck;
            Unloaded += (_, _) =>
            {
                _saveTimer.Stop();
                _statusTimer.Stop();
                SaveCurrent();
                SaveWorkspace();
                foreach (var tab in _tabs)
                {
                    tab.Browser?.Dispose();
                    tab.File?.Dispose();
                }
            };
            LoadNotes();
            InitializeWorkspace();
        }

        public void Refresh()
        {
            SaveCurrent();
            LoadNotes();
        }

        public long? CurrentNoteId => _current?.Id;

        public void InsertFileFromDisk()
        {
            if (_current == null)
            {
                FileActions.ShowError(Window.GetWindow(this), "Kies eerst een notitie voordat je een bestand invoegt.");
                return;
            }
            InsertAttachment();
        }

        public void OpenDocumentPicker() => OpenDocumentTab();

        public void OpenBrowserWorkspace()
        {
            AddBrowserTab(null, select: true);
            ShowWorkspace();
        }

        private void LoadNotes()
        {
            _allNotes = AppServices.Library.GetItems(ItemKind.Note, limit: SaveSpace.Services.LibraryService.AllItems);
            SubtitleText.Text = _allNotes.Count == 0
                ? "Schrijf, orden en bewaar. Alles wordt automatisch opgeslagen."
                : $"{_allNotes.Count} {( _allNotes.Count == 1 ? "notitie" : "notities")} · automatisch opgeslagen";
            ApplyFilter();

            var visible = NotesList.ItemsSource as List<LibraryItem> ?? new List<LibraryItem>();
            var target = _pendingSelectId.HasValue
                ? visible.FirstOrDefault(n => n.Id == _pendingSelectId.Value)
                : null;
            target ??= visible.FirstOrDefault();
            _pendingSelectId = null;

            if (target != null)
            {
                _suppressSelection = true;
                NotesList.SelectedItem = target;
                _suppressSelection = false;
                LoadNote(target);
            }
            else
            {
                ShowNoNoteState();
            }

            EmptyPanel.Visibility = _allNotes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            NoNotesHint.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            NoNotesHint.Text = _allNotes.Count == 0
                ? "Nog geen notities.\nMaak je eerste notitie met de knop rechtsboven."
                : "Geen notities gevonden met deze zoekopdracht.";
        }

        private void ApplyFilter()
        {
            var query = NoteSearch.Text.Trim();
            List<LibraryItem> filtered;
            if (string.IsNullOrWhiteSpace(query))
            {
                filtered = _allNotes.ToList();
            }
            else
            {
                filtered = _allNotes.Where(n =>
                    n.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    n.Content.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    n.Tags.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            _suppressSelection = true;
            NotesList.ItemsSource = filtered;
            if (_current != null && filtered.Any(n => n.Id == _current.Id))
                NotesList.SelectedItem = filtered.First(n => n.Id == _current.Id);
            _suppressSelection = false;
            NoteCount.Text = filtered.Count.ToString();
            NoNotesHint.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (filtered.Count == 0)
                NoNotesHint.Text = _allNotes.Count == 0
                    ? "Nog geen notities.\nMaak je eerste notitie met de knop rechtsboven."
                    : "Geen notities gevonden met deze zoekopdracht.";
        }

        private void ShowNoNoteState()
        {
            _current = null;
            RefreshAttachmentBar();
            _suppress = true;
            TitleBox.Text = "";
            TagsBox.Text = "";
            FolderLabel.Text = "Geen project";
            Editor.Document.Blocks.Clear();
            _suppress = false;
            _dirty = false;
            UpdateWordCount();
            SaveStatus.Text = "Opgeslagen: zojuist";
            SaveDot.Fill = (Brush)FindResource("Brush.Success");
            _aiPane?.SetNote(null);
            FavButton.Content = "\uE734";
            FavButton.Foreground = (Brush)FindResource("Brush.Muted");
            EditorEmptyHint.Visibility = Visibility.Visible;
            _host.SetActiveNote(null);
        }

        private void LoadNote(LibraryItem note)
        {
            _current = note;
            _suppress = true;
            TitleBox.Text = note.Title;
            TagsBox.Text = note.Tags;
            FolderLabel.Text = string.IsNullOrWhiteSpace(note.FolderName) ? "Geen project" : note.FolderName;
            FolderLabel.Foreground = note.StripBrush;
            FavButton.Content = note.IsFavorite ? "\uE735" : "\uE734";
            FavButton.Foreground = note.IsFavorite ? (Brush)FindResource("Brush.DarkYellow") : (Brush)FindResource("Brush.Muted");

            Editor.Document.Blocks.Clear();
            Editor.Document.PagePadding = new Thickness(0);
            Editor.Document.LineHeight = ThemeManager.LayoutValue("Layout.LineHeight", 25);
            // Tekst in een leeskolom die per lay-out verschilt; 0 betekent de volle breedte.
            var reading = ThemeManager.LayoutValue("Layout.ReadingWidth", 0);
            Editor.MaxWidth = reading > 0 ? reading : double.PositiveInfinity;
            Editor.HorizontalAlignment = HorizontalAlignment.Center;
            AttachmentBar.Visibility = ThemeManager.LayoutValue("Layout.ShowAttachments", 1) > 0 ? Visibility.Visible : Visibility.Collapsed;
            Editor.Document.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;

            var loaded = false;
            if (!string.IsNullOrWhiteSpace(note.ContentRtf))
            {
                try
                {
                    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(note.ContentRtf));
                    var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
                    range.Load(stream, DataFormats.Rtf);
                    loaded = true;
                }
                catch (Exception ex)
                {
                    AppPaths.Log("RTF laden mislukt: " + ex.Message);
                }
            }
            if (!loaded)
            {
                var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
                range.Text = note.Content ?? "";
            }

            _suppress = false;
            _dirty = false;
            _savedAt = note.UpdatedAt;
            UpdateWordCount();
            UpdateSaveStatus();
            SaveDot.Fill = (Brush)FindResource("Brush.Success");
            EditorEmptyHint.Visibility = Visibility.Collapsed;
            EmptyPanel.Visibility = Visibility.Collapsed;
            _aiPane?.SetNote(note.Id);
            _host.SetActiveNote(note);
            RefreshAttachmentBar();

            if (note.Title == "Nieuwe notitie" && string.IsNullOrWhiteSpace(note.Content))
            {
                TitleBox.Focus();
                TitleBox.SelectAll();
            }
        }

        private void MarkDirty()
        {
            if (_suppress || _current == null) return;
            _dirty = true;
            SaveStatus.Text = "Nog niet opgeslagen";
            SaveDot.Fill = (Brush)FindResource("Brush.Muted");
            UpdateWordCount();
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private string GetPlainText()
        {
            var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
            return range.Text.TrimEnd('\r', '\n');
        }

        private string GetRtf()
        {
            try
            {
                var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
                using var stream = new MemoryStream();
                range.Save(stream, DataFormats.Rtf);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
            catch { return ""; }
        }

        private void SaveCurrent()
        {
            _saveTimer.Stop();
            if (_current == null || !_dirty) return;
            try
            {
                _current.Title = string.IsNullOrWhiteSpace(TitleBox.Text) ? "Naamloos" : TitleBox.Text.Trim();
                _current.Content = GetPlainText();
                _current.ContentRtf = GetRtf();
                _current.Tags = TagsBox.Text.Trim();
                NoteHistoryService.CapturePrevious(_current);
                AppServices.Library.UpdateItem(_current);
                NotePackageService.SaveAsync(_current);
                _dirty = false;
                _savedAt = DateTime.Now;
                UpdateSaveStatus();
                SaveDot.Fill = (Brush)FindResource("Brush.Success");
                _current.Raise(nameof(LibraryItem.Title));
                _current.Raise(nameof(LibraryItem.MetaLine));
                _current.Raise(nameof(LibraryItem.DateLabel));
                _host.RefreshHeader();
            }
            catch (Exception ex)
            {
                SaveStatus.Text = "Opslaan mislukt";
                SaveDot.Fill = (Brush)FindResource("Brush.Danger");
                AppPaths.Log("Notitie opslaan mislukt: " + ex.Message);
            }
        }

        private void UpdateWordCount()
        {
            var text = GetPlainText();
            var words = Regex.Matches(text, @"\S+").Count;
            WordCount.Text = words == 1 ? "1 woord" : $"{words} woorden";
        }

        private void UpdateSaveStatus()
        {
            if (_current == null || _dirty) return;
            var at = _savedAt == DateTime.MinValue ? _current.UpdatedAt : _savedAt;
            var age = DateTime.Now - at;
            SaveStatus.Text = age.TotalSeconds < 60
                ? "Opgeslagen: zojuist"
                : age.TotalMinutes < 60
                    ? $"Opgeslagen: {(int)age.TotalMinutes} min geleden"
                    : age.TotalHours < 24
                        ? $"Opgeslagen: {(int)age.TotalHours} uur geleden"
                        : $"Opgeslagen: {at:d MMM, HH:mm}";
        }

        private void NotesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelection) return;
            if (NotesList.SelectedItem is not LibraryItem note) return;
            if (_current != null && note.Id == _current.Id) return;
            SaveCurrent();
            LoadNote(note);
        }

        private void NoteSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (IsLoaded) ApplyFilter();
        }

        private void TitleBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppress || _current == null) return;
            _current.Title = string.IsNullOrWhiteSpace(TitleBox.Text) ? "Naamloos" : TitleBox.Text.Trim();
            _current.Raise(nameof(LibraryItem.Title));
            MarkDirty();
        }

        private void Editor_TextChanged(object sender, TextChangedEventArgs e) => MarkDirty();

        private void TagsBox_TextChanged(object sender, TextChangedEventArgs e) => MarkDirty();

        private void NewNote_Click(object sender, RoutedEventArgs e) => _host.CreateNote(_current?.FolderId);

        private void Favorite_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            SaveCurrent();
            _current.IsFavorite = !_current.IsFavorite;
            AppServices.Library.SetFavorite(_current.Id, _current.IsFavorite);
            _current.Raise(nameof(LibraryItem.IsFavorite));
            _current.Raise(nameof(LibraryItem.FavoriteGlyph));
            FavButton.Content = _current.IsFavorite ? "\uE735" : "\uE734";
            FavButton.Foreground = _current.IsFavorite ? (Brush)FindResource("Brush.DarkYellow") : (Brush)FindResource("Brush.Muted");
            _host.RefreshHeader();
        }

        private void History_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            SaveCurrent();
            var versions = NoteHistoryService.List(_current.Id);
            var menu = new ContextMenu();
            if (versions.Count == 0)
                menu.Items.Add(new MenuItem { Header = "Nog geen eerdere versies", IsEnabled = false });
            else
            {
                foreach (var version in versions)
                {
                    var title = string.IsNullOrWhiteSpace(version.Title) ? "Naamloze notitie" : version.Title;
                    var entry = new MenuItem { Header = $"{version.SavedAt:dd-MM-yyyy HH:mm}  ·  {title}" };
                    entry.Click += (_, _) =>
                    {
                        if (_current == null) return;
                        if (MessageBox.Show(Window.GetWindow(this),
                                $"De inhoud van \"{title}\" van {version.SavedAt:dd-MM-yyyy HH:mm} terugzetten? De huidige inhoud wordt eerst als versie bewaard.",
                                "Notitie herstellen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                        SaveCurrent();
                        NoteHistoryService.Restore(_current, version);
                        LoadNote(_current);
                        AppServices.Notifications.Add("Vorige versie hersteld", title, "\uE777", "Brush.Teal");
                    };
                    menu.Items.Add(entry);
                }
            }
            menu.PlacementTarget = HistoryButton;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void Folder_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            SaveCurrent();
            var dialog = new FolderPickerWindow(AppServices.Library.GetFolders(), _current.FolderId)
            {
                Owner = Window.GetWindow(this)
            };
            if (dialog.ShowDialog() != true) return;
            AppServices.Library.SetFolder(_current.Id, dialog.SelectedFolderId);
            _current.FolderId = dialog.SelectedFolderId;
            _current.FolderName = dialog.SelectedFolderId.HasValue
                ? AppServices.Library.GetFolder(dialog.SelectedFolderId.Value)?.Name
                : null;
            FolderLabel.Text = string.IsNullOrWhiteSpace(_current.FolderName) ? "Geen project" : _current.FolderName;
            _host.RefreshHeader();
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            var title = string.IsNullOrWhiteSpace(_current.Title) ? "deze notitie" : $"‘{_current.Title}’";
            if (!ConfirmDialog.Show(Window.GetWindow(this), "Notitie naar prullenbak?",
                    $"{title} wordt naar de prullenbak verplaatst. Je kunt hem daar later nog herstellen.",
                    "Naar prullenbak", true, "\uE74D")) return;

            _saveTimer.Stop();
            _dirty = false;
            var deletedId = _current.Id;
            AppServices.Library.SoftDelete(deletedId);
            AppServices.Notifications.Add("Notitie naar prullenbak", "Je kunt hem herstellen via de prullenbak.", "\uE74D", "Brush.Danger");
            _current = null;
            _allNotes.RemoveAll(n => n.Id == deletedId);
            NotesList.SelectedItem = null;
            LoadNotes();
            _host.RefreshHeader();
        }

        // ---------- Werkruimte ----------

        private void InitializeWorkspace()
        {
            _restoringWorkspace = true;
            try
            {
                var state = LoadWorkspaceState();
                _workspaceWidth = Math.Clamp(state?.Width ?? 400, 340, 720);
                _workspaceVisible = false;
                if (state?.Tabs.Count > 0)
                {
                    foreach (var saved in state.Tabs.Take(8))
                    {
                        switch (saved.Kind)
                        {
                            case "ai":
                                AddAiTab(select: false, save: false);
                                break;
                            case "file" when !string.IsNullOrWhiteSpace(saved.Path):
                                var fileTab = AddFileTab(saved.Path!, saved.Title, select: false, save: false);
                                if (fileTab == null) continue;
                                fileTab.IsPinned = saved.Pinned;
                                break;
                            default:
                                var savedUrl = string.Equals(saved.Url, "about:blank", StringComparison.OrdinalIgnoreCase)
                                    ? null
                                    : saved.Url;
                                var browserTab = AddBrowserTab(savedUrl, select: false, save: false);
                                browserTab.IsPinned = saved.Pinned;
                                if (!string.IsNullOrWhiteSpace(saved.Title)) browserTab.Title = saved.Title;
                                break;
                        }
                    }
                }
                if (_tabs.Count == 0)
                    AddBrowserTab(null, select: false, save: false);
                _workspaceVisible = false;
                ApplyWorkspaceVisibility();
                var active = Math.Clamp(state?.Active ?? 0, 0, Math.Max(0, _tabs.Count - 1));
                if (_tabs.Count > 0) ActivateTab(_tabs[active], save: false, reveal: false);
                else ShowEmptyWorkspace();
                RebuildTabStrip();
            }
            finally
            {
                _restoringWorkspace = false;
            }
            SaveWorkspace();
        }

        private WorkspaceState? LoadWorkspaceState()
        {
            try
            {
                var raw = AppServices.Library.GetSetting("workspace.state");
                if (string.IsNullOrWhiteSpace(raw)) return null;
                return JsonSerializer.Deserialize<WorkspaceState>(raw);
            }
            catch { return null; }
        }

        private void SaveWorkspace()
        {
            if (_restoringWorkspace) return;
            try
            {
                var state = new WorkspaceState
                {
                    Visible = _workspaceVisible,
                    Width = _workspaceWidth,
                    Active = _activeTab == null ? 0 : Math.Max(0, _tabs.IndexOf(_activeTab)),
                    Tabs = _tabs.Select(tab => new WorkspaceTabState
                    {
                        Kind = tab.Kind switch
                        {
                            WorkspaceKind.Ai => "ai",
                            WorkspaceKind.File => "file",
                            _ => "browser"
                        },
                        Title = tab.Title,
                        Url = tab.Browser?.CurrentUrl ?? tab.Url,
                        Path = tab.Path,
                        Pinned = tab.IsPinned
                    }).ToList()
                };
                AppServices.Library.SetSetting("workspace.state", JsonSerializer.Serialize(state));
                if (WorkspaceColumn.ActualWidth > 0)
                    _workspaceWidth = Math.Clamp(WorkspaceColumn.ActualWidth, 340, 720);
            }
            catch (Exception ex)
            {
                AppPaths.Log("Werkruimte opslaan mislukt: " + ex.Message);
            }
        }

        private void ApplyWorkspaceVisibility()
        {
            if (_workspaceVisible)
            {
                WorkspaceColumn.MinWidth = 340;
                WorkspaceColumn.MaxWidth = 760;
                WorkspaceColumn.Width = new GridLength(_workspaceWidth);
                SplitterColumn.Width = new GridLength(14);
                WorkspaceShell.Visibility = Visibility.Visible;
                WorkspaceSplitter.Visibility = Visibility.Visible;
            }
            else
            {
                WorkspaceColumn.MinWidth = 0;
                WorkspaceColumn.MaxWidth = 0;
                WorkspaceColumn.Width = new GridLength(0);
                SplitterColumn.Width = new GridLength(0);
                WorkspaceShell.Visibility = Visibility.Collapsed;
                WorkspaceSplitter.Visibility = Visibility.Collapsed;
            }
        }

        private void SetWorkspaceVisible(bool visible)
        {
            _workspaceVisible = visible;
            ApplyWorkspaceVisibility();
            SaveWorkspace();
        }

        private void ToggleWorkspace_Click(object sender, RoutedEventArgs e) => SetWorkspaceVisible(!_workspaceVisible);
        private void HideWorkspace_Click(object sender, RoutedEventArgs e) => SetWorkspaceVisible(false);

        private WorkspaceTab AddBrowserTab(string? url, bool select, bool save = true)
        {
            var pane = new BrowserPane(url);
            var tab = new WorkspaceTab
            {
                Kind = WorkspaceKind.Browser,
                Title = "Nieuw tabblad",
                Icon = "\uE774",
                View = pane,
                Browser = pane,
                Url = url
            };
            pane.TitleChanged += (_, title) =>
            {
                tab.Title = string.IsNullOrWhiteSpace(title) ? "Nieuw tabblad" : title;
                tab.Url = pane.CurrentUrl;
                if (ReferenceEquals(tab, _activeTab)) RebuildTabStrip();
                SaveWorkspace();
            };
            pane.NewTabRequested += (_, newUrl) =>
            {
                var fresh = AddBrowserTab(newUrl, select: true);
                ShowWorkspace();
            };
            _tabs.Add(tab);
            if (select) ActivateTab(tab);
            RebuildTabStrip();
            if (save) SaveWorkspace();
            return tab;
        }

        private WorkspaceTab? AddFileTab(string path, string title, bool select, bool save = true)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var pane = new FilePane(path, title);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var tab = new WorkspaceTab
            {
                Kind = WorkspaceKind.File,
                Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileName(path) : title,
                Icon = ext is ".mp3" or ".wav" or ".m4a" or ".ogg" ? "\uE8D6"
                    : ext is ".mp4" or ".mov" or ".webm" ? "\uE714"
                    : ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" ? "\uEB9F" : "\uE8A5",
                View = pane,
                File = pane,
                Path = path
            };
            _tabs.Add(tab);
            if (select) ActivateTab(tab);
            RebuildTabStrip();
            if (save) SaveWorkspace();
            return tab;
        }

        private WorkspaceTab AddAiTab(bool select, bool save = true)
        {
            var existing = _tabs.FirstOrDefault(t => t.Kind == WorkspaceKind.Ai);
            if (existing != null)
            {
                if (select) ActivateTab(existing);
                return existing;
            }
            _aiPane = new AiPane(_host, GetPlainText, () => _current?.Title,
                InsertAiText, ReplaceWithAiText, ApplyAiTitle);
            var tab = new WorkspaceTab
            {
                Kind = WorkspaceKind.Ai,
                Title = "AI-assistent",
                Icon = "\uE945",
                View = _aiPane,
                Ai = _aiPane
            };
            _tabs.Add(tab);
            if (select) ActivateTab(tab);
            RebuildTabStrip();
            if (save) SaveWorkspace();
            return tab;
        }

        private void ApplyAiTitle(string title)
        {
            if (_current == null || string.IsNullOrWhiteSpace(title)) return;
            TitleBox.Text = title;
            SaveCurrent();
        }

        private void ShowWorkspace()
        {
            if (!_workspaceVisible) SetWorkspaceVisible(true);
        }

        private void ActivateTab(WorkspaceTab tab, bool save = true, bool reveal = true)
        {
            _activeTab = tab;
            WorkspaceContent.Children.Clear();
            WorkspaceContent.Children.Add(tab.View);
            WorkspaceEmpty.Visibility = Visibility.Collapsed;
            if (reveal) ShowWorkspace();
            if (tab.Kind == WorkspaceKind.Ai) tab.Ai?.SetNote(_current?.Id);
            RebuildTabStrip();
            if (save) SaveWorkspace();
        }

        private void ShowEmptyWorkspace()
        {
            _activeTab = null;
            WorkspaceContent.Children.Clear();
            WorkspaceEmpty.Visibility = Visibility.Visible;
        }

        private void RebuildTabStrip()
        {
            if (TabStrip == null) return;
            TabStrip.Children.Clear();
            foreach (var tab in _tabs) TabStrip.Children.Add(BuildTabElement(tab));
        }

        private Border BuildTabElement(WorkspaceTab tab)
        {
            var active = ReferenceEquals(tab, _activeTab);
            var tabWidth = GetWorkspaceTabWidth();
            var border = new Border
            {
                Background = (Brush)FindResource(active ? "Brush.AccentSoft" : "Brush.SurfaceAlt"),
                BorderBrush = (Brush)FindResource(active ? "Brush.AccentBorder" : "Brush.Line"),
                BorderThickness = new Thickness(1),
                CornerRadius = (CornerRadius)FindResource("Radius.Pill"),
                Padding = new Thickness(8, 0, 3, 0),
                Width = tabWidth,
                Height = 27,
                Margin = new Thickness(0, 0, 4, 0),
                Cursor = Cursors.Hand,
                Tag = tab,
                ToolTip = tab.Title,
                AllowDrop = true
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new TextBlock
            {
                Text = tab.Icon,
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 10.5,
                Foreground = (Brush)FindResource(tab.Kind == WorkspaceKind.Ai ? "Brush.Accent" : "Brush.Muted"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            };
            grid.Children.Add(icon);

            var label = new TextBlock
            {
                Text = tab.Title,
                FontSize = 10.5,
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = (Brush)FindResource(active ? "Brush.Ink" : "Brush.Muted"),
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = Math.Max(32, tabWidth - 61),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);

            if (tab.IsPinned)
            {
                var pin = new TextBlock
                {
                    Text = "\uE718",
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = 8.5,
                    Foreground = (Brush)FindResource("Brush.Accent"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 0, 0)
                };
                Grid.SetColumn(pin, 2);
                grid.Children.Add(pin);
            }

            var close = new Button
            {
                Style = (Style)FindResource("Btn.IconSmall"),
                Content = "\uE8BB",
                Width = 18,
                Height = 18,
                FontSize = 8,
                Margin = new Thickness(3, 0, 0, 0),
                ToolTip = "Tabblad sluiten",
                Tag = tab
            };
            close.Click += (_, e) =>
            {
                e.Handled = true;
                CloseTab(tab);
            };
            Grid.SetColumn(close, 3);
            grid.Children.Add(close);

            border.Child = grid;
            border.MouseLeftButtonUp += (_, _) => ActivateTab(tab);
            border.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                ShowTabMenu(border, tab);
            };
            border.PreviewMouseLeftButtonDown += (_, e) =>
            {
                _draggingTab = tab;
                _dragStart = e.GetPosition(null);
            };
            border.MouseMove += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed || !ReferenceEquals(_draggingTab, tab)) return;
                var pos = e.GetPosition(null);
                if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                _draggingTab = null;
                try { DragDrop.DoDragDrop(border, new DataObject("SaveSpaceWorkspaceTab", tab), DragDropEffects.Move); } catch { }
            };
            border.DragOver += (_, e) =>
            {
                if (e.Data.GetDataPresent("SaveSpaceWorkspaceTab")) e.Effects = DragDropEffects.Move;
            };
            border.Drop += (_, e) =>
            {
                if (e.Data.GetData("SaveSpaceWorkspaceTab") is not WorkspaceTab dragged || ReferenceEquals(dragged, tab)) return;
                var from = _tabs.IndexOf(dragged);
                var to = _tabs.IndexOf(tab);
                if (from < 0 || to < 0) return;
                _tabs.RemoveAt(from);
                _tabs.Insert(to, dragged);
                RebuildTabStrip();
                SaveWorkspace();
                e.Handled = true;
            };
            return border;
        }

        private void ShowTabMenu(Border target, WorkspaceTab tab)
        {
            var menu = new ContextMenu();
            var pin = new MenuItem { Header = tab.IsPinned ? "Vastmaken ongedaan maken" : "Tabblad vastmaken" };
            pin.Click += (_, _) =>
            {
                tab.IsPinned = !tab.IsPinned;
                if (tab.IsPinned)
                {
                    _tabs.Remove(tab);
                    _tabs.Insert(0, tab);
                }
                RebuildTabStrip();
                SaveWorkspace();
            };
            menu.Items.Add(pin);

            var left = new MenuItem { Header = "Naar links", IsEnabled = _tabs.IndexOf(tab) > 0 };
            left.Click += (_, _) => MoveTab(tab, -1);
            menu.Items.Add(left);
            var right = new MenuItem { Header = "Naar rechts", IsEnabled = _tabs.IndexOf(tab) < _tabs.Count - 1 };
            right.Click += (_, _) => MoveTab(tab, 1);
            menu.Items.Add(right);

            menu.Items.Add(new Separator());
            var close = new MenuItem { Header = "Tabblad sluiten" };
            close.Click += (_, _) => CloseTab(tab);
            menu.Items.Add(close);
            var others = new MenuItem { Header = "Andere tabbladen sluiten" };
            others.Click += (_, _) =>
            {
                var keep = _tabs.Where(t => t.IsPinned && t != tab).ToList();
                var closing = _tabs.Where(t => t != tab && !t.IsPinned).ToList();
                foreach (var victim in closing) DisposeTab(victim);
                _tabs.RemoveAll(t => closing.Contains(t));
                if (!_tabs.Contains(tab) && !keep.Contains(tab)) _tabs.Add(tab);
                if (_activeTab == null && _tabs.Count > 0) ActivateTab(_tabs[0], save: false);
                RebuildTabStrip();
                SaveWorkspace();
            };
            menu.Items.Add(others);

            menu.PlacementTarget = target;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void MoveTab(WorkspaceTab tab, int delta)
        {
            var from = _tabs.IndexOf(tab);
            var to = from + delta;
            if (from < 0 || to < 0 || to >= _tabs.Count) return;
            _tabs.RemoveAt(from);
            _tabs.Insert(to, tab);
            RebuildTabStrip();
            SaveWorkspace();
        }

        private void CloseTab(WorkspaceTab tab)
        {
            var index = _tabs.IndexOf(tab);
            DisposeTab(tab);
            _tabs.Remove(tab);
            if (ReferenceEquals(_activeTab, tab))
            {
                _activeTab = null;
                if (_tabs.Count > 0)
                    ActivateTab(_tabs[Math.Clamp(index, 0, _tabs.Count - 1)], save: false);
                else
                    ShowEmptyWorkspace();
            }
            RebuildTabStrip();
            SaveWorkspace();
        }

        private void DisposeTab(WorkspaceTab tab)
        {
            tab.Browser?.Dispose();
            tab.File?.Dispose();
            if (ReferenceEquals(tab.Ai, _aiPane)) _aiPane = null;
        }

        private void TabStrip_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("SaveSpaceWorkspaceTab")) e.Effects = DragDropEffects.Move;
        }

        private void TabStrip_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData("SaveSpaceWorkspaceTab") is not WorkspaceTab dragged) return;
            _tabs.Remove(dragged);
            _tabs.Add(dragged);
            RebuildTabStrip();
            SaveWorkspace();
        }

        private void WorkspaceSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (WorkspaceColumn.ActualWidth > 0)
                _workspaceWidth = Math.Clamp(WorkspaceColumn.ActualWidth, 340, 720);
            RebuildTabStrip();
            SaveWorkspace();
        }

        private double GetWorkspaceTabWidth()
        {
            var available = WorkspaceColumn?.ActualWidth > 0 ? WorkspaceColumn.ActualWidth : _workspaceWidth;
            var count = Math.Max(1, _tabs.Count);
            return Math.Clamp((available - 74) / count, 108, 190);
        }

        private void AddTab_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            var browser = new MenuItem { Header = "Nieuwe browsertab" };
            browser.Click += (_, _) => { AddBrowserTab(null, true); ShowWorkspace(); };
            menu.Items.Add(browser);
            var ai = new MenuItem { Header = "AI-assistent" };
            ai.Click += (_, _) => { AddAiTab(true); ShowWorkspace(); };
            menu.Items.Add(ai);
            menu.Items.Add(new Separator());
            var document = new MenuItem { Header = "Document uit bibliotheek…" };
            document.Click += (_, _) => OpenDocumentTab();
            menu.Items.Add(document);
            var file = new MenuItem { Header = "Bestand van schijf…" };
            file.Click += (_, _) => OpenFileTab();
            menu.Items.Add(file);

            menu.PlacementTarget = sender as Button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void OpenDocumentTab()
        {
            var docs = AppServices.Library.GetItems(ItemKind.Document, limit: SaveSpace.Services.LibraryService.AllItems);
            if (docs.Count == 0)
            {
                FileActions.ShowError(Window.GetWindow(this), "Er staan nog geen documenten in je bibliotheek. Voeg eerst een document toe via Documenten.");
                return;
            }
            var picker = new ItemPickerWindow(docs, "Document in werkruimte openen") { Owner = Window.GetWindow(this) };
            if (picker.ShowDialog() != true || picker.SelectedItem == null) return;
            var item = picker.SelectedItem;
            AddFileTab(item.FilePath ?? "", item.Title, true);
            ShowWorkspace();
        }

        private void OpenFileTab()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Bestand in werkruimte openen",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            AddFileTab(dialog.FileName, Path.GetFileName(dialog.FileName), true);
            ShowWorkspace();
        }

        private void OpenAiWorkspace_Click(object sender, RoutedEventArgs e)
        {
            AiPopup.IsOpen = false;
            AddAiTab(true);
            ShowWorkspace();
        }

        // ---------- Formatting ----------
        private void Bold_Click(object sender, RoutedEventArgs e) => RunCommand(EditingCommands.ToggleBold);
        private void Italic_Click(object sender, RoutedEventArgs e) => RunCommand(EditingCommands.ToggleItalic);
        private void Underline_Click(object sender, RoutedEventArgs e) => RunCommand(EditingCommands.ToggleUnderline);
        private void Bullets_Click(object sender, RoutedEventArgs e) => RunCommand(EditingCommands.ToggleBullets);
        private void Numbers_Click(object sender, RoutedEventArgs e) => RunCommand(EditingCommands.ToggleNumbering);
        private void Undo_Click(object sender, RoutedEventArgs e) { Editor.Focus(); if (Editor.CanUndo) Editor.Undo(); MarkDirty(); }
        private void Redo_Click(object sender, RoutedEventArgs e) { Editor.Focus(); if (Editor.CanRedo) Editor.Redo(); MarkDirty(); }

        private void RunCommand(RoutedUICommand command)
        {
            if (_current == null) return;
            Editor.Focus();
            command.Execute(null, Editor);
            MarkDirty();
        }

        private void Strike_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            var current = Editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
            var isStrike = current is TextDecorationCollection decorations &&
                           decorations.Any(d => d.Location == TextDecorationLocation.Strikethrough);
            Editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty,
                isStrike ? null : TextDecorations.Strikethrough);
            Editor.Focus();
            MarkDirty();
        }

        private void Heading_Click(object sender, RoutedEventArgs e) => ApplyTextStyle(24, FontWeights.Bold);
        private void Subheading_Click(object sender, RoutedEventArgs e) => ApplyTextStyle(19, FontWeights.SemiBold);
        private void Body_Click(object sender, RoutedEventArgs e) => ApplyTextStyle(15, FontWeights.Normal);

        private void ApplyTextStyle(double size, FontWeight weight)
        {
            if (_current == null) return;
            Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
            Editor.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, weight);
            Editor.Focus();
            MarkDirty();
        }

        private void TextColor_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            foreach (var (label, hex) in new[]
                     {
                         ("Accentrood", "#E32636"), ("Wit", "#F3F3F5"), ("Grijs", "#A8A9B0"),
                         ("Groen", "#7FA268"), ("Blauw", "#6D8CA8"), ("Oranje", "#E8A33D"), ("Paars", "#B07ACC")
                     })
            {
                var item = new MenuItem { Header = label };
                var brush = new SolidColorBrush(ThemeManager.Parse(hex, "#E32636"));
                item.Icon = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = brush };
                item.Click += (_, _) =>
                {
                    Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
                    Editor.Focus();
                    MarkDirty();
                };
                menu.Items.Add(item);
            }
            var reset = new MenuItem { Header = "Standaardkleur" };
            reset.Click += (_, _) =>
            {
                Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, FindResource("Brush.Ink"));
                Editor.Focus();
                MarkDirty();
            };
            menu.Items.Add(new Separator());
            menu.Items.Add(reset);
            menu.PlacementTarget = sender as Button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void FontSize_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            foreach (var size in new[] { 11.0, 12.0, 14.0, 16.0, 18.0, 22.0, 28.0, 36.0 })
            {
                var item = new MenuItem { Header = $"{size:0} pt" };
                var value = size;
                item.Click += (_, _) =>
                {
                    Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, value);
                    Editor.Focus();
                    MarkDirty();
                };
                menu.Items.Add(item);
            }
            var reset = new MenuItem { Header = "Standaardgrootte" };
            reset.Click += (_, _) =>
            {
                Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, 14.5);
                Editor.Focus();
                MarkDirty();
            };
            menu.Items.Add(new Separator());
            menu.Items.Add(reset);
            menu.PlacementTarget = sender as Button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void Checklist_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            Editor.Focus();
            var paragraph = Editor.CaretPosition.Paragraph;
            if (paragraph == null)
            {
                var fresh = new Paragraph(new Run("\u2610 "));
                Editor.Document.Blocks.Add(fresh);
                Editor.CaretPosition = fresh.ContentEnd;
            }
            else
            {
                Editor.CaretPosition.InsertTextInRun("\u2610 ");
            }
            MarkDirty();
        }

        private void Link_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            var selected = new TextRange(Editor.Selection.Start, Editor.Selection.End).Text.Trim();
            var input = PromptDialog.Show(Window.GetWindow(this), "Koppeling invoegen",
                "Plak een webadres. Selecteer eerst tekst als je een eigen linktekst wilt gebruiken.",
                selected.Length > 0 && selected.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? selected : "https://");
            if (string.IsNullOrWhiteSpace(input)) return;
            if (!input.Contains("://", StringComparison.Ordinal)) input = "https://" + input;
            if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                FileActions.ShowError(Window.GetWindow(this), "Dit is geen geldig webadres.");
                return;
            }
            InsertHyperlink(string.IsNullOrWhiteSpace(selected) || selected.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? input : selected, uri);
        }

        private void InsertHyperlink(string label, Uri uri)
        {
            Editor.Focus();
            var start = Editor.Selection.Start;
            var display = string.IsNullOrWhiteSpace(label) ? uri.ToString() : label;
            Editor.Selection.Text = display;
            var end = start.GetPositionAtOffset(display.Length, LogicalDirection.Forward);
            if (end == null) { MarkDirty(); return; }
            var hyperlink = new Hyperlink(start, end)
            {
                NavigateUri = uri,
                ToolTip = uri.ToString(),
                Foreground = (Brush)FindResource("Brush.Link")
            };
            hyperlink.RequestNavigate += (_, args) =>
            {
                args.Handled = true;
                try
                {
                    if (args.Uri.Scheme == "noteagent-attachment")
                    {
                        if (_current == null) throw new InvalidOperationException("Open eerst de notitie met deze bijlage.");
                        var relative = Uri.UnescapeDataString(args.Uri.AbsolutePath.TrimStart('/')).Replace('/', Path.DirectorySeparatorChar);
                        var root = Path.GetFullPath(AppPaths.NoteFilesDir(_current.Id)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                        var path = Path.GetFullPath(Path.Combine(root, relative));
                        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Deze bijlage verwijst buiten de notitiemap.");
                        _host.OpenFilePathInViewer(path);
                    }
                    else if (args.Uri.IsFile) _host.OpenFilePathInViewer(args.Uri.LocalPath);
                    else FileActions.OpenUrl(args.Uri.AbsoluteUri);
                }
                catch (Exception ex) { FileActions.ShowError(Window.GetWindow(this), ex.Message); }
            };
            MarkDirty();
        }

        private void Insert_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            var menu = new ContextMenu();
            var attachment = new MenuItem { Header = "Bestand of afbeelding toevoegen…" };
            attachment.Click += (_, _) => InsertAttachment();
            menu.Items.Add(attachment);
            var link = new MenuItem { Header = "Koppeling invoegen…" };
            link.Click += (_, _) => Link_Click(sender, e);
            menu.Items.Add(link);
            menu.Items.Add(new Separator());
            var table = new MenuItem { Header = "Tabel invoegen…" };
            table.Click += (_, _) => InsertTable();
            menu.Items.Add(table);
            var quote = new MenuItem { Header = "Citaat" };
            quote.Click += (_, _) => InsertQuote();
            menu.Items.Add(quote);
            var code = new MenuItem { Header = "Codeblok" };
            code.Click += (_, _) => InsertCode();
            menu.Items.Add(code);
            var checklist = new MenuItem { Header = "Checklist" };
            checklist.Click += (_, _) => Checklist_Click(sender, e);
            menu.Items.Add(checklist);
            var drawing = new MenuItem { Header = "Tekening maken…" };
            drawing.Click += (_, _) => Drawing_Click(sender, e);
            menu.Items.Add(drawing);

            menu.PlacementTarget = sender as Button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void InsertTable()
        {
            var input = PromptDialog.Show(Window.GetWindow(this), "Tabel invoegen",
                "Geef het aantal rijen en kolommen, bijvoorbeeld 3x3. De tabel komt onderaan de notitie.",
                "3x3");
            if (string.IsNullOrWhiteSpace(input)) return;
            var parts = input.ToLowerInvariant().Split('x', '×', '*');
            if (parts.Length != 2 || !int.TryParse(parts[0].Trim(), out var rows) || !int.TryParse(parts[1].Trim(), out var cols))
            {
                FileActions.ShowError(Window.GetWindow(this), "Gebruik het formaat 3x3.");
                return;
            }
            rows = Math.Clamp(rows, 1, 12);
            cols = Math.Clamp(cols, 1, 8);

            var table = new Table
            {
                CellSpacing = 0,
                BorderBrush = (Brush)FindResource("Brush.Line"),
                BorderThickness = new Thickness(1)
            };
            var group = new TableRowGroup();
            for (var r = 0; r < rows; r++)
            {
                var row = new TableRow();
                for (var c = 0; c < cols; c++)
                {
                    row.Cells.Add(new TableCell(new Paragraph(new Run("")))
                    {
                        BorderBrush = (Brush)FindResource("Brush.Line"),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(6, 4, 6, 4)
                    });
                }
                group.Rows.Add(row);
            }
            table.RowGroups.Add(group);
            Editor.Focus();
            var currentParagraph = Editor.CaretPosition.Paragraph;
            if (currentParagraph != null) Editor.Document.Blocks.InsertAfter(currentParagraph, table);
            else Editor.Document.Blocks.Add(table);
            Editor.CaretPosition = table.ContentEnd;
            MarkDirty();
        }

        private void Drawing_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            var dialog = new DrawingWindow { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true || dialog.PngBytes == null) return;

            try
            {
                var directory = AppPaths.NoteFilesDir(_current.Id);
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "Tekening-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
                var suffix = 2;
                while (File.Exists(path)) path = Path.Combine(directory, $"Tekening-{DateTime.Now:yyyyMMdd-HHmmss} ({suffix++}).png");
                File.WriteAllBytes(path, dialog.PngBytes);
                RefreshAttachmentBar();
                var bitmap = ImageTools.LoadRaw(path, 1200);
                if (bitmap == null) throw new InvalidDataException("De tekening kon niet worden opgeslagen.");
                Editor.Focus();
                Editor.Selection.Text = "";
                var image = new System.Windows.Controls.Image
                {
                    Source = bitmap,
                    MaxWidth = 560,
                    MaxHeight = 440,
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    Margin = new Thickness(0, 8, 0, 8),
                    ToolTip = "Tekening"
                };
                var container = new InlineUIContainer(image, Editor.CaretPosition);
                container.BaselineAlignment = BaselineAlignment.Center;
                Editor.CaretPosition = container.ElementEnd;
                Editor.CaretPosition.InsertLineBreak();
                MarkDirty();
                SaveCurrent();
            }
            catch (Exception ex)
            {
                FileActions.ShowError(Window.GetWindow(this), "De tekening kon niet aan de notitie worden toegevoegd.\n\n" + ex.Message);
            }
        }

        private void BrowserWorkspace_Click(object sender, RoutedEventArgs e)
        {
            AddBrowserTab(null, select: true);
            ShowWorkspace();
        }

        private void InsertQuote()
        {
            Editor.Focus();
            if (Editor.Selection.IsEmpty)
            {
                var paragraph = new Paragraph(new Run("\u201CJe citaat\u201D"))
                {
                    FontStyle = FontStyles.Italic,
                    Foreground = (Brush)FindResource("Brush.Muted"),
                    BorderBrush = (Brush)FindResource("Brush.Accent"),
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(10, 2, 0, 2)
                };
                Editor.Document.Blocks.Add(paragraph);
                Editor.CaretPosition = paragraph.ContentEnd;
            }
            else
            {
                Editor.Selection.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Italic);
                Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, FindResource("Brush.Muted"));
            }
            MarkDirty();
        }

        private void InsertCode()
        {
            Editor.Focus();
            if (Editor.Selection.IsEmpty)
            {
                var paragraph = new Paragraph(new Run("code"))
                {
                    FontFamily = (FontFamily)FindResource("Font.MonoFamily"),
                    FontSize = 12.5,
                    Background = (Brush)FindResource("Brush.SurfaceAlt"),
                    Padding = new Thickness(10, 6, 10, 6)
                };
                Editor.Document.Blocks.Add(paragraph);
                Editor.CaretPosition = paragraph.ContentEnd;
            }
            else
            {
                Editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, FindResource("Font.MonoFamily"));
                Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, 12.5);
                Editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, FindResource("Brush.SurfaceAlt"));
            }
            MarkDirty();
        }

        // ---------- Bijlagebalk onderaan de notitie ----------

        private void RefreshAttachmentBar()
        {
            if (AttachmentList == null) return;
            AttachmentList.Children.Clear();
            if (_current == null)
            {
                AttachmentCount.Text = "";
                return;
            }

            var dir = AppPaths.NoteFilesDir(_current.Id);
            var files = Directory.Exists(dir)
                ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                    .OrderBy(f => Path.GetRelativePath(dir, f), StringComparer.CurrentCultureIgnoreCase).ToList()
                : new List<string>();

            AttachmentCount.Text = files.Count switch
            {
                0 => "Nog geen bijlagen",
                1 => "1 bijlage",
                _ => $"{files.Count} bijlagen"
            };
            foreach (var file in files) AttachmentList.Children.Add(CreateAttachmentChip(file));
        }

        private UIElement CreateAttachmentChip(string path)
        {
            var name = Path.GetRelativePath(AppPaths.NoteFilesDir(_current!.Id), path).Replace('\\', '/');
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock
            {
                Text = AttachmentGlyph(Path.GetExtension(path).ToLowerInvariant()),
                Style = (Style)FindResource("Text.Icon"),
                FontSize = 13,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            content.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 12,
                MaxWidth = 200,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });

            var chip = new Button
            {
                Style = (Style)FindResource("Btn.Ghost"),
                Content = content,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(10, 5, 10, 5),
                ToolTip = name + "\nKlik om te openen. Rechtermuisknop voor meer opties.",
                ContextMenu = new ContextMenu()
            };
            chip.Click += (_, _) => RunSafe(() => _host.OpenFilePathInViewer(path));
            chip.ContextMenu.Items.Add(MenuItemFor("Openen in SaveSpace", () => _host.OpenFilePathInViewer(path)));
            chip.ContextMenu.Items.Add(MenuItemFor("Openen met standaardprogramma (Word, PowerPoint, enz.)", () => FileActions.OpenFile(path)));
            chip.ContextMenu.Items.Add(MenuItemFor("Map tonen", () => FileActions.RevealInExplorer(path)));
            chip.ContextMenu.Items.Add(MenuItemFor("Naam wijzigen…", () => RenameAttachment(path)));
            var organize = new MenuItem { Header = "In map plaatsen…" };
            organize.Click += (_, _) => OrganizeAttachment(path);
            chip.ContextMenu.Items.Add(organize);
            chip.ContextMenu.Items.Add(new Separator());
            chip.ContextMenu.Items.Add(MenuItemFor("Verwijderen uit notitie", () => RemoveAttachment(path)));
            return chip;
        }

        private static string AttachmentGlyph(string ext) => ext switch
        {
            ".mp4" or ".m4v" or ".mov" or ".webm" or ".mkv" or ".avi" or ".wmv" => "\uE714",
            ".mp3" or ".wav" or ".m4a" or ".ogg" or ".flac" or ".aac" or ".wma" => "\uE8D6",
            _ => "\uE8A5"
        };

        private MenuItem MenuItemFor(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => RunSafe(action);
            return item;
        }

        private void RunSafe(Action action)
        {
            try { action(); }
            catch (Exception ex) { FileActions.ShowError(Window.GetWindow(this), ex.Message); }
        }

        private void AddAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            var dialog = new OpenFileDialog
            {
                Title = "Bijlagen aan notitie toevoegen",
                Filter = "Alle bestanden|*.*",
                InitialDirectory = Directory.Exists(AppServices.Settings.AttachmentFolder) ? AppServices.Settings.AttachmentFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                CheckFileExists = true,
                Multiselect = true
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            AddAttachmentFiles(dialog.FileNames);
        }

        private void Attachments_DragOver(object sender, DragEventArgs e)
        {
            var ok = _current != null && e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Attachments_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
            AddAttachmentFiles(paths);
            e.Handled = true;
        }

        /// <summary>Kopieert bestanden naar de bijlagemap van de notitie, ook als het geen afbeelding of document is.</summary>
        private void AddAttachmentFiles(IEnumerable<string> paths)
        {
            if (_current == null) return;
            var dir = AppPaths.NoteFilesDir(_current.Id);
            Directory.CreateDirectory(dir);

            var added = 0;
            foreach (var path in paths.Where(File.Exists))
            {
                if (IsDuplicateAttachment(path, dir))
                {
                    var keep = MessageBox.Show(Window.GetWindow(this),
                        $"\"{Path.GetFileName(path)}\" lijkt inhoudelijk hetzelfde als een bijlage die al in deze notitie staat. Toch nog een kopie toevoegen?",
                        "Dubbele bijlage", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (keep != MessageBoxResult.Yes) continue;
                }
                var destination = Path.Combine(dir, Path.GetFileName(path));
                var name = Path.GetFileNameWithoutExtension(path);
                var extension = Path.GetExtension(path);
                var counter = 2;
                while (File.Exists(destination)) destination = Path.Combine(dir, $"{name} ({counter++}){extension}");
                try
                {
                    File.Copy(path, destination, false);
                    added++;
                }
                catch (Exception ex)
                {
                    FileActions.ShowError(Window.GetWindow(this), $"{Path.GetFileName(path)} kon niet worden gekopieerd.\n\n{ex.Message}");
                }
            }

            if (added == 0) return;
            NotePackageService.SaveAsync(_current);
            RefreshAttachmentBar();
            AppServices.Notifications.Add("Bijlage toegevoegd", added == 1 ? "1 bestand aan de notitie gekoppeld." : $"{added} bestanden aan de notitie gekoppeld.", "\uE8E5", "Brush.Document");
        }

        private static bool IsDuplicateAttachment(string source, string noteDirectory)
        {
            try
            {
                var size = new FileInfo(source).Length;
                foreach (var existing in Directory.EnumerateFiles(noteDirectory, "*", SearchOption.AllDirectories))
                {
                    if (string.Equals(Path.GetFullPath(existing), Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase)) continue;
                    if (new FileInfo(existing).Length != size) continue;
                    using var sourceStream = File.OpenRead(source);
                    using var existingStream = File.OpenRead(existing);
                    if (CryptographicOperations.FixedTimeEquals(SHA256.HashData(sourceStream), SHA256.HashData(existingStream))) return true;
                }
            }
            catch (Exception ex) { AppPaths.Log("Dubbele bijlage controleren mislukt: " + ex.Message); }
            return false;
        }

        private void RenameAttachment(string path)
        {
            if (_current == null || !File.Exists(path)) return;
            var oldRelative = Path.GetRelativePath(AppPaths.NoteFilesDir(_current.Id), path).Replace('\\', '/');
            var proposed = PromptDialog.Show(Window.GetWindow(this), "Bijlage hernoemen", "Kies een nieuwe bestandsnaam.", Path.GetFileName(path));
            if (string.IsNullOrWhiteSpace(proposed)) return;
            var name = Path.GetFileName(proposed.Trim());
            if (name != proposed.Trim() || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                FileActions.ShowError(Window.GetWindow(this), "Gebruik alleen een bestandsnaam zonder maptekens.");
                return;
            }
            var target = Path.Combine(Path.GetDirectoryName(path)!, name);
            if (File.Exists(target)) { FileActions.ShowError(Window.GetWindow(this), "Er bestaat al een bijlage met deze naam in die map."); return; }
            File.Move(path, target);
            var newRelative = Path.GetRelativePath(AppPaths.NoteFilesDir(_current.Id), target).Replace('\\', '/');
            UpdateAttachmentLinks(oldRelative, newRelative);
            SaveCurrent();
            RefreshAttachmentBar();
        }

        private void OrganizeAttachment(string path)
        {
            if (_current == null || !File.Exists(path)) return;
            var folder = PromptDialog.Show(Window.GetWindow(this), "Bijlage ordenen", "Naam van het mapje binnen deze notitie.");
            if (string.IsNullOrWhiteSpace(folder)) return;
            folder = Path.GetFileName(folder.Trim());
            if (folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || folder is "." or "..")
            {
                FileActions.ShowError(Window.GetWindow(this), "Kies een geldige naam voor het mapje.");
                return;
            }
            var directory = Path.Combine(AppPaths.NoteFilesDir(_current.Id), folder);
            Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, Path.GetFileName(path));
            if (File.Exists(target)) { FileActions.ShowError(Window.GetWindow(this), "In dit mapje staat al een bijlage met dezelfde naam."); return; }
            var oldRelative = Path.GetRelativePath(AppPaths.NoteFilesDir(_current.Id), path).Replace('\\', '/');
            File.Move(path, target);
            var newRelative = Path.GetRelativePath(AppPaths.NoteFilesDir(_current.Id), target).Replace('\\', '/');
            UpdateAttachmentLinks(oldRelative, newRelative);
            SaveCurrent();
            RefreshAttachmentBar();
        }

        private void UpdateAttachmentLinks(string oldRelative, string newRelative)
        {
            if (_current == null) return;
            foreach (var block in Editor.Document.Blocks)
            {
                if (block is Paragraph paragraph)
                    foreach (var inline in paragraph.Inlines)
                        if (inline is Hyperlink link && link.NavigateUri?.Scheme == "noteagent-attachment")
                        {
                            var linked = Uri.UnescapeDataString(link.NavigateUri.AbsolutePath.TrimStart('/'));
                            if (string.Equals(linked, oldRelative, StringComparison.OrdinalIgnoreCase))
                                link.NavigateUri = new Uri("noteagent-attachment://open/" + Uri.EscapeDataString(newRelative));
                        }
            }
            _dirty = true;
        }

        private void RemoveAttachment(string path)
        {
            if (_current == null) return;
            var name = Path.GetFileName(path);
            var confirmed = ConfirmDialog.Show(Window.GetWindow(this), "Bijlage verwijderen",
                $"\"{name}\" wordt uit deze notitie verwijderd. Het originele bestand buiten SaveSpace blijft staan. " +
                "Een verwijzing in de tekst blijft staan en werkt dan niet meer.");
            if (!confirmed) return;

            File.Delete(path);
            NotePackageService.SaveAsync(_current);
            RefreshAttachmentBar();
        }

        private void InsertAttachment()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Bestand aan notitie toevoegen",
                Filter = "Bestanden voor notities|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.webp;*.mp4;*.m4v;*.mov;*.wmv;*.avi;*.mkv;*.webm;*.mp3;*.wav;*.m4a;*.ogg;*.flac;*.aac;*.wma;*.pdf;*.doc;*.docx;*.xls;*.xlsx;*.ppt;*.pptx;*.txt;*.md;*.rtf;*.csv;*.epub;*.zip;*.rar;*.7z;*.json;*.xml;*.html;*.htm;*.odt;*.ods;*.odp|Alle bestanden|*.*",
                InitialDirectory = Directory.Exists(AppServices.Settings.AttachmentFolder) ? AppServices.Settings.AttachmentFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            InsertAttachment(dialog.FileName);
        }

        private void Editor_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                return;
            }
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void Editor_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
            if (paths.Length == 0) return;
            Editor.Focus();
            Editor.CaretPosition = Editor.GetPositionFromPoint(e.GetPosition(Editor), true) ?? Editor.Document.ContentEnd;
            foreach (var path in paths.Where(File.Exists)) InsertAttachment(path);
            e.Handled = true;
        }

        private void InsertAttachment(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var kind = ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp" ? ItemKind.Image
                : ext is ".mp4" or ".mov" or ".webm" or ".mkv" or ".avi" ? ItemKind.Video
                : ext is ".mp3" or ".wav" or ".m4a" or ".ogg" or ".flac" or ".aac" or ".wma" ? ItemKind.Audio
                : ItemKind.Document;
            string? copiedFile = null;
            try
            {
                if (_current == null) return;
                var directory = AppPaths.NoteFilesDir(_current.Id);
                Directory.CreateDirectory(directory);
                if (IsDuplicateAttachment(path, directory) && MessageBox.Show(Window.GetWindow(this),
                        $"\"{Path.GetFileName(path)}\" lijkt inhoudelijk hetzelfde als een bijlage die al in deze notitie staat. Toch nog een kopie toevoegen?",
                        "Dubbele bijlage", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                var destination = Path.Combine(directory, Path.GetFileName(path));
                var name = Path.GetFileNameWithoutExtension(path);
                var extension = Path.GetExtension(path);
                var counter = 2;
                while (File.Exists(destination))
                {
                    destination = Path.Combine(directory, $"{name} ({counter++}){extension}");
                }
                File.Copy(path, destination, false);
                copiedFile = destination;
                if (kind == ItemKind.Image)
                {
                    var bitmap = ImageTools.LoadRaw(destination, 1100);
                    if (bitmap == null) throw new InvalidDataException("Deze afbeelding kan niet worden weergegeven.");
                    Editor.Focus();
                    var image = new System.Windows.Controls.Image
                    {
                        Source = bitmap,
                        MaxWidth = 560,
                        MaxHeight = 440,
                        Stretch = System.Windows.Media.Stretch.Uniform,
                        Margin = new Thickness(0, 8, 0, 8),
                        ToolTip = Path.GetFileName(destination)
                    };
                    Editor.Selection.Text = "";
                    var container = new InlineUIContainer(image, Editor.CaretPosition);
                    container.BaselineAlignment = BaselineAlignment.Center;
                    Editor.CaretPosition = container.ElementEnd;
                    Editor.CaretPosition.InsertLineBreak();
                    MarkDirty();
                }
                else
                {
                    var glyph = kind switch { ItemKind.Video => "\uE714", ItemKind.Audio => "\uE8D6", _ => "\uE8A5" };
                    var attachmentUri = new Uri("noteagent-attachment://open/" + Uri.EscapeDataString(Path.GetFileName(destination)));
                    InsertHyperlink(glyph + "  " + Path.GetFileName(destination), attachmentUri);
                }
                SaveCurrent();
                RefreshAttachmentBar();
                AppServices.Notifications.Add("Bijlage toegevoegd", Path.GetFileName(destination), "\uE8E5", "Brush.Document");
            }
            catch (Exception ex)
            {
                try { if (copiedFile != null && File.Exists(copiedFile)) File.Delete(copiedFile); } catch { }
                FileActions.ShowError(Window.GetWindow(this), ex.Message);
            }
        }

        private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SaveCurrent();
                e.Handled = true;
            }
        }

        private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SaveCurrent();
                e.Handled = true;
            }
        }

        // ---------- AI ----------
        private void Ai_Click(object sender, RoutedEventArgs e)
        {
            if (!AppServices.Ai.IsConfigured)
            {
                if (ConfirmDialog.Show(Window.GetWindow(this), "AI is optioneel",
                        "Er is nog geen AI-provider ingesteld. Kies bij Instellingen → AI voor Ollama, OpenAI, Anthropic of een OpenAI-compatibel endpoint. Je API-sleutel wordt veilig in Windows Referentiebeheer bewaard.",
                        "Naar instellingen", false, "\uE945"))
                    _host.Navigate(PageKind.Settings);
                return;
            }
            AiPopup.IsOpen = true;
        }

        private async void AiSummarize_Click(object sender, RoutedEventArgs e)
        {
            AiPopup.IsOpen = false;
            await RunAiAsync("Vat de volgende notitie samen in enkele heldere zinnen. Antwoord alleen met de samenvatting.",
                "Samenvatting", allowReplace: false);
        }

        private async void AiImprove_Click(object sender, RoutedEventArgs e)
        {
            AiPopup.IsOpen = false;
            await RunAiAsync("Herschrijf de volgende notitie helderder en grammaticaal correct. Behoud de toon, betekenis en taal. Antwoord alleen met de herschreven tekst.",
                "Verbeterde tekst", allowReplace: true);
        }

        private async void AiTitle_Click(object sender, RoutedEventArgs e)
        {
            AiPopup.IsOpen = false;
            var text = GetPlainText();
            if (string.IsNullOrWhiteSpace(text))
            {
                FileActions.ShowError(Window.GetWindow(this), "Schrijf eerst wat tekst voordat je een titel laat voorstellen.");
                return;
            }
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var result = await AppServices.Ai.ChatAsync(
                    "Je bent een Nederlandstalige schrijfassistent. Antwoord alleen met de gevraagde tekst.",
                    "Stel één korte, pakkende titel voor deze notitie. Geef alleen de titel, zonder aanhalingstekens of uitleg.\n\n" + text);
                Mouse.OverrideCursor = null;
                var title = result.Trim().Trim('"', '\'', ' ');
                if (string.IsNullOrWhiteSpace(title)) return;
                TitleBox.Text = title.Length > 120 ? title[..120] : title;
                SaveCurrent();
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                FileActions.ShowError(Window.GetWindow(this), ex.Message, "AI");
            }
        }

        private async void AiContinue_Click(object sender, RoutedEventArgs e)
        {
            AiPopup.IsOpen = false;
            await RunAiAsync("Schrijf een kort, passend vervolg op de volgende notitie in dezelfde toon en taal. Antwoord alleen met het vervolg.",
                "Vervolg", allowReplace: false);
        }

        private async Task RunAiAsync(string instruction, string label, bool allowReplace)
        {
            var text = GetPlainText();
            if (string.IsNullOrWhiteSpace(text))
            {
                FileActions.ShowError(Window.GetWindow(this), "Schrijf eerst wat tekst voordat je de AI-assistent gebruikt.");
                return;
            }

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var result = await AppServices.Ai.ChatAsync(
                    "Je bent een behulpzame Nederlandstalige schrijfassistent. Antwoord bondig, zonder inleiding of meta-uitleg.",
                    instruction + "\n\n---\n" + text);
                Mouse.OverrideCursor = null;

                var dialog = new AiResultWindow(label, result,
                    insert: value => InsertAiText(value),
                    replace: allowReplace ? value => ReplaceWithAiText(value) : null)
                {
                    Owner = Window.GetWindow(this)
                };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                FileActions.ShowError(Window.GetWindow(this), ex.Message, "AI");
            }
        }

        private void InsertAiText(string text)
        {
            if (_current == null) return;
            if (!Editor.Selection.IsEmpty)
                Editor.Selection.Text = text;
            else
                Editor.CaretPosition.InsertTextInRun(text);
            Editor.Focus();
            MarkDirty();
        }

        private void ReplaceWithAiText(string text)
        {
            if (_current == null) return;
            var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
            range.Text = text;
            Editor.Focus();
            MarkDirty();
        }
    }
}
