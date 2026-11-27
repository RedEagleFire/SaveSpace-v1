using System;
using System.Globalization;
using Microsoft.Win32;

namespace SaveSpace.Services
{
    public class SettingsService
    {
        private readonly LibraryService _library;

        public event Action? Changed;

        public SettingsService(LibraryService library)
        {
            _library = library;
        }

        private string Get(string key, string fallback) => _library.GetSetting(key) ?? fallback;

        public string Theme
        {
            get => Get("theme", "dark");
            set => Save("theme", value);
        }

        public bool IsDark => Theme switch
        {
            "light" => false,
            "system" => ReadSystemDarkMode(),
            _ => true
        };

        private static bool ReadSystemDarkMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
                return Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 1), CultureInfo.InvariantCulture) == 0;
            }
            catch { return true; }
        }

        public string Accent
        {
            get => Get("accent", IsDark ? "#F21D2F" : "#C8392B");
            set => Save("accent", value);
        }

        public string ColorNote
        {
            get => Get("color.note", IsDark ? "#F21D2F" : "#C8392B");
            set => Save("color.note", value);
        }

        public string ColorLink
        {
            get => Get("color.link", IsDark ? "#3FB5A6" : "#147D74");
            set => Save("color.link", value);
        }

        public string ColorImage
        {
            get => Get("color.image", IsDark ? "#E8A33D" : "#D98A2B");
            set => Save("color.image", value);
        }

        public string ColorVideo
        {
            get => Get("color.video", IsDark ? "#C9A227" : "#A8730F");
            set => Save("color.video", value);
        }

        public string ColorDocument
        {
            get => Get("color.document", IsDark ? "#A8A9B0" : "#6B6259");
            set => Save("color.document", value);
        }

        public string ColorFolder
        {
            get => Get("color.folder", IsDark ? "#7FA268" : "#557A46");
            set => Save("color.folder", value);
        }

        public double Radius
        {
            get => double.TryParse(Get("ui.radius", "10"), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 0, 24) : 10;
            set => Save("ui.radius", value.ToString("0.#", CultureInfo.InvariantCulture));
        }

        public double FontScale
        {
            get => double.TryParse(Get("ui.fontscale", "1"), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 0.85, 1.35) : 1;
            set => Save("ui.fontscale", value.ToString("0.00", CultureInfo.InvariantCulture));
        }

        public bool Animations
        {
            get => Get("ui.animations", "true") == "true";
            set => Save("ui.animations", value ? "true" : "false");
        }

        public bool Compact
        {
            get => Get("ui.compact", "false") == "true";
            set => Save("ui.compact", value ? "true" : "false");
        }

        public bool HighContrast
        {
            get => Get("accessibility.highcontrast", "false") == "true";
            set => Save("accessibility.highcontrast", value ? "true" : "false");
        }

        public string LayoutPreset
        {
            get => Get("ui.layoutpreset", "claude");
            set => Save("ui.layoutpreset", value);
        }

        public string HomeBackgroundColor
        {
            get => Get("ui.homebackground", "");
            set => Save("ui.homebackground", value);
        }

        public bool HomeWidgetFavorites { get => Get("home.widget.favorites", "true") == "true"; set => Save("home.widget.favorites", value ? "true" : "false"); }
        public bool HomeWidgetProjects { get => Get("home.widget.projects", "true") == "true"; set => Save("home.widget.projects", value ? "true" : "false"); }
        public bool HomeWidgetNotes { get => Get("home.widget.notes", "true") == "true"; set => Save("home.widget.notes", value ? "true" : "false"); }
        public bool HomeWidgetDocuments { get => Get("home.widget.documents", "true") == "true"; set => Save("home.widget.documents", value ? "true" : "false"); }
        public bool HomeWidgetImages { get => Get("home.widget.images", "true") == "true"; set => Save("home.widget.images", value ? "true" : "false"); }
        public bool HomeWidgetVideos { get => Get("home.widget.videos", "true") == "true"; set => Save("home.widget.videos", value ? "true" : "false"); }
        public bool HomeWidgetAudio { get => Get("home.widget.audio", "false") == "true"; set => Save("home.widget.audio", value ? "true" : "false"); }
        public bool HomeWidgetLinks { get => Get("home.widget.links", "false") == "true"; set => Save("home.widget.links", value ? "true" : "false"); }
        public bool HomeWidgetTrash { get => Get("home.widget.trash", "false") == "true"; set => Save("home.widget.trash", value ? "true" : "false"); }
        public bool HomeWidgetBackup { get => Get("home.widget.backup", "false") == "true"; set => Save("home.widget.backup", value ? "true" : "false"); }

        public string AttachmentFolder
        {
            get => Get("files.initialfolder", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            set => Save("files.initialfolder", value);
        }

        public string UserName
        {
            get => Get("user.name", Environment.UserName);
            set => Save("user.name", value);
        }

        public string LibraryName
        {
            get => Get("library.name", "Mijn bibliotheek");
            set => Save("library.name", value);
        }

        public string AiProvider
        {
            get => Get("ai.provider", "none");
            set => Save("ai.provider", value);
        }

        public string AiModel
        {
            get => Get("ai.model", "llama3.2");
            set => Save("ai.model", value);
        }

        public string AiEndpoint
        {
            get => Get("ai.endpoint", "");
            set => Save("ai.endpoint", value);
        }

        public string BrowserHome
        {
            get => Get("browser.home", "https://duckduckgo.com");
            set => Save("browser.home", value);
        }

        public string SearchEngine
        {
            get => Get("browser.searchengine", "duckduckgo");
            set => Save("browser.searchengine", value);
        }

        public bool EditorSpellcheck
        {
            get => Get("editor.spellcheck", "true") == "true";
            set => Save("editor.spellcheck", value ? "true" : "false");
        }

        public bool AutoBackup
        {
            get => Get("backup.auto", "false") == "true";
            set => Save("backup.auto", value ? "true" : "false");
        }

        public string BackupMirrorFolder
        {
            get => Get("backup.mirrorfolder", "");
            set => Save("backup.mirrorfolder", value);
        }

        public int BackupKeep
        {
            get => int.TryParse(Get("backup.keep", "10"), out var v) ? Math.Clamp(v, 1, 50) : 10;
            set => Save("backup.keep", value.ToString());
        }

        public int TrashRetentionDays
        {
            get => int.TryParse(Get("trash.retentiondays", "0"), out var days) && days is 30 or 60 or 90 ? days : 0;
            set => Save("trash.retentiondays", value is 30 or 60 or 90 ? value.ToString() : "0");
        }

        public DateTime? LastBackup
        {
            get
            {
                var raw = _library.GetSetting("backup.last");
                return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt.ToLocalTime() : null;
            }
            set => Save("backup.last", value?.ToString("O", CultureInfo.InvariantCulture));
        }

        private void Save(string key, string? value)
        {
            _library.SetSetting(key, value);
            Changed?.Invoke();
        }

        public void ApplyDefaultsAfterReset()
        {
            foreach (var key in new[] { "theme", "accent", "color.note", "color.link", "color.image", "color.video", "color.document", "color.folder", "ui.radius", "ui.fontscale", "ui.animations", "ui.compact", "ui.layoutpreset", "ui.homebackground", "accessibility.highcontrast" })
                _library.SetSetting(key, null);
            Changed?.Invoke();
        }
    }
}
