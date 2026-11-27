using System;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace SaveSpace.Services
{
    public class AppConfig
    {
        public string LibraryRoot { get; set; } = "";
    }

    public static class AppPaths
    {
        public static string ConfigDir { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoteAgent");

        public static string LegacyConfigDir { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SaveSpace");

        public static string ConfigFile { get; } = Path.Combine(ConfigDir, "config.json");
        public static string LegacyConfigFile { get; } = Path.Combine(LegacyConfigDir, "config.json");
        public static string LogFile => File.Exists(Path.Combine(ConfigDir, "noteagent.log"))
            ? Path.Combine(ConfigDir, "noteagent.log")
            : Path.Combine(AppContext.BaseDirectory, "noteagent.log");

        public static string DefaultRoot { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NoteAgent");

        public static string LegacyDefaultRoot { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SaveSpace");

        public static string Root { get; private set; } = DefaultRoot;

        public static string DatabasePath => Path.Combine(Root, "savespace.db");
        public static string MediaRoot => Path.Combine(Root, "media");
        public static string ImagesDir => Path.Combine(MediaRoot, "afbeeldingen");
        public static string VideosDir => Path.Combine(MediaRoot, "videos");
        public static string DocumentsDir => Path.Combine(MediaRoot, "documenten");
        public static string AudioDir => Path.Combine(MediaRoot, "muziek");
        public static string ThumbsDir => Path.Combine(MediaRoot, "thumbnails");
        public static string NoteFilesDir(long noteId) => Path.Combine(Root, "Notes", noteId.ToString(), "Bestanden");
        public static string BackupsDir => Path.Combine(Root, "backups");

        public static void Initialize()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                if (!File.Exists(ConfigFile) && File.Exists(LegacyConfigFile))
                {
                    File.Copy(LegacyConfigFile, ConfigFile);
                    Log("Bestaande SaveSpace-configuratie overgenomen.");
                }

                if (File.Exists(ConfigFile))
                {
                    var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigFile));
                    if (!string.IsNullOrWhiteSpace(cfg?.LibraryRoot))
                        Root = cfg.LibraryRoot;
                }
                else if (Directory.Exists(LegacyDefaultRoot) && !Directory.Exists(DefaultRoot))
                {
                    // Laat bestaande SaveSpace-bibliotheek staan waar hij is.
                    Root = LegacyDefaultRoot;
                    SaveRoot(Root);
                }
            }
            catch (Exception ex)
            {
                Log("Config laden mislukt: " + ex.Message);
                Root = DefaultRoot;
            }

            try
            {
                EnsureFolders();
                VerifyRootWritable();
                Log("Start: bibliotheekmappen zijn klaar op " + Root);
            }
            catch (Exception ex)
            {
                Root = Path.Combine(AppContext.BaseDirectory, "SaveSpaceData");
                try
                {
                    EnsureFolders();
                    VerifyRootWritable();
                    Log("De gekozen bibliotheekmap was niet beschrijfbaar; draagbare opslag gebruikt. " + ex.Message);
                }
                catch (Exception fallbackException)
                {
                    throw new IOException("SaveSpace kan geen bibliotheekmap maken. Kies een map met schrijfrechten.", fallbackException);
                }
            }
        }

        private static void VerifyRootWritable()
        {
            var probe = Path.Combine(Root, ".noteagent-write-check-" + Guid.NewGuid().ToString("N"));
            try { using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { } }
            finally { try { if (File.Exists(probe)) File.Delete(probe); } catch { } }
        }

        public static void EnsureFolders()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(MediaRoot);
            Directory.CreateDirectory(ImagesDir);
            Directory.CreateDirectory(VideosDir);
            Directory.CreateDirectory(DocumentsDir);
            Directory.CreateDirectory(AudioDir);
            Directory.CreateDirectory(ThumbsDir);
            Directory.CreateDirectory(BackupsDir);
            Directory.CreateDirectory(Path.Combine(Root, "Cache"));
            Directory.CreateDirectory(Path.Combine(Root, "Settings"));
            Directory.CreateDirectory(Path.Combine(Root, "Notes"));
            Directory.CreateDirectory(Path.Combine(Root, "Projects"));
        }

        /// <summary>
        /// Kopieert de volledige bibliotheek naar een nieuwe map en schakelt daarna over.
        /// De oude map blijft bewust staan; er wordt niets automatisch verwijderd.
        /// </summary>
        public static string MigrateStorage(string newRoot)
        {
            var source = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(newRoot).TrimEnd(Path.DirectorySeparatorChar);
            var sourcePrefix = source + Path.DirectorySeparatorChar;
            var targetPrefix = target + Path.DirectorySeparatorChar;
            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Dit is al je huidige opslagmap.");
            if (target.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase) ||
                source.StartsWith(targetPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Kies een map buiten je huidige opslagmap, zodat de bestanden niet dubbel worden gekopieerd.");

            Directory.CreateDirectory(target);
            if (File.Exists(Path.Combine(target, Path.GetFileName(DatabasePath))))
                throw new InvalidOperationException("In deze map staat al een SaveSpace-database. Kies een lege map.");

            try
            {
                SqliteConnection.ClearAllPools();
                foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(source, dir);
                    if (rel.StartsWith("Cache", StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(Path.Combine(target, rel));
                }
                foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(source, file);
                    if (rel.StartsWith("Cache", StringComparison.OrdinalIgnoreCase)) continue;
                    var destination = Path.Combine(target, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(file, destination, true);
                }

                if (!File.Exists(Path.Combine(target, Path.GetFileName(DatabasePath))))
                    throw new IOException("De database kon niet naar de nieuwe map worden gekopieerd.");

                SaveRoot(target);
                Log($"Opslag verplaatst van {source} naar {target}. Oude map blijft bewaard.");
                return target;
            }
            catch
            {
                SqliteConnection.ClearAllPools();
                throw;
            }
        }

        public static void SaveRoot(string root)
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(new AppConfig { LibraryRoot = root },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFile, json);
            Root = root;
            EnsureFolders();
        }

        public static bool IsInsideStorage(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                var full = Path.GetFullPath(path);
                var media = Path.GetFullPath(MediaRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return full.StartsWith(media, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static void Log(string message)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
            try
            {
                Directory.CreateDirectory(ConfigDir);
                File.AppendAllText(Path.Combine(ConfigDir, "noteagent.log"), line);
            }
            catch { }
            try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "noteagent.log"), line); }
            catch { }
        }
    }
}
