using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SaveSpace.Services
{
    public static class AppServices
    {
        public static Database Database { get; private set; } = null!;
        public static LibraryService Library { get; private set; } = null!;
        public static SettingsService Settings { get; private set; } = null!;
        public static NotificationService Notifications { get; } = new();
        public static BackupService Backups { get; private set; } = null!;
        public static AiService Ai { get; private set; } = null!;

        public static void Initialize()
        {
            AppPaths.Initialize();
            Database = new Database(AppPaths.DatabasePath);
            Database.Initialize();
            Library = new LibraryService(Database);
            Settings = new SettingsService(Library);
            Backups = new BackupService(Library, Settings);
            Ai = new AiService(Settings);
            Library.SeedIfEmpty();
            ThemeManager.Apply(Settings);
        }
    }

    public class NotificationItem : INotifyPropertyChanged
    {
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public string Glyph { get; set; } = "\uE946";
        public string AccentKey { get; set; } = "Brush.Accent";
        public DateTime Time { get; set; } = DateTime.Now;
        public bool IsRead { get; set; }

        public string TimeLabel
        {
            get
            {
                var span = DateTime.Now - Time;
                if (span.TotalMinutes < 1) return "Nu";
                if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min";
                if (span.TotalHours < 24) return $"{(int)span.TotalHours} u";
                return Time.ToString("d MMM");
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public void Raise([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class NotificationService : INotifyPropertyChanged
    {
        public ObservableCollection<NotificationItem> Items { get; } = new();

        public int UnreadCount => Items.Count(i => !i.IsRead);

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Add(string title, string message, string glyph = "\uE946", string accentKey = "Brush.Accent")
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(() => Add(title, message, glyph, accentKey));
                return;
            }
            Items.Insert(0, new NotificationItem { Title = title, Message = message, Glyph = glyph, AccentKey = accentKey });
            while (Items.Count > 30) Items.RemoveAt(Items.Count - 1);
            Raise(nameof(UnreadCount));
        }

        public void MarkAllRead()
        {
            foreach (var item in Items) item.IsRead = true;
            Raise(nameof(UnreadCount));
        }

        public void Clear()
        {
            Items.Clear();
            Raise(nameof(UnreadCount));
        }

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
