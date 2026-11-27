using System.IO.Compression;
using System.IO;
using System.Text.Json;
using System.Collections.Concurrent;
using SaveSpace.Models;

namespace SaveSpace.Services
{
    public sealed class NotePackageManifest
    {
        public int Version { get; set; } = 1;
        public string Uuid { get; set; } = "";
        public long NoteId { get; set; }
        public string Title { get; set; } = "Naamloos";
        public string Content { get; set; } = "";
        public string? ContentRtf { get; set; }
        public string Tags { get; set; } = "";
        public List<string> Attachments { get; set; } = new();
    }

    /// <summary>Stores a portable copy of a note and all of its attached files in one .noteagent file.</summary>
    public static class NotePackageService
    {
        private const string ManifestName = "note.json";
        private const string AttachmentPrefix = "Attachments/";
        private const long MaxPackageBytes = 500L * 1024 * 1024;
        private static readonly ConcurrentDictionary<long, object> SaveLocks = new();
        private static readonly ConcurrentDictionary<long, long> SaveVersions = new();

        public static string PackagePath(long noteId) => Path.Combine(AppPaths.Root, "Notes", $"{noteId}.noteagent");

        private static NotePackageManifest ToManifest(LibraryItem note) => new()
        {
            Uuid = note.Uuid ?? "",
            NoteId = note.Id,
            Title = string.IsNullOrWhiteSpace(note.Title) ? "Naamloos" : note.Title,
            Content = note.Content ?? "",
            ContentRtf = note.ContentRtf,
            Tags = note.Tags ?? ""
        };

        public static void Save(LibraryItem note)
        {
            var snapshot = ToManifest(note);
            SaveVersions.AddOrUpdate(snapshot.NoteId, 1, (_, current) => current + 1);
            lock (SaveLocks.GetOrAdd(snapshot.NoteId, _ => new object()))
                SaveSnapshot(snapshot);
        }

        public static void SaveAsync(LibraryItem note)
        {
            var snapshot = ToManifest(note);
            var version = SaveVersions.AddOrUpdate(snapshot.NoteId, 1, (_, current) => current + 1);
            _ = Task.Run(() =>
            {
                lock (SaveLocks.GetOrAdd(snapshot.NoteId, _ => new object()))
                {
                    // Een nieuwere versie staat al in de wachtrij; die schrijft het actuele beeld.
                    if (SaveVersions.TryGetValue(snapshot.NoteId, out var latest) && latest != version) return;
                    try { SaveSnapshot(snapshot); }
                    catch (Exception ex) { AppPaths.Log("Notitiebestand bijwerken mislukt: " + ex.Message); }
                }
            });
        }

        private static void SaveSnapshot(NotePackageManifest manifest)
        {
            var target = PackagePath(manifest.NoteId);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var filesDirectory = AppPaths.NoteFilesDir(manifest.NoteId);
            var fileNames = Directory.Exists(filesDirectory)
                ? Directory.EnumerateFiles(filesDirectory, "*", SearchOption.AllDirectories).Where(File.Exists)
                    .Select(path => Path.GetRelativePath(filesDirectory, path).Replace('\\', '/'))
                    .Where(n => !string.IsNullOrWhiteSpace(n)).ToList()
                : new List<string>();
            manifest.Attachments = fileNames;

            // Bestaand pakket: alleen het notitiebestand vervangen. Bijlagen worden niet opnieuw ingepakt.
            if (File.Exists(target))
            {
                try
                {
                    UpdateInPlace(target, manifest, filesDirectory, fileNames);
                    return;
                }
                catch (Exception ex)
                {
                    AppPaths.Log("Pakket bijwerken mislukt, opnieuw inpakken: " + ex.Message);
                }
            }
            WriteFull(target, manifest, filesDirectory, fileNames);
        }

