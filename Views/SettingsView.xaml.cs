using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SaveSpace.Models;
using SaveSpace.Services;
using SaveSpace.Views.Dialogs;

namespace SaveSpace.Views
{
    public partial class SettingsView : UserControl, IRefreshable
    {
        private sealed record MissingFileEntry(string Title, string Path);
        private static readonly string[] Accents = { "#E32636", "#D9772E", "#3FB5A6", "#7FA268", "#6D8CA8", "#B07ACC", "#C9A227", "#A8A9B0" };

        private readonly MainWindow _host;
        private bool _loading;
        private string _provider = "none";

        public SettingsView(MainWindow host)
        {
            InitializeComponent();
            _host = host;
            Loaded += (_, _) => Reload();
        }

        public void Refresh() => Reload();

        private void Reload()
        {
            if (!IsLoaded && !IsInitialized) return;
            _loading = true;
            try
            {
                var settings = AppServices.Settings;
                ThemeDark.IsChecked = settings.Theme == "dark";
                ThemeLight.IsChecked = settings.Theme == "light";
                ThemeSystem.IsChecked = settings.Theme == "system";
                RadiusSlider.Value = settings.Radius;
                RadiusValue.Text = settings.Radius.ToString("0.#");
                FontSlider.Value = settings.FontScale;
                FontValue.Text = $"{settings.FontScale * 100:0}%";
                AnimationsToggle.IsChecked = settings.Animations;
                CompactToggle.IsChecked = settings.Compact;
                LayoutPresetBox.SelectedValue = settings.LayoutPreset;
                UpdateLayoutPreview(settings.LayoutPreset);
                WidgetFavorites.IsChecked = settings.HomeWidgetFavorites;
                WidgetProjects.IsChecked = settings.HomeWidgetProjects;
                WidgetNotes.IsChecked = settings.HomeWidgetNotes;
                WidgetDocuments.IsChecked = settings.HomeWidgetDocuments;
                WidgetImages.IsChecked = settings.HomeWidgetImages;
                WidgetVideos.IsChecked = settings.HomeWidgetVideos;
                WidgetAudio.IsChecked = settings.HomeWidgetAudio;
                WidgetLinks.IsChecked = settings.HomeWidgetLinks;
                WidgetTrash.IsChecked = settings.HomeWidgetTrash;
                WidgetBackup.IsChecked = settings.HomeWidgetBackup;
                BuildHomeBackgrounds();
                LibraryNameBox.Text = settings.LibraryName;
                AttachmentFolderBox.Text = settings.AttachmentFolder;
                BackupMirrorFolderBox.Text = settings.BackupMirrorFolder;

                StoragePathText.Text = AppPaths.Root;
                StorageStatusText.Text = "";

                LastBackupText.Text = settings.LastBackup.HasValue
                    ? "Laatste: " + settings.LastBackup.Value.ToString("d MMM yyyy, HH:mm")
                    : "Nog geen back-up";
                AutoBackupToggle.IsChecked = settings.AutoBackup;
                KeepBox.Text = settings.BackupKeep.ToString();
                TrashRetentionBox.SelectedValue = settings.TrashRetentionDays.ToString();
                ReloadBackups();

                _provider = settings.AiProvider;
                EndpointBox.Text = settings.AiEndpoint;
                ModelBox.Text = settings.AiModel;
                BuildProviders();
                UpdateProviderHint();
                UpdateKeyStatus();
                AiStatusText.Text = "";

                BrowserHomeBox.Text = settings.BrowserHome;
                EngineDuck.IsChecked = settings.SearchEngine == "duckduckgo";
                EngineGoogle.IsChecked = settings.SearchEngine == "google";
                EngineBing.IsChecked = settings.SearchEngine == "bing";
                BrowserStatusText.Text = "";
                PrivacyStatusText.Text = "";

                SpellcheckToggle.IsChecked = settings.EditorSpellcheck;
                HighContrastToggle.IsChecked = settings.HighContrast;

                BuildAccents();
                LoadAbout();
            }
            finally
            {
                _loading = false;
            }
        }

