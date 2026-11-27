using System.IO;
using System.Text.Json;
using SaveSpace.Models;

namespace SaveSpace.Services
{
    public sealed class NoteVersion
    {
        public DateTime SavedAt { get; set; }
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
        public string? ContentRtf { get; set; }
        public string Tags { get; set; } = "";
    }

    /// <summary>Bewart begrensde, lokale herstelpunten voordat een notitie wordt overschreven.</summary>
    public static class NoteHistoryService
    {
        private const int MaximumVersions = 40;
        private static string DirectoryFor(long id) => Path.Combine(AppPaths.Root, "Notes", id.ToString(), "History");

        public static List<NoteVersion> List(long noteId)
        {
            try
            {
                var directory = DirectoryFor(noteId);
                if (!Directory.Exists(directory)) return new();
                return Directory.GetFiles(directory, "*.json").Select(Read)
                    .Where(v => v != null).Cast<NoteVersion>().OrderByDescending(v => v.SavedAt).ToList();
            }
            catch (Exception ex) { AppPaths.Log("Notitiegeschiedenis lezen mislukt: " + ex.Message); return new(); }
        }

        public static void CapturePrevious(LibraryItem edited)
        {
            try
            {
                var previous = AppServices.Library.GetItem(edited.Id);
                if (previous == null || previous.Kind != ItemKind.Note) return;
                if (previous.Title == edited.Title && previous.Content == edited.Content && previous.ContentRtf == edited.ContentRtf && previous.Tags == edited.Tags) return;
                var directory = DirectoryFor(edited.Id);
                Directory.CreateDirectory(directory);
                var latest = List(edited.Id).FirstOrDefault();
                if (latest != null && DateTime.Now - latest.SavedAt < TimeSpan.FromMinutes(5)) return;
                var version = new NoteVersion
                {
                    SavedAt = DateTime.Now,
                    Title = previous.Title,
                    Content = previous.Content,
                    ContentRtf = previous.ContentRtf,
                    Tags = previous.Tags
                };
                var path = Path.Combine(directory, version.SavedAt.ToString("yyyyMMdd-HHmmss-fff") + ".json");
                File.WriteAllText(path, JsonSerializer.Serialize(version, new JsonSerializerOptions { WriteIndented = true }));
                foreach (var old in Directory.GetFiles(directory, "*.json").Select(p => new FileInfo(p)).OrderByDescending(f => f.CreationTime).Skip(MaximumVersions))
                    old.Delete();
            }
            catch (Exception ex) { AppPaths.Log("Notitieversie opslaan mislukt: " + ex.Message); }
        }

        public static void Restore(LibraryItem note, NoteVersion version)
        {
            note.Title = version.Title;
            note.Content = version.Content;
            note.ContentRtf = version.ContentRtf;
            note.Tags = version.Tags;
            AppServices.Library.UpdateItem(note);
            NotePackageService.SaveAsync(note);
        }

        private static NoteVersion? Read(string path)
        {
            try { return JsonSerializer.Deserialize<NoteVersion>(File.ReadAllText(path)); }
            catch (Exception ex) { AppPaths.Log("Ongeldige notitieversie overgeslagen: " + ex.Message); return null; }
        }
    }
}