        private static void UpdateInPlace(string target, NotePackageManifest manifest, string filesDirectory, List<string> fileNames)
        {
            using var archive = ZipFile.Open(target, ZipArchiveMode.Update);
            archive.GetEntry(ManifestName)?.Delete();
            var manifestEntry = archive.CreateEntry(ManifestName, CompressionLevel.Optimal);
            using (var stream = manifestEntry.Open())
                JsonSerializer.Serialize(stream, manifest, new JsonSerializerOptions { WriteIndented = true });

            // Verwijderde bijlagen weghalen, nieuwe toevoegen.
            foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith(AttachmentPrefix, StringComparison.Ordinal)).ToList())
            {
                if (!fileNames.Contains(entry.FullName[AttachmentPrefix.Length..], StringComparer.OrdinalIgnoreCase)) entry.Delete();
            }
            var present = archive.Entries
                .Where(e => e.FullName.StartsWith(AttachmentPrefix, StringComparison.Ordinal))
                .Select(e => e.FullName[AttachmentPrefix.Length..])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var name in fileNames)
            {
                if (present.Contains(name)) continue;
                var path = Path.Combine(filesDirectory, name.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path)) archive.CreateEntryFromFile(path, AttachmentPrefix + name, CompressionLevel.NoCompression);
            }
        }

        private static void WriteFull(string target, NotePackageManifest manifest, string filesDirectory, List<string> fileNames)
        {
            var temp = target + ".tmp";
            try
            {
                using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
                {
                    var manifestEntry = archive.CreateEntry(ManifestName, CompressionLevel.Optimal);
                    using (var stream = manifestEntry.Open())
                        JsonSerializer.Serialize(stream, manifest, new JsonSerializerOptions { WriteIndented = true });

                    foreach (var name in fileNames)
                    {
                        var path = Path.Combine(filesDirectory, name.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(path)) archive.CreateEntryFromFile(path, AttachmentPrefix + name, CompressionLevel.NoCompression);
                    }
                }
                File.Move(temp, target, true);
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                throw;
            }
        }

        public static NotePackageManifest Read(string path)
        {
            using var archive = ZipFile.OpenRead(path);
            var manifestEntry = archive.GetEntry(ManifestName) ?? throw new InvalidDataException("Dit is geen SaveSpace-notitiebestand.");
            if (manifestEntry.Length > 20 * 1024 * 1024) throw new InvalidDataException("Het notitiebestand is te groot om te openen.");
            using var stream = manifestEntry.Open();
            var manifest = JsonSerializer.Deserialize<NotePackageManifest>(stream)
                ?? throw new InvalidDataException("De notitiegegevens in dit bestand zijn ongeldig.");
            if (manifest.Version != 1) throw new InvalidDataException("Deze versie van het notitiebestand wordt niet ondersteund.");
            return manifest;
        }

        public static void ExtractAttachments(string packagePath, long noteId)
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var attachmentEntries = archive.Entries.Where(e => e.FullName.StartsWith(AttachmentPrefix, StringComparison.Ordinal)).ToList();
            var total = attachmentEntries.Sum(e => e.Length);
            if (total > MaxPackageBytes) throw new InvalidDataException("De bijlagen in dit notitiebestand zijn te groot.");

            var targetDirectory = AppPaths.NoteFilesDir(noteId);
            Directory.CreateDirectory(targetDirectory);
            foreach (var entry in attachmentEntries)
            {
                // Alleen de bestandsnaam gebruiken: voorkomt dat een pakket buiten de map schrijft.
                var relative = entry.FullName[AttachmentPrefix.Length..].Replace('/', Path.DirectorySeparatorChar);
                if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) ||
                    relative.Split(Path.DirectorySeparatorChar).Any(part => part is ".." or ".")) continue;
                var destination = Path.GetFullPath(Path.Combine(targetDirectory, relative));
                var root = Path.GetFullPath(targetDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var source = entry.Open();
                using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
                source.CopyTo(target);
            }
        }
    }
}
