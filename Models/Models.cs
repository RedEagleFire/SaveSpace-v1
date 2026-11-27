using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SaveSpace.Models
{
    public enum ItemKind
    {
        Note,
        Link,
        Image,
        Video,
        Document,
        Audio
    }

    public class LibraryItem : INotifyPropertyChanged
    {
        public long Id { get; set; }
        public string Uuid { get; set; } = "";
        public ItemKind Kind { get; set; }
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
        public string? ContentRtf { get; set; }
        public string? Url { get; set; }
        public string? FilePath { get; set; }
        public string? ThumbPath { get; set; }
        public string? Mime { get; set; }
        public long SizeBytes { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double Duration { get; set; }
        public string Tags { get; set; } = "";
        public long? FolderId { get; set; }
        public string? FolderName { get; set; }
        public string? FolderColor { get; set; }

        /// <summary>Kleurstrook in de kleur van het project; zonder project de standaard notitiekleur.</summary>
        public Brush StripBrush => FolderBrush(1.0);

        /// <summary>Heel zachte tint van dezelfde kleur, als achtergrond van de rij.</summary>
        public Brush TintBrush => FolderBrush(0.10);

        private Brush FolderBrush(double alpha)
        {
            if (string.IsNullOrWhiteSpace(FolderColor))
                return Application.Current?.TryFindResource("Brush.Note") as Brush ?? Brushes.Transparent;
            var c = SaveSpace.Services.ThemeManager.Parse(FolderColor, "#C8392B");
            var brush = new SolidColorBrush(Color.FromArgb((byte)(255 * alpha), c.R, c.G, c.B));
            brush.Freeze();
            return brush;
        }
        public bool IsFavorite { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public DateTime? DeletedAt { get; set; }

        private BitmapImage? _preview;
        private bool _previewLoaded;

        public BitmapImage? Preview
        {
            get
            {
                if (_previewLoaded) return _preview;
                _previewLoaded = true;
                var path = ThumbPath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) path = FilePath;
                if (Kind is ItemKind.Image or ItemKind.Video && !string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                        bmp.DecodePixelWidth = 520;
                        bmp.UriSource = new Uri(path, UriKind.Absolute);
                        bmp.EndInit();
                        bmp.Freeze();
                        _preview = bmp;
                    }
                    catch { _preview = null; }
                }
                return _preview;
            }
        }

        public string KindLabel => Kind switch
        {
            ItemKind.Note => "Notitie",
            ItemKind.Link => "Weblink",
            ItemKind.Image => "Afbeelding",
            ItemKind.Video => "Video",
            ItemKind.Audio => "Muziek",
            _ => "Document"
        };

        public string Icon => Kind switch
        {
            ItemKind.Note => "\uE70B",
            ItemKind.Link => "\uE71B",
            ItemKind.Image => "\uEB9F",
            ItemKind.Video => "\uE714",
            ItemKind.Audio => "\uE8D6",
            _ => "\uE8A5"
        };

        public string AccentKey => Kind switch
        {
            ItemKind.Note => "Brush.Note",
            ItemKind.Link => "Brush.Link",
            ItemKind.Image => "Brush.Image",
            ItemKind.Video => "Brush.Video",
            ItemKind.Audio => "Brush.Accent",
            _ => "Brush.Document"
        };

        public Brush AccentBrush => (Brush)System.Windows.Application.Current.Resources[AccentKey];

        public string Domain
        {
            get
            {
                if (Uri.TryCreate(Url, UriKind.Absolute, out var uri))
                    return uri.Host.Replace("www.", "");
                return Url ?? "";
            }
        }

        public string MetaLine
        {
            get
            {
                return Kind switch
                {
                    ItemKind.Link => Domain,
                    ItemKind.Image => Width > 0 ? $"{Width} × {Height}" : "Afbeelding",
                    ItemKind.Video => Duration > 0 ? FormatDuration(Duration) : "Video",
                    ItemKind.Document => FormatSize(SizeBytes),
                    ItemKind.Audio => FormatSize(SizeBytes),
                    _ => string.IsNullOrWhiteSpace(Content) ? "Lege notitie" : Truncate(StripWhitespace(Content), 78)
                };
            }
        }

        public string DateLabel
        {
            get
            {
                var d = UpdatedAt;
                var now = DateTime.Now;
                if (d.Date == now.Date)
                {
                    var span = now - d;
                    if (span.TotalMinutes < 1) return "Zojuist";
                    if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min geleden";
                    return $"{d:HH:mm}";
                }
                if (d.Date == now.Date.AddDays(-1)) return $"Gisteren, {d:HH:mm}";
                if (now.Date < d.Date.AddDays(7)) return d.ToString("dddd HH:mm", new CultureInfo("nl-NL"));
                return d.ToString("d MMM yyyy", new CultureInfo("nl-NL"));
            }
        }

        public string SizeLabel => FormatSize(SizeBytes);

        public string ExtensionLabel
        {
            get
            {
                if (Kind != ItemKind.Document || string.IsNullOrWhiteSpace(FilePath)) return KindLabel;
                var ext = Path.GetExtension(FilePath).TrimStart('.').ToUpperInvariant();
                return string.IsNullOrEmpty(ext) ? "DOCUMENT" : ext;
            }
        }

        public string FavoriteGlyph => IsFavorite ? "\uE735" : "\uE734";

        public static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "—";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
            return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.#} {units[unit]}";
        }

        public static string FormatDuration(double seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
        }

        public static string Truncate(string text, int max) => text.Length <= max ? text : text[..max].TrimEnd() + "…";
        private static string StripWhitespace(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

        public event PropertyChangedEventHandler? PropertyChanged;
        public void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public void RefreshBindings()
        {
            Raise(nameof(Preview));
            Raise(nameof(IsFavorite));
            Raise(nameof(FavoriteGlyph));
            Raise(nameof(MetaLine));
            Raise(nameof(DateLabel));
        }
    }

    public class Folder
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string Color { get; set; } = "#C8392B";
        public long? ParentId { get; set; }
        public bool IsFavorite { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public int ItemCount { get; set; }
        public int SubfolderCount { get; set; }
        public string CountLabel => ItemCount == 1 ? "1 item" : $"{ItemCount} items";
        public Brush ColorBrush
        {
            get
            {
                try { return (Brush)new BrushConverter().ConvertFromString(Color)!; }
                catch { return Brushes.IndianRed; }
            }
        }
    }

    public class LinkedFolder
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public DateTime AddedAt { get; set; } = DateTime.Now;
        public bool Exists => Directory.Exists(Path);
        public string StatusLabel => Exists ? "Beschikbaar" : "Niet gevonden";
    }

    public class LibraryStats
    {
        public int Notes { get; set; }
        public int Links { get; set; }
        public int Images { get; set; }
        public int Videos { get; set; }
        public int Documents { get; set; }
        public int Audios { get; set; }
        public int Favorites { get; set; }
        public int Folders { get; set; }
        public int Trash { get; set; }
        public long MediaBytes { get; set; }
        public long DatabaseBytes { get; set; }
        public long TotalBytes => MediaBytes + DatabaseBytes;
        public int TotalItems => Notes + Links + Images + Videos + Documents + Audios;
        public int MediaItems => Images + Videos + Audios;
    }
}
