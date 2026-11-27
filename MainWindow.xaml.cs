using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using SaveSpace.Models;
using SaveSpace.Services;
using SaveSpace.Views;
using SaveSpace.Views.Dialogs;

namespace SaveSpace
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(420) };
        private readonly MediaImportService _imports = new();
        private readonly List<long> _openNoteIds = new();
        private PageKind _currentPage = PageKind.Home;
        private object? _currentParameter;
        private bool _suppressSearch;
        private LibraryItem? _activeNote;

        private sealed class SwitcherEntry
        {
            public LibraryItem Note { get; init; } = null!;
            public string Title { get; init; } = "";
            public string DateLabel { get; init; } = "";
            public string FavoriteGlyph { get; init; } = "";
            public string IconGlyph { get; init; } = "\uE70B";
            public Brush IconBrush { get; init; } = Brushes.Gray;
            public Brush IconSoftBrush { get; init; } = Brushes.Transparent;
            public Visibility ActiveVisibility { get; init; } = Visibility.Collapsed;
            public Visibility OpenVisibility { get; init; } = Visibility.Collapsed;
        }

        public PageKind CurrentPage => _currentPage;

        public MainWindow()
        {
            InitializeComponent();
            NotificationsList.ItemsSource = AppServices.Notifications.Items;
            AppServices.Notifications.PropertyChanged += Notifications_PropertyChanged;
            Loaded += MainWindow_Loaded;
            StateChanged += MainWindow_StateChanged;
            Closed += (_, _) => AppServices.Notifications.PropertyChanged -= Notifications_PropertyChanged;
            _searchTimer.Tick += SearchTimer_Tick;
            SearchBox.KeyDown += SearchBox_KeyDown;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            SaveSpace.Services.StartupLog.Mark("Hoofdvenster geladen");
            LoadOpenNotes();
            RefreshHeader();
            UpdateProfile();
            Navigate(PageKind.Home);
            if (AppServices.Notifications.Items.Count == 0)
            {
                AppServices.Notifications.Add("Welkom bij SaveSpace",
                    "Alles wat je bewaart blijft lokaal. Koppel je eigen opslagmap via Instellingen.",
                    "\uE8B7", "Brush.Folder");
            }
        }

        private void Notifications_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NotificationService.UnreadCount))
                Dispatcher.Invoke(RefreshBadge);
        }

        public void RefreshHeader()
        {
            try
            {
                var stats = AppServices.Library.GetStats();
                LibraryLabel.Text = $"{AppServices.Settings.LibraryName} — {stats.TotalItems} items";
                LibraryLabel.ToolTip = AppPaths.Root;
                RefreshBadge();
                NotificationsEmpty.Visibility = AppServices.Notifications.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                UpdateSwitcherLabel();
                UpdateProfile();
            }
            catch (Exception ex)
            {
                AppPaths.Log("Header verversen mislukt: " + ex.Message);
            }
        }

        private void UpdateSwitcherLabel()
        {
            if (SwitcherLabel == null || SwitcherIcon == null) return;
            if (_activeNote == null)
            {
                SwitcherLabel.Text = "Mijn bibliotheek";
                SwitcherIcon.Text = "\uE8B7";
                LibrarySwitcherButton.ToolTip = "Wissel van notitie";
                return;
            }
            SwitcherLabel.Text = string.IsNullOrWhiteSpace(_activeNote.Title) ? "Naamloos" : _activeNote.Title;
            SwitcherIcon.Text = "\uE70B";
            LibrarySwitcherButton.ToolTip = string.IsNullOrWhiteSpace(_activeNote.FolderName)
                ? _activeNote.Title
                : $"{_activeNote.FolderName} — {_activeNote.Title}";
        }

        // ================= OPEN NOTITIES =================

        public void SetActiveNote(LibraryItem? note)
        {
            _activeNote = note;
            if (note != null) TrackNote(note.Id, persist: false);
            UpdateSwitcherLabel();
            PersistOpenNotes();
        }

        private void TrackNote(long noteId, bool persist = true)
        {
            _openNoteIds.Remove(noteId);
            _openNoteIds.Insert(0, noteId);
            if (_openNoteIds.Count > 8) _openNoteIds.RemoveRange(8, _openNoteIds.Count - 8);
            if (persist) PersistOpenNotes();
        }

        private void PersistOpenNotes()
        {
            try
            {
                AppServices.Library.SetSetting("ui.openNotes", string.Join(",", _openNoteIds));
                AppServices.Library.SetSetting("ui.activeNote", _activeNote?.Id.ToString() ?? "");
            }
            catch (Exception ex)
            {
                AppPaths.Log("Open notities opslaan mislukt: " + ex.Message);
            }
        }

        private void LoadOpenNotes()
        {
            try
            {
                _openNoteIds.Clear();
                var raw = AppServices.Library.GetSetting("ui.openNotes");
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (!long.TryParse(part, out var id)) continue;
                        if (_openNoteIds.Contains(id)) continue;
                        var note = AppServices.Library.GetItem(id);
                        if (note == null || note.DeletedAt.HasValue || note.Kind != ItemKind.Note) continue;
                        _openNoteIds.Add(id);
                        if (_openNoteIds.Count >= 8) break;
                    }
                }

                var activeRaw = AppServices.Library.GetSetting("ui.activeNote");
                if (long.TryParse(activeRaw, out var activeId))
                {
                    var active = AppServices.Library.GetItem(activeId);
                    if (active != null && !active.DeletedAt.HasValue && active.Kind == ItemKind.Note)
                    {
                        _activeNote = active;
                        if (!_openNoteIds.Contains(activeId)) TrackNote(activeId, persist: false);
                    }
                }
                if (_activeNote == null && _openNoteIds.Count > 0)
                    _activeNote = AppServices.Library.GetItem(_openNoteIds[0]);
            }
            catch (Exception ex)
            {
                AppPaths.Log("Open notities laden mislukt: " + ex.Message);
            }
        }

        private void RefreshBadge()
        {
            var unread = AppServices.Notifications.UnreadCount;
            BellBadge.Visibility = unread > 0 ? Visibility.Visible : Visibility.Collapsed;
            BellBadgeText.Text = unread > 99 ? "99+" : unread.ToString();
        }

        private bool _suppressThemeToggle;

        private void UpdateProfile()
        {
            var name = string.IsNullOrWhiteSpace(AppServices.Settings.UserName) ? Environment.UserName : AppServices.Settings.UserName;
            ProfileName.Text = name;
            ProfilePath.Text = AppPaths.Root;
            AvatarInitials.Text = Initials(name);
            if (DarkModeToggle != null)
            {
                _suppressThemeToggle = true;
                DarkModeToggle.IsChecked = AppServices.Settings.IsDark;
                _suppressThemeToggle = false;
            }
        }

        private void ThemeToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressThemeToggle) return;
            var dark = DarkModeToggle.IsChecked == true;
            SetTheme(dark);
        }

        public void ToggleTheme() => SetTheme(!AppServices.Settings.IsDark);

        private void SetTheme(bool dark)
        {
            AppServices.Settings.Theme = dark ? "dark" : "light";
            ThemeManager.Apply(AppServices.Settings);
            UpdateProfile();
            RefreshHeader();
            if (PageHost.Content is IRefreshable refreshable) refreshable.Refresh();
        }

        private static string Initials(string name)
        {
            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) return (parts[0][..1] + parts[1][..1]).ToUpperInvariant();
            return name.Length >= 2 ? name[..2].ToUpperInvariant() : name.ToUpperInvariant();
        }

        // ================= NAVIGATION =================
        public void Navigate(PageKind page, object? parameter = null)
        {
            try
            {
                _currentPage = page;
                _currentParameter = parameter;
                NewPopup.IsOpen = false;
                NotificationsPopup.IsOpen = false;
                ProfilePopup.IsOpen = false;
                LibrarySwitcherPopup.IsOpen = false;

                FrameworkElement view = page switch
                {
                    PageKind.Home => new HomeView(this),
                    PageKind.Notes => new NotesView(this, parameter is long id ? id : null),
                    PageKind.Links => new LinksView(this),
                    PageKind.Images => new ImagesView(this),
                    PageKind.Videos => new VideosView(this),
                    PageKind.Documents => new DocumentsView(this),
                    PageKind.Audio => new DocumentsView(this, "audio"),
                    PageKind.Favorites => new FavoritesView(this),
                    PageKind.Folders => new FoldersView(this, parameter is long folderId ? folderId : null),
                    PageKind.Trash => new TrashView(this),
                    PageKind.Settings => new SettingsView(this),
                    PageKind.Search => new SearchView(this, parameter as string ?? ""),
                    PageKind.LinkedFolder => new LinkedFolderView(this, parameter as LinkedFolder),
                    _ => new HomeView(this)
                };

                PageHost.Content = view;
                SetActiveNav(page);
                AnimatePage();
                Title = page switch
                {
                    PageKind.Home => "SaveSpace",
                    PageKind.Notes => "SaveSpace — Notities",
                    PageKind.Links => "SaveSpace — Weblinks",
                    PageKind.Images => "SaveSpace — Afbeeldingen",
                    PageKind.Videos => "SaveSpace — Video's",
                    PageKind.Documents => "SaveSpace — Documenten",
                    PageKind.Audio => "SaveSpace — Muziek",
                    PageKind.Favorites => "SaveSpace — Favorieten",
                    PageKind.Folders => "SaveSpace — Mappen",
                    PageKind.Trash => "SaveSpace — Prullenbak",
                    PageKind.Settings => "SaveSpace — Instellingen",
                    PageKind.Search => "SaveSpace — Zoeken",
                    PageKind.LinkedFolder => "SaveSpace — Gekoppelde map",
                    _ => "SaveSpace"
                };
                RefreshHeader();
            }
            catch (Exception ex)
            {
                AppPaths.Log("Navigatie mislukt: " + ex);
                FileActions.ShowError(this, "Deze pagina kon niet worden geopend.\n\n" + ex.Message);
            }
        }

        private void SetActiveNav(PageKind page)
        {
            if (page == PageKind.Settings)
                SettingsButton.SetResourceReference(BackgroundProperty, "Brush.AccentSoft");
            else
                SettingsButton.ClearValue(BackgroundProperty);
        }

        private void AnimatePage()
        {
            if (!ThemeManager.AnimationsEnabled)
            {
                PageHost.Opacity = 1;
                PageTranslate.Y = 0;
                return;
            }
            var slide = ThemeManager.LayoutValue("Layout.PageSlide", 12);
            var duration = TimeSpan.FromMilliseconds(ThemeManager.LayoutValue("Layout.PageDuration", 240));
            PageHost.Opacity = 0;
            PageTranslate.Y = slide;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
            PageTranslate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(slide, 0, duration) { EasingFunction = ease });
        }

        public void RefreshCurrent()
        {
            if (PageHost.Content is IRefreshable refreshable) refreshable.Refresh();
            RefreshHeader();
        }

        public void RefreshAfterChange()
        {
            RefreshHeader();
            if (PageHost.Content is IRefreshable refreshable) refreshable.Refresh();
        }

        public void ApplyTheme()
        {
            ThemeManager.Apply(AppServices.Settings);
            RefreshHeader();
        }

        // ================= HEADER ACTIONS =================
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }
            try { DragMove(); } catch { }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressSearch) return;
            _searchTimer.Stop();
            if (string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                if (_currentPage == PageKind.Search) Navigate(PageKind.Home);
                return;
            }
            _searchTimer.Start();
        }

        private void SearchTimer_Tick(object? sender, EventArgs e)
        {
            _searchTimer.Stop();
            if (_suppressSearch) return;
            var query = SearchBox.Text.Trim();
            if (query.Length == 0) return;
            Navigate(PageKind.Search, query);
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _searchTimer.Stop();
                Navigate(PageKind.Search, SearchBox.Text.Trim());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ClearSearch();
                Navigate(PageKind.Home);
                e.Handled = true;
            }
        }

        private void ClearSearch()
        {
            if (string.IsNullOrEmpty(SearchBox.Text)) return;
            _suppressSearch = true;
            SearchBox.Text = "";
            _suppressSearch = false;
        }

        private void NewButton_Click(object sender, RoutedEventArgs e)
        {
            NewPopup.IsOpen = true;
            if (NewPopup.Child is FrameworkElement child)
            {
                child.Opacity = 0;
                child.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
            }
        }

        private void NewNote_Click(object sender, RoutedEventArgs e)
        {
            NewPopup.IsOpen = false;
            CreateNote();
        }

        private void OpenNotePackage_Click(object sender, RoutedEventArgs e)
        {
            NewPopup.IsOpen = false;
            var dialog = new OpenFileDialog
            {
                Title = "SaveSpace-notitie openen",
                Filter = "SaveSpace-notitie (*.noteagent)|*.noteagent|Alle bestanden (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true) return;
            OpenNotePackagePath(dialog.FileName);
        }

        public void OpenNotePackagePath(string packagePath)
        {
            if (!File.Exists(packagePath)) return;
            try
            {
                var manifest = NotePackageService.Read(packagePath);
                var existing = AppServices.Library.GetItemByUuid(manifest.Uuid);
                if (existing?.Kind == ItemKind.Note && !existing.DeletedAt.HasValue)
                {
                    Navigate(PageKind.Notes, existing.Id);
                    return;
                }

                var note = new LibraryItem
                {
                    Kind = ItemKind.Note,
                    Title = manifest.Title,
                    Content = manifest.Content,
                    ContentRtf = manifest.ContentRtf,
                    Tags = manifest.Tags,
                    Uuid = manifest.Uuid ?? ""
                };
                note.Id = AppServices.Library.InsertItem(note);
                NotePackageService.ExtractAttachments(packagePath, note.Id);
                NotePackageService.SaveAsync(note);
                Navigate(PageKind.Notes, note.Id);
            }
            catch (Exception ex)
            {
                FileActions.ShowError(this, "Dit notitiebestand kon niet worden geopend.\n\n" + ex.Message);
            }
        }

        public async Task<(int Imported, int Skipped, List<string> Errors)> ImportNotePackagesAsync(string[] paths)
        {
            var errors = new List<string>();
            var imported = 0;
            var skipped = 0;
            var dialog = new ProgressDialog("Notities samenvoegen", $"0 van {paths.Length} bestanden", indeterminate: false) { Owner = this };
            dialog.Show();
            try
            {
                IProgress<ImportProgress> progress = new Progress<ImportProgress>(p => dialog.UpdateProgress(p.Current, p.Total, $"{p.Current} van {p.Total} · {p.FileName}"));
                (imported, skipped, errors) = await Task.Run(() =>
                {
                    var localImported = 0;
                    var localSkipped = 0;
                    var localErrors = new List<string>();
                    for (var index = 0; index < paths.Length; index++)
                    {
                        var path = paths[index];
                        try
                        {
                            var manifest = NotePackageService.Read(path);
                            if (!string.IsNullOrWhiteSpace(manifest.Uuid) && AppServices.Library.GetItemByUuid(manifest.Uuid) != null)
                            {
                                localSkipped++;
                                continue;
                            }
                            var note = new LibraryItem
                            {
                                Kind = ItemKind.Note,
                                Title = manifest.Title,
                                Content = manifest.Content,
                                ContentRtf = manifest.ContentRtf,
                                Tags = manifest.Tags,
                                Uuid = manifest.Uuid ?? ""
                            };
                            AppServices.Library.InsertItem(note);
                            try
                            {
                                NotePackageService.ExtractAttachments(path, note.Id);
                                NotePackageService.Save(note);
                            }
                            catch
                            {
                                AppServices.Library.DeletePermanently(note.Id);
                                throw;
                            }
                            localImported++;
                        }
                        catch (Exception ex) { localErrors.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
                        finally { progress.Report(new ImportProgress(index + 1, paths.Length, Path.GetFileName(path))); }
                    }
                    return (localImported, localSkipped, localErrors);
                });
            }
            finally { dialog.Close(); }

            if (imported > 0)
            {
                Navigate(PageKind.Notes);
                AppServices.Notifications.Add("Notities toegevoegd", $"{imported} notitie(s) samengevoegd met je bibliotheek.", "\uE8E5", "Brush.Note");
            }
            return (imported, skipped, errors);
        }

        public void CreateNote(long? folderId = null)
        {
            var folder = folderId.HasValue ? AppServices.Library.GetFolder(folderId.Value) : null;
            var note = new LibraryItem
            {
                Kind = ItemKind.Note,
                Title = "Nieuwe notitie",
                Content = "",
                FolderId = folder?.Id,
                FolderName = folder?.Name ?? ""
            };
            AppServices.Library.InsertItem(note);
            AppServices.Notifications.Add(
                folder == null ? "Notitie aangemaakt" : $"Notitie in {folder.Name}",
                folder == null ? "Je nieuwe notitie staat klaar om te schrijven." : "De notitie staat meteen in dit project.",
                "\uE70B", "Brush.Note");
            TrackNote(note.Id, persist: false);
            SetActiveNote(note);
            Navigate(PageKind.Notes, note.Id);
        }

        private void NewLink_Click(object sender, RoutedEventArgs e)
        {
            NewPopup.IsOpen = false;
            CreateLink();
        }

        public void CreateLink()
        {
            ClearSearch();
            Navigate(PageKind.Links);
            if (PageHost.Content is LinksView links) links.OpenAddDialog();
        }

        private void NewImages_Click(object sender, RoutedEventArgs e) => ImportMedia(ItemKind.Image);
        private void NewVideos_Click(object sender, RoutedEventArgs e) => ImportMedia(ItemKind.Video);
        private void NewDocuments_Click(object sender, RoutedEventArgs e) => ImportMedia(ItemKind.Document);

        private void NewFolder_Click(object sender, RoutedEventArgs e)
        {
            NewPopup.IsOpen = false;
            CreateFolder();
        }

        public void CreateFolder()
        {
            ClearSearch();
            Navigate(PageKind.Folders);
            if (PageHost.Content is FoldersView folders) folders.OpenCreateDialog();
        }

        public async void ImportMedia(ItemKind kind, long? folderId = null, bool navigateToPage = true)
        {
            var dialog = new OpenFileDialog
            {
                Multiselect = true,
                CheckFileExists = true,
                Title = kind switch
                {
                    ItemKind.Image => "Afbeeldingen importeren",
                    ItemKind.Video => "Video's importeren",
                    ItemKind.Audio => "Muziek importeren",
                    _ => "Documenten toevoegen"
                },
                Filter = kind switch
                {
                    ItemKind.Image => "Afbeeldingen|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.tif;*.tiff;*.ico;*.webp|Alle bestanden|*.*",
                    ItemKind.Video => "Video's|*.mp4;*.m4v;*.mov;*.wmv;*.avi;*.mkv;*.webm;*.mpg;*.mpeg|Alle bestanden|*.*",
                    ItemKind.Audio => "Muziek|*.mp3;*.wav;*.m4a;*.ogg;*.flac;*.aac;*.wma|Alle bestanden|*.*",
                    _ => "Documenten|*.pdf;*.doc;*.docx;*.xls;*.xlsx;*.ppt;*.pptx;*.txt;*.md;*.rtf;*.csv;*.epub;*.zip;*.rar;*.7z;*.json;*.xml;*.html;*.odt;*.ods;*.odp|Alle bestanden|*.*"
                }
            };
            if (dialog.ShowDialog(this) != true) return;

            var progressDialog = new ProgressDialog("Bestanden importeren", $"0 van {dialog.FileNames.Length} bestanden") { Owner = this };
            progressDialog.Show();
            ImportResult result;
            try
            {
                IProgress<ImportProgress> progress = new Progress<ImportProgress>(p =>
                    progressDialog.UpdateProgress(p.Current, p.Total, $"{p.Current} van {p.Total} · {p.FileName}"));
                result = await Task.Run(() => _imports.Import(dialog.FileNames, kind, folderId, progress, path =>
                    Dispatcher.Invoke(() => MessageBox.Show(this,
                        $"\"{System.IO.Path.GetFileName(path)}\" is al met dezelfde inhoud in je bibliotheek aanwezig. Toch nog een kopie toevoegen?",
                        "Dubbel bestand", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)));
            }
            catch (Exception ex)
            {
                FileActions.ShowError(this, "Bestanden importeren is niet gelukt.\n\n" + ex.Message);
                return;
            }
            finally
            {
                progressDialog.Close();
            }

            if (result.Items.Count > 0)
            {
                var label = kind switch { ItemKind.Image => "afbeelding(en)", ItemKind.Video => "video('s)", ItemKind.Audio => "muziekbestand(en)", _ => "document(en)" };
                AppServices.Notifications.Add($"{result.Items.Count} {label} toegevoegd",
                    "De originelen zijn niet aangepast. SaveSpace bewaart een eigen kopie.", "\uE710",
                    kind == ItemKind.Image ? "Brush.Image" : kind == ItemKind.Video ? "Brush.Video" : kind == ItemKind.Audio ? "Brush.Accent" : "Brush.Document");
                if (navigateToPage)
                {
                    Navigate(kind switch { ItemKind.Image => PageKind.Images, ItemKind.Video => PageKind.Videos, ItemKind.Audio => PageKind.Audio, _ => PageKind.Documents });
                }
                else
                {
                    RefreshAfterChange();
                }
            }

            if (result.Errors.Count > 0)
            {
                var message = string.Join("\n", result.Errors.Take(6));
                if (result.Errors.Count > 6) message += $"\n— en nog {result.Errors.Count - 6} fout(en).";
                FileActions.ShowError(this, "Sommige bestanden konden niet worden toegevoegd:\n\n" + message);
            }
        }

        private void BellButton_Click(object sender, RoutedEventArgs e)
        {
            AppServices.Notifications.MarkAllRead();
            RefreshBadge();
            NotificationsPopup.IsOpen = true;
        }

        private void MarkNotificationsRead_Click(object sender, RoutedEventArgs e)
        {
            AppServices.Notifications.MarkAllRead();
            RefreshBadge();
        }

        private void ClearNotifications_Click(object sender, RoutedEventArgs e)
        {
            AppServices.Notifications.Clear();
            RefreshBadge();
            NotificationsEmpty.Visibility = Visibility.Visible;
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ProfilePopup.IsOpen = false;
            ClearSearch();
            Navigate(PageKind.Settings);
        }

        private void AiButton_Click(object sender, RoutedEventArgs e)
        {
            if (!AppServices.Ai.IsConfigured)
            {
                if (ConfirmDialog.Show(this, "AI is optioneel",
                        "Er is nog geen AI-provider ingesteld. Kies bij Instellingen ? AI voor Ollama, OpenAI, Anthropic of een OpenAI-compatibel endpoint. Je API-sleutel wordt veilig in Windows Referentiebeheer bewaard.",
                        "Naar instellingen", false, "\uE945"))
                    Navigate(PageKind.Settings);
                return;
            }
            new AiAssistantWindow { Owner = this }.Show();
        }

        private void Brand_Click(object sender, RoutedEventArgs e)
        {
            ClearSearch();
            Navigate(PageKind.Home);
        }

        private void LibrarySwitcher_Click(object sender, RoutedEventArgs e)
        {
            var folders = AppServices.Library.GetFolders().ToDictionary(f => f.Id);
            var entries = new List<SwitcherEntry>();
            var seen = new HashSet<long>();
            foreach (var id in _openNoteIds)
            {
                var note = AppServices.Library.GetItem(id);
                if (note == null || note.DeletedAt.HasValue || note.Kind != ItemKind.Note) continue;
                if (!seen.Add(note.Id)) continue;
                entries.Add(BuildSwitcherEntry(note, true, folders));
            }
            foreach (var note in AppServices.Library.GetItems(ItemKind.Note, limit: 12))
            {
                if (!seen.Add(note.Id)) continue;
                entries.Add(BuildSwitcherEntry(note, false, folders));
            }

            SwitcherNotesList.ItemsSource = entries;
            SwitcherEmpty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            LibrarySwitcherPopup.IsOpen = !LibrarySwitcherPopup.IsOpen;
        }

        private SwitcherEntry BuildSwitcherEntry(LibraryItem note, bool isOpen, IReadOnlyDictionary<long, Folder> folders)
        {
            var isActive = _activeNote?.Id == note.Id;
            var icon = (Brush)FindResource("Brush.Note");
            var soft = (Brush)FindResource("Brush.NoteSoft");
            if (note.FolderId is long folderId && folders.TryGetValue(folderId, out var folder))
            {
                var color = ThemeManager.Parse(folder.Color, "#E32636");
                icon = new SolidColorBrush(color);
                soft = new SolidColorBrush(Color.FromArgb(38, color.R, color.G, color.B));
            }
            if (isActive)
            {
                icon = (Brush)FindResource("Brush.Accent");
                soft = (Brush)FindResource("Brush.AccentSoft");
            }
            return new SwitcherEntry
            {
                Note = note,
                Title = string.IsNullOrWhiteSpace(note.Title) ? "Naamloos" : note.Title,
                DateLabel = note.DateLabel,
                FavoriteGlyph = note.FavoriteGlyph,
                IconBrush = icon,
                IconSoftBrush = soft,
                ActiveVisibility = isActive ? Visibility.Visible : Visibility.Collapsed,
                OpenVisibility = isOpen ? Visibility.Visible : Visibility.Collapsed
            };
        }

        // ================= PROJECTACTIES =================

        private void ProjectMenu_Click(object sender, RoutedEventArgs e)
        {
            var folder = _activeNote?.FolderId is long folderId ? AppServices.Library.GetFolder(folderId) : null;
            var menu = new ContextMenu();

            if (folder != null)
            {
                menu.Items.Add(new MenuItem
                {
                    Header = folder.Name,
                    IsEnabled = false,
                    FontWeight = FontWeights.SemiBold
                });
                menu.Items.Add(new Separator());
            }

            AddProjectMenuItem(menu, "Nieuwe notitie", "\uE710", () => CreateNote(folder?.Id));
            if (_activeNote != null)
                AddProjectMenuItem(menu, "Notitie met bijlagen exporteren…", "\uE898", ExportActiveNote);
            AddProjectMenuItem(menu, "Document openen", "\uE8A5", () => ShowNotesWorkspace(ActiveNoteId(), view => view.OpenDocumentPicker()));
            AddProjectMenuItem(menu, "Bestand invoegen", "\uE723", () =>
            {
                var id = EnsureNoteForAction();
                if (id == null) return;
                ShowNotesWorkspace(id, view => view.InsertFileFromDisk());
            });
            AddProjectMenuItem(menu, "Browser openen", "\uE774", () => ShowNotesWorkspace(ActiveNoteId(), view => view.OpenBrowserWorkspace()));

            if (folder != null)
            {
                menu.Items.Add(new Separator());
                AddProjectMenuItem(menu, "Project hernoemen", "\uE8AC", () => RenameProject(folder));
                AddProjectMenuItem(menu, "Projectinstellingen", "\uE713", () =>
                {
                    ClearSearch();
                    Navigate(PageKind.Folders, folder.Id);
                });
                AddProjectMenuItem(menu, "Project verwijderen", "\uE74D", () => DeleteProject(folder));
            }

            menu.Items.Add(new Separator());
            AddProjectMenuItem(menu, "Instellingen", "\uE713", () =>
            {
                ClearSearch();
                Navigate(PageKind.Settings);
            });

            menu.PlacementTarget = sender as UIElement;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void AddProjectMenuItem(ContextMenu menu, string header, string glyph, Action action)
        {
            var item = new MenuItem
            {
                Header = header,
                Icon = new TextBlock
                {
                    Text = glyph,
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = 12,
                    Foreground = (Brush)FindResource("Brush.Muted")
                }
            };
            item.Click += (_, _) =>
            {
                menu.IsOpen = false;
                try { action(); }
                catch (Exception ex) { FileActions.ShowError(this, ex.Message); }
            };
            menu.Items.Add(item);
        }

        private long? ActiveNoteId() => _activeNote?.Id;

        private async void ExportActiveNote()
        {
            var note = _activeNote;
            if (note == null) return;
            var dialog = new SaveFileDialog
            {
                Title = "Notitie exporteren",
                Filter = "SaveSpace-notitie met bijlagen (*.noteagent)|*.noteagent|Markdown-bestand (*.md)|*.md|Tekstbestand (*.txt)|*.txt",
                FilterIndex = 1,
                DefaultExt = ".noteagent",
                FileName = SafeFileName(note.Title) + ".noteagent",
                AddExtension = true,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != true) return;
            var latest = AppServices.Library.GetItem(note.Id) ?? note;
            var isPackage = string.Equals(System.IO.Path.GetExtension(dialog.FileName), ".noteagent", StringComparison.OrdinalIgnoreCase);
            var progress = new ProgressDialog("Notitie exporteren", "Notitie en bijlagen klaarmaken…", indeterminate: true) { Owner = this };
            progress.Show();
            try
            {
                await Task.Run(() =>
                {
                    if (isPackage)
                    {
                        NotePackageService.Save(latest);
                        File.Copy(NotePackageService.PackagePath(latest.Id), dialog.FileName, true);
                    }
                    else NoteExport.SaveAs(latest, dialog.FileName);
                });
                AppServices.Notifications.Add("Notitie geëxporteerd", System.IO.Path.GetFileName(dialog.FileName), "\uE8E5", "Brush.Document");
            }
            catch (Exception ex)
            {
                FileActions.ShowError(this, "De notitie kon niet worden geëxporteerd.\n\n" + ex.Message);
            }
            finally { progress.Close(); }
        }

        private static string SafeFileName(string title)
        {
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var clean = new string((string.IsNullOrWhiteSpace(title) ? "Naamloos" : title.Trim())
                .Select(c => invalid.Contains(c) ? '-' : c).ToArray());
            return clean.Length > 80 ? clean[..80] : clean;
        }

        private long? EnsureNoteForAction()
        {
            if (_activeNote != null) return _activeNote.Id;
            var recent = AppServices.Library.GetItems(ItemKind.Note, limit: 1).FirstOrDefault();
            if (recent != null)
            {
                SetActiveNote(recent);
                return recent.Id;
            }
            CreateNote();
            return _activeNote?.Id;
        }

        private void ShowNotesWorkspace(long? noteId, Action<NotesView> action)
        {
            NotesView? existing = PageHost.Content as NotesView;
            if (_currentPage != PageKind.Notes || existing == null ||
                (noteId.HasValue && existing.CurrentNoteId != noteId.Value))
            {
                ClearSearch();
                Navigate(PageKind.Notes, noteId);
                existing = PageHost.Content as NotesView;
            }
            if (existing != null) action(existing);
        }

        private void RenameProject(Folder folder)
        {
            var name = PromptDialog.Show(this, "Project hernoemen", "Kies een korte, herkenbare naam.", folder.Name);
            if (string.IsNullOrWhiteSpace(name) || name.Trim() == folder.Name) return;
            AppServices.Library.UpdateFolder(folder.Id, name, folder.Color, folder.ParentId);
            if (_activeNote?.FolderId == folder.Id) _activeNote.FolderName = name.Trim();
            AppServices.Notifications.Add("Project hernoemd", name.Trim(), "\uE8B7", "Brush.Folder");
            RefreshAfterChange();
            SetActiveNote(_activeNote);
        }

        private void DeleteProject(Folder folder)
        {
            if (!ConfirmDialog.Show(this, "Project verwijderen?",
                    $"—{folder.Name}— wordt verwijderd. De notities en bestanden zelf blijven bewaard en verhuizen naar je bibliotheek.",
                    "Project verwijderen", true, "\uE74D"))
                return;
            AppServices.Library.DeleteFolder(folder.Id);
            if (_activeNote?.FolderId == folder.Id)
            {
                _activeNote.FolderId = null;
                _activeNote.FolderName = "";
            }
            AppServices.Notifications.Add("Project verwijderd", folder.Name, "\uE74D", "Brush.Danger");
            RefreshAfterChange();
            SetActiveNote(_activeNote);
        }

        private void SwitcherNote_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: LibraryItem note }) return;
            LibrarySwitcherPopup.IsOpen = false;
            ClearSearch();
            SetActiveNote(note);
            Navigate(PageKind.Notes, note.Id);
        }

        private void SwitcherAll_Click(object sender, RoutedEventArgs e)
        {
            LibrarySwitcherPopup.IsOpen = false;
            ClearSearch();
            Navigate(PageKind.Notes);
        }

        private void SwitcherNew_Click(object sender, RoutedEventArgs e)
        {
            LibrarySwitcherPopup.IsOpen = false;
            CreateNote();
        }

        private void LibrarySwitcherClose_Click(object sender, RoutedEventArgs e) =>
            LibrarySwitcherPopup.IsOpen = false;

        private void ProfileButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateProfile();
            ProfilePopup.IsOpen = true;
        }

        private void ProfileBackup_Click(object sender, RoutedEventArgs e)
        {
            ProfilePopup.IsOpen = false;
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var path = AppServices.Backups.CreateBackup(false);
                Mouse.OverrideCursor = null;
                RefreshHeader();
                MessageBox.Show(this,
                    "De database is veilig opgeslagen:\n\n" + path +
                    "\n\nWil je ook je foto's, video's en documenten in de back-up? Maak dan een volledige back-up via Instellingen ? Back-ups.",
                    "Back-up gemaakt", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                FileActions.ShowError(this, "De back-up kon niet worden gemaakt.\n\n" + ex.Message);
            }
        }

        private void OpenStorage_Click(object sender, RoutedEventArgs e)
        {
            ProfilePopup.IsOpen = false;
            try { FileActions.OpenFolder(AppPaths.Root); }
            catch (Exception ex) { FileActions.ShowError(this, ex.Message); }
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            ProfilePopup.IsOpen = false;
            var stats = AppServices.Library.GetStats();
            MessageBox.Show(this,
                "SaveSpace 1.0\n\n" +
                "Een warme, lokale bibliotheek voor notities, weblinks, afbeeldingen, video's en documenten.\n\n" +
                $"Items: {stats.TotalItems}\nFavorieten: {stats.Favorites}\nMappen: {stats.Folders}\n\n" +
                $"Opslag: {AppPaths.Root}\n\n" +
                "Alles blijft op deze computer. Er worden geen bestanden naar internet gestuurd, tenzij je zelf een AI-provider gebruikt.",
                "Over SaveSpace", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ================= OPEN ITEMS =================
        public void OpenItem(LibraryItem item, IList<LibraryItem>? context = null)
        {
            switch (item.Kind)
            {
                case ItemKind.Note:
                    Navigate(PageKind.Notes, item.Id);
                    break;
                case ItemKind.Link:
                    try { FileActions.OpenUrl(item.Url); }
                    catch (Exception ex) { FileActions.ShowError(this, ex.Message); }
                    break;
                case ItemKind.Image:
                    new ImageViewerWindow(item, context ?? new List<LibraryItem> { item }) { Owner = this }.Show();
                    break;
                case ItemKind.Video:
                    new VideoPlayerWindow(item) { Owner = this }.Show();
                    break;
                case ItemKind.Document:
                    new DocumentViewerWindow(item, context ?? new List<LibraryItem> { item }) { Owner = this }.Show();
                    break;
                case ItemKind.Audio:
                    new DocumentViewerWindow(item, context ?? new List<LibraryItem> { item }) { Owner = this }.Show();
                    break;
            }
        }

        public void OpenFilePathInViewer(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("De bijlage is niet gevonden.", path);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var kind = MediaImportService.ImageExtensions.Contains(ext) ? ItemKind.Image
                : MediaImportService.VideoExtensions.Contains(ext) ? ItemKind.Video
                : MediaImportService.AudioExtensions.Contains(ext) ? ItemKind.Audio
                : ItemKind.Document;
            var item = new LibraryItem
            {
                Kind = kind,
                Title = Path.GetFileNameWithoutExtension(path),
                FilePath = path,
                Mime = MediaImportService.MimeFor(ext),
                SizeBytes = new FileInfo(path).Length
            };
            OpenItem(item);
        }

        public void RestartApp()
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                    Process.Start(new ProcessStartInfo { FileName = exe, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppPaths.Log("Herstarten mislukt: " + ex.Message);
            }
            Application.Current.Shutdown();
        }

        // ================= WINDOW CHROME =================
        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (MaximizeButton == null) return;
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
            MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Herstellen" : "Maximaliseren";
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }
            else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
            {
                CreateNote();
                e.Handled = true;
            }
            else if (e.Key == Key.D && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                ToggleTheme();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                NewPopup.IsOpen = false;
                NotificationsPopup.IsOpen = false;
                ProfilePopup.IsOpen = false;
            }
        }
    }
}
