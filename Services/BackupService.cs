using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace SaveSpace.Services
{
    public class BackupInfo
    {
        public string Path { get; set; } = "";
        public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
        public DateTime Created { get; set; }
        public long Size { get; set; }
        public string SizeLabel => Models.LibraryItem.FormatSize(Size);
        public string DateLabel => Created.ToString("d MMMM yyyy, HH:mm");
        public bool IsAutomatic => System.IO.Path.GetFileName(Path).StartsWith("auto-", StringComparison.OrdinalIgnoreCase);
    }

    public class BackupService
    {
        private const string AppVersion = "1.0.0";
        private readonly LibraryService _library;
        private readonly SettingsService _settings;

        public BackupService(LibraryService library, SettingsService settings)
        {
            _library = library;
            _settings = settings;
        }

        public string CreateBackup(bool includeMedia, bool automatic = false)
        {
            Directory.CreateDirectory(AppPaths.BackupsDir);
            _library.Db.Checkpoint();
            SqliteConnection.ClearAllPools();

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var name = automatic ? $"auto-{stamp}.zip" : $"savespace-{stamp}.zip";
            var target = Path.Combine(AppPaths.BackupsDir, name);
            var temp = target + ".tmp";
            if (File.Exists(temp)) File.Delete(temp);

            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                if (File.Exists(AppPaths.DatabasePath))
                    zip.CreateEntryFromFile(AppPaths.DatabasePath, "savespace.db", CompressionLevel.Optimal);

                if (includeMedia)
                {
                    AddFolder(zip, AppPaths.MediaRoot, "media/", CompressionLevel.Optimal);
                    // Notitiebijlagen en .noteagent-pakketten horen bij de bibliotheek en moeten mee.
                    AddFolder(zip, Path.Combine(AppPaths.Root, "Notes"), "notes/", CompressionLevel.NoCompression);
                }

                var manifest = zip.CreateEntry("manifest.json", CompressionLevel.NoCompression);
                using var writer = new StreamWriter(manifest.Open());
                writer.Write(JsonSerializer.Serialize(new
                {
                    app = "SaveSpace",
                    version = AppVersion,
                    created = DateTime.Now.ToString("O"),
                    includesMedia = includeMedia,
                    includesNotes = includeMedia,
                    libraryRoot = AppPaths.Root
                }, new JsonSerializerOptions { WriteIndented = true }));
            }

            if (File.Exists(target)) File.Delete(target);
            File.Move(temp, target);
            MirrorBackup(target);
            _settings.LastBackup = DateTime.Now;
            Prune();
            AppServices.Notifications.Add("Back-up gemaakt",
                $"{(includeMedia ? "Volledige" : "Compacte")} back-up opgeslagen in de back-upmap.",
                "\uE74E", "Brush.Teal");
            return target;
        }

        private static void AddFolder(ZipArchive zip, string folder, string prefix, CompressionLevel level)
        {
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                var rel = prefix + Path.GetRelativePath(folder, file).Replace('\\', '/');
                var fileLevel = IsLargeMedia(file) ? CompressionLevel.NoCompression : level;
                zip.CreateEntryFromFile(file, rel, fileLevel);
            }
        }

        private static bool IsLargeMedia(string path) =>
            new[] { ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm", ".mpg", ".mpeg", ".mp3", ".wav", ".m4a", ".ogg", ".flac", ".aac", ".wma", ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp" }
                .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

        private void MirrorBackup(string backupPath)
        {
            var folder = _settings.BackupMirrorFolder;
            if (string.IsNullOrWhiteSpace(folder)) return;
            try
            {
                Directory.CreateDirectory(folder);
                var destination = Path.GetFullPath(Path.Combine(folder, Path.GetFileName(backupPath)));
                if (string.Equals(Path.GetFullPath(backupPath), destination, StringComparison.OrdinalIgnoreCase)) return;
                File.Copy(backupPath, destination, true);
                AppPaths.Log("Tweede back-upkopie opgeslagen: " + destination);
            }
            catch (Exception ex)
            {
                AppPaths.Log("Tweede back-upkopie mislukt: " + ex.Message);
                AppServices.Notifications.Add("Extra back-up niet gelukt", ex.Message, "\uE7BA", "Brush.Danger");
            }
        }

        public List<BackupInfo> List()
        {
            try
            {
                if (!Directory.Exists(AppPaths.BackupsDir)) return new List<BackupInfo>();
                return Directory.GetFiles(AppPaths.BackupsDir, "*.zip")
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(f => f.CreationTime)
                    .Select(f => new BackupInfo { Path = f.FullName, Created = f.CreationTime, Size = f.Length })
                    .ToList();
            }
            catch { return new List<BackupInfo>(); }
        }

        public void Restore(string zipPath)
        {
            if (!File.Exists(zipPath)) throw new FileNotFoundException("Back-upbestand niet gevonden.", zipPath);
            _library.Db.Checkpoint();
            SqliteConnection.ClearAllPools();

            // Eerst een volledige noodkopie van de huidige bibliotheek, inclusief bijlagen.
            CreateBackup(true, automatic: true);

            using (var zip = ZipFile.OpenRead(zipPath))
            {
                var dbEntry = zip.GetEntry("savespace.db")
                    ?? throw new InvalidDataException("Deze back-up bevat geen SaveSpace-database.");
                var tempDb = AppPaths.DatabasePath + ".restore";
                dbEntry.ExtractToFile(tempDb, true);

                // Oude WAL-bestanden mogen niet over de teruggezette database heen worden toegepast.
                foreach (var suffix in new[] { "-wal", "-shm" })
                {
                    var leftover = AppPaths.DatabasePath + suffix;
                    if (File.Exists(leftover)) File.Delete(leftover);
                }
                File.Copy(tempDb, AppPaths.DatabasePath, true);
                File.Delete(tempDb);

                ExtractFolder(zip, "media/", AppPaths.MediaRoot);
                ExtractFolder(zip, "notes/", Path.Combine(AppPaths.Root, "Notes"));
            }

            AppServices.Notifications.Add("Back-up hersteld",
                "Je bibliotheek is teruggezet. SaveSpace start opnieuw.", "\uE777", "Brush.Amber");
        }

        private static void ExtractFolder(ZipArchive zip, string prefix, string destinationRoot)
        {
            var root = Path.GetFullPath(destinationRoot);
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                var rel = entry.FullName[prefix.Length..];
                if (string.IsNullOrWhiteSpace(rel) || entry.FullName.EndsWith("/")) continue;
                var destination = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
                // Nooit buiten de eigen map schrijven.
                if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, true);
            }
        }

        public void Delete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { AppPaths.Log("Back-up verwijderen mislukt: " + ex.Message); }
        }

        /// <summary>Houdt alleen het gekozen aantal automatische back-ups over. Handmatige back-ups blijven altijd staan.</summary>
        public void Prune()
        {
            try
            {
                var keep = _settings.BackupKeep;
                var automatic = Directory.GetFiles(AppPaths.BackupsDir, "auto-*.zip")
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(f => f.CreationTime)
                    .Skip(keep);
                foreach (var file in automatic) file.Delete();
            }
            catch (Exception ex) { AppPaths.Log("Back-ups opruimen mislukt: " + ex.Message); }
        }

        public void RunAutomaticIfDue()
        {
            if (!_settings.AutoBackup) return;
            if (_settings.LastBackup.HasValue && DateTime.Now - _settings.LastBackup.Value < TimeSpan.FromHours(24)) return;
            try { CreateBackup(true, true); }
            catch (Exception ex)
            {
                AppPaths.Log("Automatische back-up mislukt: " + ex.Message);
                AppServices.Notifications.Add("Back-up mislukt", ex.Message, "\uE7BA", "Brush.Danger");
            }
        }

        public void OpenBackupFolder()
        {
            Directory.CreateDirectory(AppPaths.BackupsDir);
            Process.Start(new ProcessStartInfo { FileName = AppPaths.BackupsDir, UseShellExecute = true });
        }
    }
}