        private void LoadAbout()
        {
            var stats = AppServices.Library.GetStats();
            AboutStatsText.Text =
                $"{stats.TotalItems} items · {stats.Notes} notities · {stats.Folders} projecten · " +
                $"{stats.Documents} documenten · {stats.Images} afbeeldingen · {stats.Videos} video's";
            AboutStorageText.Text = $"{AppPaths.Root}  ·  {LibraryItem.FormatSize(stats.TotalBytes)} in gebruik";
        }

        // ---------------- Navigatie ----------------

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton { Tag: string tag }) return;
            foreach (var chip in new[] { NavGeneral, NavStorage, NavAi, NavBrowser, NavEditor, NavAbout })
                chip.IsChecked = ReferenceEquals(chip, sender);

            PanelGeneral.Visibility = tag == "general" ? Visibility.Visible : Visibility.Collapsed;
            PanelStorage.Visibility = tag == "storage" ? Visibility.Visible : Visibility.Collapsed;
            PanelAi.Visibility = tag == "ai" ? Visibility.Visible : Visibility.Collapsed;
            PanelBrowser.Visibility = tag == "browser" ? Visibility.Visible : Visibility.Collapsed;
            PanelEditor.Visibility = tag == "editor" ? Visibility.Visible : Visibility.Collapsed;
            PanelAbout.Visibility = tag == "about" ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---------------- Weergave ----------------

        private void Theme_Click(object sender, RoutedEventArgs e)
        {
            if (_loading || sender is not ToggleButton { Tag: string theme }) return;
            AppServices.Settings.Theme = theme;
            ThemeDark.IsChecked = theme == "dark";
            ThemeLight.IsChecked = theme == "light";
            ThemeSystem.IsChecked = theme == "system";
            _host.ApplyTheme();
            BuildAccents();
        }

        private void LayoutPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || LayoutPresetBox.SelectedItem is not ComboBoxItem { Tag: string preset }) return;
            var settings = AppServices.Settings;
            settings.LayoutPreset = preset;
            switch (preset)
            {
                case "claude": settings.Theme = "dark"; settings.Compact = false; settings.Radius = 10; settings.FontScale = 1.05; settings.Animations = true; break;
                case "apple": settings.Theme = "dark"; settings.Compact = false; settings.Radius = 14; settings.FontScale = 1.0; settings.Animations = true; break;
                case "focus": settings.Theme = "dark"; settings.Compact = false; settings.Radius = 1; settings.FontScale = 1.12; settings.Animations = false; break;
                case "library": settings.Theme = "dark"; settings.Compact = true; settings.Radius = 4; settings.FontScale = 1.0; settings.Animations = true; break;
            }
            _host.ApplyTheme();
            Reload();
        }

        private void UpdateLayoutPreview(string preset)
        {
            (LayoutPreviewTitle.Text, LayoutPreviewText.Text) = preset switch
            {
                "apple" => ("Apple-stijl · Liquid Glass", "Lichte glaslagen, zacht kleurlicht en een rustige, duidelijke bediening."),
                "focus" => ("Focus", "Bijna zwart, nauwelijks chroom, een serif voor het schrijven."),
                "library" => ("Bibliotheek", "Bijna zwart, rustige grijstinten en een slank rood accent."),
                _ => ("Claude-stijl", "Warm donker palet, serif-koppen en ruime leesbreedte.")
            };
        }

        private void HomeWidget_Click(object sender, RoutedEventArgs e)
        {
            if (_loading || sender is not CheckBox { Tag: string key } check) return;
            var settings = AppServices.Settings;
            var enabled = check.IsChecked == true;
            switch (key)
            {
                case "favorites": settings.HomeWidgetFavorites = enabled; break;
                case "projects": settings.HomeWidgetProjects = enabled; break;
                case "notes": settings.HomeWidgetNotes = enabled; break;
                case "documents": settings.HomeWidgetDocuments = enabled; break;
                case "images": settings.HomeWidgetImages = enabled; break;
                case "videos": settings.HomeWidgetVideos = enabled; break;
                case "audio": settings.HomeWidgetAudio = enabled; break;
                case "links": settings.HomeWidgetLinks = enabled; break;
                case "trash": settings.HomeWidgetTrash = enabled; break;
                case "backup": settings.HomeWidgetBackup = enabled; break;
            }
        }

        private void BuildHomeBackgrounds()
        {
            if (HomeBackgroundPanel == null) return;
            HomeBackgroundPanel.Children.Clear();
            var colors = new (string Name, string Hex)[]
            {
                ("Standaard", ""), ("Rood", "#650000"), ("Bordeaux", "#43121D"),
                ("Blauw", "#122B49"), ("Groen", "#15372B"), ("Paars", "#2B1A43"), ("Amber", "#442A14")
            };
            foreach (var (name, hex) in colors)
            {
                var selected = string.Equals(hex, AppServices.Settings.HomeBackgroundColor, StringComparison.OrdinalIgnoreCase);
                var chip = new Button
                {
                    Width = 34,
                    Height = 34,
                    Margin = new Thickness(0, 0, 8, 6),
                    Padding = new Thickness(2),
                    Background = new SolidColorBrush(hex.Length == 0 ? ThemeManager.Parse("#45464C", "#45464C") : ThemeManager.Parse(hex, "#650000")),
                    BorderBrush = new SolidColorBrush(selected ? ThemeManager.Parse(AppServices.Settings.Accent, "#E32636") : Colors.Transparent),
                    BorderThickness = new Thickness(selected ? 2 : 1),
                    Tag = hex,
                    ToolTip = name,
                    Cursor = Cursors.Hand
                };
                chip.Click += HomeBackground_Click;
                HomeBackgroundPanel.Children.Add(chip);
            }
        }

        private void HomeBackground_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string hex }) return;
            AppServices.Settings.HomeBackgroundColor = hex;
            BuildHomeBackgrounds();
            _host.ApplyTheme();
        }

        private void Compact_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppServices.Settings.Compact = CompactToggle.IsChecked == true;
            _host.ApplyTheme();
        }

        private void BuildAccents()
        {
            if (AccentPanel == null) return;
            AccentPanel.Children.Clear();
            var current = AppServices.Settings.Accent;
            foreach (var hex in Accents)
            {
                var selected = string.Equals(hex, current, StringComparison.OrdinalIgnoreCase);
                var color = ThemeManager.Parse(hex, "#E32636");
                var swatch = new Border
                {
                    Width = 30,
                    Height = 30,
                    Margin = new Thickness(0, 0, 7, 7),
                    CornerRadius = new CornerRadius(9),
                    Background = new SolidColorBrush(color),
                    BorderThickness = new Thickness(selected ? 2 : 1),
                    BorderBrush = selected ? (Brush)FindResource("Brush.Ink") : (Brush)FindResource("Brush.Line"),
                    Cursor = Cursors.Hand,
                    ToolTip = hex
                };
                swatch.MouseLeftButtonUp += (_, _) =>
                {
                    AppServices.Settings.Accent = hex;
                    _host.ApplyTheme();
                    BuildAccents();
                };
                AccentPanel.Children.Add(swatch);
            }
        }

        private void RadiusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (RadiusValue == null) return;
            RadiusValue.Text = e.NewValue.ToString("0.#");
            if (_loading) return;
            AppServices.Settings.Radius = e.NewValue;
            _host.ApplyTheme();
        }

        private void FontSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (FontValue == null) return;
            FontValue.Text = $"{e.NewValue * 100:0}%";
            if (_loading) return;
            AppServices.Settings.FontScale = e.NewValue;
            _host.ApplyTheme();
        }

        private void Animations_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppServices.Settings.Animations = AnimationsToggle.IsChecked == true;
            _host.ApplyTheme();
        }

        private void ResetAppearance_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmDialog.Show(Window.GetWindow(this), "Vormgeving herstellen?",
                    "Thema, accentkleur, afronding, tekstgrootte en animaties gaan terug naar de standaardwaarden. Je notities en bestanden blijven staan.",
                    "Herstellen", false, "\uE777")) return;
            AppServices.Settings.ApplyDefaultsAfterReset();
            _host.ApplyTheme();
            Reload();
        }

        private void SaveLibraryName_Click(object sender, RoutedEventArgs e)
        {
            var name = LibraryNameBox.Text.Trim();
            AppServices.Settings.LibraryName = string.IsNullOrWhiteSpace(name) ? "Mijn bibliotheek" : name;
            LibraryNameBox.Text = AppServices.Settings.LibraryName;
            _host.RefreshHeader();
            AppServices.Notifications.Add("Naam opgeslagen", AppServices.Settings.LibraryName, "\uE73E", "Brush.Sage");
        }

        private void ChooseAttachmentFolder_Click(object sender, RoutedEventArgs e)
        {
            var folder = FileActions.PickFolder(Window.GetWindow(this), "Kies de standaardmap voor bijlagen");
            if (string.IsNullOrWhiteSpace(folder)) return;
            AppServices.Settings.AttachmentFolder = folder;
            AttachmentFolderBox.Text = folder;
        }

        private void ChooseBackupMirrorFolder_Click(object sender, RoutedEventArgs e)
        {
            var folder = FileActions.PickFolder(Window.GetWindow(this), "Kies een map voor een tweede back-upkopie");
            if (string.IsNullOrWhiteSpace(folder)) return;
            AppServices.Settings.BackupMirrorFolder = folder;
            BackupMirrorFolderBox.Text = folder;
        }

        private void ClearBackupMirrorFolder_Click(object sender, RoutedEventArgs e)
        {
            AppServices.Settings.BackupMirrorFolder = "";
            BackupMirrorFolderBox.Text = "";
        }

        private void RegisterNotePackage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var appExe = Path.Combine(AppContext.BaseDirectory, "SaveSpace.exe");
                if (!File.Exists(appExe)) appExe = Environment.ProcessPath ?? throw new FileNotFoundException("Het SaveSpace-programma is niet gevonden.");
                using var extension = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.noteagent");
                extension.SetValue("", "SaveSpace.NotePackage");
                using var app = Registry.CurrentUser.CreateSubKey(@"Software\Classes\SaveSpace.NotePackage");
                app.SetValue("", "SaveSpace-notitie met bijlagen");
                using var icon = Registry.CurrentUser.CreateSubKey(@"Software\Classes\SaveSpace.NotePackage\DefaultIcon");
                icon.SetValue("", $"\"{appExe}\",0");
                using var command = Registry.CurrentUser.CreateSubKey(@"Software\Classes\SaveSpace.NotePackage\shell\open\command");
                command.SetValue("", $"\"{appExe}\" \"%1\"");
                FileAssociationStatus.Text = "Dubbelklik op .noteagent-bestanden om ze met SaveSpace te openen.";
            }
            catch (Exception ex)
            {
                FileAssociationStatus.Text = "Koppelen is niet gelukt: " + ex.Message;
                AppPaths.Log("Bestandstype koppelen mislukt: " + ex);
            }
        }

        // ---------------- Opslag ----------------

        private void OpenStorage_Click(object sender, RoutedEventArgs e)
        {
            try { FileActions.OpenFolder(AppPaths.Root); }
            catch (Exception ex) { FileActions.ShowError(Window.GetWindow(this), ex.Message); }
        }

        private async void ImportNotePackages_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "SaveSpace-notities samenvoegen",
                Filter = "SaveSpace-notitie (*.noteagent)|*.noteagent",
                CheckFileExists = true,
                Multiselect = true
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            try
            {
                var summary = await _host.ImportNotePackagesAsync(dialog.FileNames);
                MergeStatusText.Text = $"{summary.Imported} toegevoegd · {summary.Skipped} al aanwezig · {summary.Errors.Count} fout(en).";
                if (summary.Errors.Count > 0)
                    FileActions.ShowError(Window.GetWindow(this), "Sommige notities konden niet worden toegevoegd:\n\n" + string.Join("\n", summary.Errors.Take(8)));
            }
            catch (Exception ex)
            {
                MergeStatusText.Text = "Samenvoegen is niet gelukt.";
                FileActions.ShowError(Window.GetWindow(this), ex.Message);
            }
        }

        private void MoveStorage_Click(object sender, RoutedEventArgs e)
        {
            var target = FileActions.PickFolder(Window.GetWindow(this), "Kies de nieuwe opslagmap");
            if (string.IsNullOrWhiteSpace(target)) return;
            var answer = MessageBox.Show(Window.GetWindow(this),
                $"SaveSpace kopieert je bibliotheek naar:\n{target}\n\nDe huidige map blijft daarna ongewijzigd bestaan. De app moet opnieuw starten om de nieuwe map te gebruiken.\n\nDoorgaan?",
                "Opslag verhuizen", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                var newRoot = AppPaths.MigrateStorage(target);
                StoragePathText.Text = newRoot;
                StorageStatusText.Text = "Verhuisd. SaveSpace start nu opnieuw met de nieuwe map.";
                StorageStatusText.Foreground = (Brush)FindResource("Brush.Success");
                Mouse.OverrideCursor = null;
                if (MessageBox.Show(Window.GetWindow(this), "De bibliotheek is verhuisd. Nu opnieuw starten?",
                        "SaveSpace", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                    RestartApp();
            }
            catch (Exception ex)
            {
                StorageStatusText.Text = ex.Message;
                StorageStatusText.Foreground = (Brush)FindResource("Brush.Danger");
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        // ---------------- Back-ups ----------------

        private void ReloadBackups()
        {
            var backups = AppServices.Backups.List();
            BackupList.ItemsSource = backups;
            NoBackupsText.Visibility = backups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            LastBackupText.Text = AppServices.Settings.LastBackup.HasValue
                ? "Laatste: " + AppServices.Settings.LastBackup.Value.ToString("d MMM yyyy, HH:mm")
                : "Nog geen back-up";
        }

        private void CheckMissingFiles_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var missing = AppServices.Library.GetItems(limit: LibraryService.AllItems)
                    .Where(item => !string.IsNullOrWhiteSpace(item.FilePath) && !File.Exists(item.FilePath))
                    .Select(item => new MissingFileEntry(item.Title, item.FilePath!))
                    .OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
                MissingFilesList.ItemsSource = missing;
                MissingFilesStatus.Text = missing.Count == 0
                    ? "Alles staat op de verwachte plek."
                    : $"{missing.Count} bestand(en) ontbreken. De notitie of bibliotheekverwijzing blijft bewaard.";
            }
            catch (Exception ex)
            {
                MissingFilesStatus.Text = "Controleren is niet gelukt: " + ex.Message;
            }
        }

        private void Backup_Click(object sender, RoutedEventArgs e) => CreateBackup(false);
        private void FullBackup_Click(object sender, RoutedEventArgs e) => CreateBackup(true);

        private void CreateBackup(bool includeMedia)
        {
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                var path = AppServices.Backups.CreateBackup(includeMedia);
                AppServices.Settings.LastBackup = DateTime.Now;
                AppServices.Backups.Prune();
                ReloadBackups();
                AppServices.Notifications.Add("Back-up gemaakt",
                    includeMedia ? "Volledige back-up inclusief bestanden." : "Compacte back-up van je database.",
                    "\uE8F4", "Brush.Sage");
                AppPaths.Log("Back-up gemaakt: " + path);
            }
            catch (Exception ex)
            {
                FileActions.ShowError(Window.GetWindow(this), "De back-up kon niet worden gemaakt.\n\n" + ex.Message);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void OpenBackups_Click(object sender, RoutedEventArgs e)
        {
            try { FileActions.OpenFolder(AppPaths.BackupsDir); }
            catch (Exception ex) { FileActions.ShowError(Window.GetWindow(this), ex.Message); }
        }

        private void AutoBackup_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppServices.Settings.AutoBackup = AutoBackupToggle.IsChecked == true;
        }

        private void TrashRetention_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || TrashRetentionBox.SelectedValue is not string value || !int.TryParse(value, out var days)) return;
            AppServices.Settings.TrashRetentionDays = days;
        }

        private void KeepBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(KeepBox.Text, out var keep))
                AppServices.Settings.BackupKeep = Math.Clamp(keep, 1, 50);
            KeepBox.Text = AppServices.Settings.BackupKeep.ToString();
        }

        private void RestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: BackupInfo backup }) return;
            if (!ConfirmDialog.Show(Window.GetWindow(this), "Back-up herstellen?",
                    $"Je huidige bibliotheek wordt vervangen door de back-up van {backup.DateLabel}. Er wordt eerst een veiligheidskopie van je huidige database gemaakt. SaveSpace start daarna opnieuw.",
                    "Herstellen", true, "\uE777")) return;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                AppServices.Backups.Restore(backup.Path);
                AppPaths.Log("Back-up hersteld: " + backup.Path);
                Mouse.OverrideCursor = null;
                RestartApp();
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                FileActions.ShowError(Window.GetWindow(this), "De back-up kon niet worden hersteld.\n\n" + ex.Message);
            }
        }

        private void DeleteBackup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: BackupInfo backup }) return;
            if (!ConfirmDialog.Show(Window.GetWindow(this), "Back-up verwijderen?",
                    $"«{backup.Name}» wordt definitief verwijderd. Dit kan niet ongedaan worden gemaakt.",
                    "Verwijderen", true, "\uE74D")) return;
            AppServices.Backups.Delete(backup.Path);
            ReloadBackups();
        }

        // ---------------- AI ----------------

        private void BuildProviders()
        {
            ProviderPanel.Children.Clear();
            foreach (var (key, label, icon, _) in AiService.Providers)
            {
                var button = new ToggleButton
                {
                    Style = (Style)FindResource("Btn.Nav"),
                    Tag = key,
                    IsChecked = _provider == key,
                    Margin = new Thickness(0, 0, 6, 6)
                };
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(new TextBlock
                {
                    Text = icon,
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 6, 0)
                });
                content.Children.Add(new TextBlock { Text = label });
                button.Content = content;
                button.Click += (_, _) =>
                {
                    _provider = key;
                    foreach (var child in ProviderPanel.Children.OfType<ToggleButton>())
                        child.IsChecked = ReferenceEquals(child, button);
                    AppServices.Settings.AiProvider = key;
                    if (string.IsNullOrWhiteSpace(EndpointBox.Text))
                        EndpointBox.Text = AiService.DefaultEndpointFor(key);
                    ModelBox.Text = AppServices.Settings.AiModel;
                    UpdateProviderHint();
                    UpdateKeyStatus();
                };
                ProviderPanel.Children.Add(button);
            }
        }

        private void UpdateProviderHint()
        {
            var info = AiService.Providers.FirstOrDefault(p => p.Key == _provider);
            ProviderHint.Text = info.Hint ?? "";
            if (_provider == "none")
                ProviderHint.Text = "Kies een provider om de assistent te gebruiken.";
            else if (string.IsNullOrWhiteSpace(EndpointBox.Text))
                EndpointBox.Text = AiService.DefaultEndpointFor(_provider);
        }

        private void UpdateKeyStatus()
        {
            if (_provider == "ollama")
            {
                KeyStatusText.Text = "Ollama heeft geen API-sleutel nodig.";
                KeyStatusText.Foreground = (Brush)FindResource("Brush.Muted");
            }
            else if (_provider == "none")
            {
                KeyStatusText.Text = "Kies eerst een provider.";
                KeyStatusText.Foreground = (Brush)FindResource("Brush.Muted");
            }
            else if (AppServices.Ai.HasKey(_provider))
            {
                KeyStatusText.Text = "Er is een sleutel opgeslagen in Windows Referentiebeheer.";
                KeyStatusText.Foreground = (Brush)FindResource("Brush.Success");
            }
            else
            {
                KeyStatusText.Text = "Nog geen sleutel opgeslagen.";
                KeyStatusText.Foreground = (Brush)FindResource("Brush.Muted");
            }
        }

        private bool SaveAiSettings(bool quiet = false)
        {
            if (_provider == "none")
            {
                AiStatusText.Text = "Kies eerst een AI-provider.";
                AiStatusText.Foreground = (Brush)FindResource("Brush.Danger");
                return false;
            }
            var model = ModelBox.Text.Trim();
            if (model.Length == 0)
            {
                AiStatusText.Text = "Vul een modelnaam in.";
                AiStatusText.Foreground = (Brush)FindResource("Brush.Danger");
                return false;
            }
            AppServices.Settings.AiProvider = _provider;
            AppServices.Settings.AiModel = model;
            AppServices.Settings.AiEndpoint = EndpointBox.Text.Trim();
            if (!quiet)
            {
                AiStatusText.Text = $"Opgeslagen: {AppServices.Ai.ProviderLabel} · {model}";
                AiStatusText.Foreground = (Brush)FindResource("Brush.Success");
            }
            return true;
        }

        private void SaveAi_Click(object sender, RoutedEventArgs e) => SaveAiSettings();

        private void SaveKey_Click(object sender, RoutedEventArgs e)
        {
            if (_provider is "none" or "ollama")
            {
                AiStatusText.Text = _provider == "ollama" ? "Ollama heeft geen sleutel nodig." : "Kies eerst een provider.";
                AiStatusText.Foreground = (Brush)FindResource("Brush.Muted");
                return;
            }
            var key = KeyBox.Password.Trim();
            if (key.Length == 0)
            {
                AiStatusText.Text = "Vul je API-sleutel in.";
                AiStatusText.Foreground = (Brush)FindResource("Brush.Danger");
                return;
            }
            if (!AppServices.Ai.SaveKey(_provider, key))
            {
                AiStatusText.Text = "De sleutel kon niet veilig worden opgeslagen.";
                AiStatusText.Foreground = (Brush)FindResource("Brush.Danger");
                return;
            }
            KeyBox.Clear();
            UpdateKeyStatus();
            AiStatusText.Text = "Sleutel opgeslagen in Windows Referentiebeheer.";
            AiStatusText.Foreground = (Brush)FindResource("Brush.Success");
        }

        private void RemoveKey_Click(object sender, RoutedEventArgs e)
        {
            if (_provider is "none" or "ollama") return;
            AppServices.Ai.DeleteKey(_provider);
            UpdateKeyStatus();
            AiStatusText.Text = "Sleutel verwijderd.";
            AiStatusText.Foreground = (Brush)FindResource("Brush.Muted");
        }

        private async void FetchModels_Click(object sender, RoutedEventArgs e)
        {
            ModelSuggestions.Children.Clear();
            if (!SaveAiSettings(quiet: true)) return;
            AiStatusText.Text = "Modellen ophalen…";
            AiStatusText.Foreground = (Brush)FindResource("Brush.Muted");
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                var models = await AppServices.Ai.ListModelsAsync();
                if (models.Count == 0)
                {
                    AiStatusText.Text = "De provider gaf geen modellen terug.";
                    AiStatusText.Foreground = (Brush)FindResource("Brush.Muted");
                    return;
                }
                foreach (var model in models.Take(14))
                {
                    var chip = new Button
                    {
                        Style = (Style)FindResource("Btn.Ghost"),
                        Content = model,
                        Tag = model,
                        FontSize = 10.5,
                        Padding = new Thickness(8, 4, 8, 4),
                        Margin = new Thickness(0, 0, 5, 5)
                    };
                    chip.Click += (_, _) => ModelBox.Text = model;
                    ModelSuggestions.Children.Add(chip);
                }
                AiStatusText.Text = $"{models.Count} modellen gevonden. Klik er een aan.";
                AiStatusText.Foreground = (Brush)FindResource("Brush.Success");
            }
            catch (Exception ex)
            {
                AiStatusText.Text = ex.Message;
                AiStatusText.Foreground = (Brush)FindResource("Brush.Danger");
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private async void TestAi_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveAiSettings()) return;
            TestAiButton.IsEnabled = false;
            AiStatusText.Text = "Bezig met testen…";
            AiStatusText.Foreground = (Brush)FindResource("Brush.Muted");
            try
            {
                var reply = await AppServices.Ai.ChatAsync(
                    "Je bent een korte verbindingstest. Antwoord in één zin.",
                    "Antwoord met: de verbinding werkt.");
                var text = reply.Trim();
                if (text.Length > 220) text = text[..220] + "…";
                AiStatusText.Text = "Verbonden. " + text;
                AiStatusText.Foreground = (Brush)FindResource("Brush.Success");
            }
            catch (Exception ex)
            {
                AiStatusText.Text = "Test mislukt: " + ex.Message;
                AiStatusText.Foreground = (Brush)FindResource("Brush.Danger");
            }
            finally
            {
                TestAiButton.IsEnabled = true;
            }
        }

        // ---------------- Browser ----------------

        private void Engine_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton clicked) return;
            foreach (var chip in new[] { EngineDuck, EngineGoogle, EngineBing })
                chip.IsChecked = ReferenceEquals(chip, clicked);
        }

        private void SaveBrowser_Click(object sender, RoutedEventArgs e)
        {
            var home = BrowserHomeBox.Text.Trim();
            if (home.Length == 0) home = "https://duckduckgo.com";
            if (!home.Contains("://", StringComparison.Ordinal)) home = "https://" + home;
            if (!Uri.TryCreate(home, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                BrowserStatusText.Text = "Vul een geldig webadres in, bijvoorbeeld https://duckduckgo.com.";
                BrowserStatusText.Foreground = (Brush)FindResource("Brush.Danger");
                return;
            }
            BrowserHomeBox.Text = uri.ToString();
            AppServices.Settings.BrowserHome = uri.ToString();
            AppServices.Settings.SearchEngine = EngineGoogle.IsChecked == true ? "google"
                : EngineBing.IsChecked == true ? "bing" : "duckduckgo";
            BrowserStatusText.Text = "Browserinstellingen opgeslagen.";
            BrowserStatusText.Foreground = (Brush)FindResource("Brush.Success");
        }

        private void ClearBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmDialog.Show(Window.GetWindow(this), "Browsergegevens wissen?",
                    "De lokale WebView2-cache, cookies en tijdelijke browsegegevens worden verwijderd. Favorieten en geschiedenis die SaveSpace zelf bewaart, blijven staan.",
                    "Wissen", true, "\uE74D")) return;
            var folder = Path.Combine(AppPaths.Root, "Cache", "WebView2");
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                PrivacyStatusText.Text = "Browsergegevens gewist. Nieuwe browsesessies starten schoon.";
                PrivacyStatusText.Foreground = (Brush)FindResource("Brush.Success");
            }
            catch (Exception ex)
            {
                PrivacyStatusText.Text = "Wissen lukte niet terwijl de browser open is. Sluit de app en probeer opnieuw. " + ex.Message;
                PrivacyStatusText.Foreground = (Brush)FindResource("Brush.Danger");
            }
        }

        private void RemoveAllKeys_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmDialog.Show(Window.GetWindow(this), "Alle AI-sleutels verwijderen?",
                    "De opgeslagen sleutels voor OpenAI, Anthropic en OpenAI-compatibele providers worden uit Windows Referentiebeheer verwijderd. Je moet ze daarna opnieuw invoeren.",
                    "Alle sleutels verwijderen", true, "\uE74D")) return;
            foreach (var (key, _, _, _) in AiService.Providers)
                if (key != "ollama") AppServices.Ai.DeleteKey(key);
            UpdateKeyStatus();
            PrivacyStatusText.Text = "Alle AI-sleutels zijn verwijderd.";
            PrivacyStatusText.Foreground = (Brush)FindResource("Brush.Success");
        }

        // ---------------- Editor ----------------

        private void Spellcheck_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppServices.Settings.EditorSpellcheck = SpellcheckToggle.IsChecked == true;
        }

        private void HighContrast_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            AppServices.Settings.HighContrast = HighContrastToggle.IsChecked == true;
            _host.ApplyTheme();
        }

        // ---------------- Over ----------------

        private void OpenLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!File.Exists(AppPaths.LogFile))
                {
                    FileActions.ShowError(Window.GetWindow(this), "Er is nog geen logbestand.");
                    return;
                }
                FileActions.OpenFile(AppPaths.LogFile);
            }
            catch (Exception ex) { FileActions.ShowError(Window.GetWindow(this), ex.Message); }
        }

        private static void RestartApp()
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exe))
                    Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Start SaveSpace handmatig opnieuw om de wijziging te gebruiken.\n\n" + ex.Message,
                    "SaveSpace", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
