using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using SaveSpace.Models;

namespace SaveSpace.Services
{
    public class LibraryService
    {
        private readonly Database _db;
        public Database Db => _db;

        /// <summary>Gebruik dit voor lijsten die alles moeten tonen; er wordt niets stil afgekapt.</summary>
        public const int AllItems = 1_000_000;

        private const string SelectItems = @"
            SELECT i.*, f.name AS folder_name, f.color AS folder_color
            FROM items i LEFT JOIN folders f ON f.id = i.folder_id ";

        public LibraryService(Database db)
        {
            _db = db;
        }

        private static string Iso(DateTime dt) => dt.ToString("O", CultureInfo.InvariantCulture);

        private static DateTime ParseDate(SqliteDataReader r, string name)
        {
            var idx = r.GetOrdinal(name);
            if (r.IsDBNull(idx)) return DateTime.Now;
            var raw = r.GetString(idx);
            return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
                ? dt.ToLocalTime()
                : DateTime.Now;
        }

        private static DateTime? ParseNullableDate(SqliteDataReader r, string name)
        {
            var idx = r.GetOrdinal(name);
            if (r.IsDBNull(idx)) return null;
            var raw = r.GetString(idx);
            return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
                ? dt.ToLocalTime()
                : null;
        }

        private static string? ReadColumn(SqliteDataReader r, string name)
        {
            for (var i = 0; i < r.FieldCount; i++)
            {
                if (string.Equals(r.GetName(i), name, StringComparison.OrdinalIgnoreCase))
                    return r.IsDBNull(i) ? null : r.GetString(i);
            }
            return null;
        }

        private static string ReadUuid(SqliteDataReader r)
        {
            for (var i = 0; i < r.FieldCount; i++)
            {
                if (string.Equals(r.GetName(i), "uuid", StringComparison.OrdinalIgnoreCase))
                    return r.IsDBNull(i) ? "" : r.GetString(i);
            }
            return "";
        }

        private static LibraryItem Map(SqliteDataReader r)
        {
            var item = new LibraryItem
            {
                Id = r.GetInt64(r.GetOrdinal("id")),
                Uuid = ReadUuid(r),
                Kind = Enum.TryParse<ItemKind>(r.GetString(r.GetOrdinal("kind")), true, out var kind) ? kind : ItemKind.Note,
                Title = r.GetString(r.GetOrdinal("title")),
                Content = r.IsDBNull(r.GetOrdinal("content")) ? "" : r.GetString(r.GetOrdinal("content")),
                ContentRtf = r.IsDBNull(r.GetOrdinal("content_rtf")) ? null : r.GetString(r.GetOrdinal("content_rtf")),
                Url = r.IsDBNull(r.GetOrdinal("url")) ? null : r.GetString(r.GetOrdinal("url")),
                FilePath = r.IsDBNull(r.GetOrdinal("file_path")) ? null : r.GetString(r.GetOrdinal("file_path")),
                ThumbPath = r.IsDBNull(r.GetOrdinal("thumb_path")) ? null : r.GetString(r.GetOrdinal("thumb_path")),
                Mime = r.IsDBNull(r.GetOrdinal("mime")) ? null : r.GetString(r.GetOrdinal("mime")),
                SizeBytes = r.IsDBNull(r.GetOrdinal("size_bytes")) ? 0 : r.GetInt64(r.GetOrdinal("size_bytes")),
                Width = r.IsDBNull(r.GetOrdinal("width")) ? 0 : r.GetInt32(r.GetOrdinal("width")),
                Height = r.IsDBNull(r.GetOrdinal("height")) ? 0 : r.GetInt32(r.GetOrdinal("height")),
                Duration = r.IsDBNull(r.GetOrdinal("duration")) ? 0 : r.GetDouble(r.GetOrdinal("duration")),
                Tags = r.IsDBNull(r.GetOrdinal("tags")) ? "" : r.GetString(r.GetOrdinal("tags")),
                FolderId = r.IsDBNull(r.GetOrdinal("folder_id")) ? null : r.GetInt64(r.GetOrdinal("folder_id")),
                IsFavorite = !r.IsDBNull(r.GetOrdinal("is_favorite")) && r.GetInt64(r.GetOrdinal("is_favorite")) == 1,
                CreatedAt = ParseDate(r, "created_at"),
                UpdatedAt = ParseDate(r, "updated_at"),
                DeletedAt = ParseNullableDate(r, "deleted_at")
            };
            var folderIdx = r.GetOrdinal("folder_name");
            if (!r.IsDBNull(folderIdx)) item.FolderName = r.GetString(folderIdx);
            item.FolderColor = ReadColumn(r, "folder_color");
            return item;
        }

        public List<LibraryItem> GetItems(ItemKind? kind = null, bool? favorite = null, long? folderId = null,
            bool includeDeleted = false, int limit = 500)
        {
            var sql = SelectItems;
            var where = new List<string>();
            var args = new List<(string, object?)>();
            if (!includeDeleted) where.Add("i.deleted_at IS NULL");
            else where.Add("i.deleted_at IS NOT NULL");
            if (kind.HasValue) { where.Add("i.kind = @kind"); args.Add(("@kind", kind.Value.ToString().ToLowerInvariant())); }
            if (favorite == true) where.Add("i.is_favorite = 1");
            if (folderId.HasValue) { where.Add("i.folder_id = @folder"); args.Add(("@folder", folderId.Value)); }
            sql += " WHERE " + string.Join(" AND ", where);
            sql += includeDeleted ? " ORDER BY i.deleted_at DESC" : " ORDER BY i.updated_at DESC";
            sql += $" LIMIT {Math.Clamp(limit, 1, AllItems)}";
            return _db.Query(sql, Map, args.ToArray());
        }

        public List<LibraryItem> GetRecent(int limit = 8) =>
            GetItems(limit: limit);

        public List<LibraryItem> GetFavorites() =>
            GetItems(favorite: true, limit: AllItems);

        public List<LibraryItem> GetTrash() =>
            GetItems(includeDeleted: true, limit: AllItems);

        public LibraryItem? GetItemByUuid(string uuid) =>
            string.IsNullOrEmpty(uuid) ? null
                : _db.QueryOne(SelectItems + " WHERE i.uuid = @uuid AND i.deleted_at IS NULL", Map, ("@uuid", uuid));

        public LibraryItem? GetItem(long id) =>
            _db.QueryOne(SelectItems + " WHERE i.id = @id", Map, ("@id", id));

        public long InsertItem(LibraryItem item)
        {
            var now = DateTime.Now;
            if (item.CreatedAt == default) item.CreatedAt = now;
            item.UpdatedAt = now;
            if (string.IsNullOrEmpty(item.Uuid)) item.Uuid = Guid.NewGuid().ToString("N");
            var id = _db.Scalar(@"
                INSERT INTO items (uuid, kind, title, content, content_rtf, url, file_path, thumb_path, mime,
                    size_bytes, width, height, duration, tags, folder_id, is_favorite, created_at, updated_at, deleted_at)
                VALUES (@uuid, @kind, @title, @content, @rtf, @url, @file, @thumb, @mime,
                    @size, @width, @height, @duration, @tags, @folder, @fav, @created, @updated, @deleted);
                SELECT last_insert_rowid();",
                ("@uuid", item.Uuid),
                ("@kind", item.Kind.ToString().ToLowerInvariant()),
                ("@title", string.IsNullOrWhiteSpace(item.Title) ? "Naamloos" : item.Title.Trim()),
                ("@content", item.Content ?? ""),
                ("@rtf", item.ContentRtf),
                ("@url", item.Url),
                ("@file", item.FilePath),
                ("@thumb", item.ThumbPath),
                ("@mime", item.Mime),
                ("@size", item.SizeBytes),
                ("@width", item.Width),
                ("@height", item.Height),
                ("@duration", item.Duration),
                ("@tags", item.Tags ?? ""),
                ("@folder", item.FolderId),
                ("@fav", item.IsFavorite ? 1 : 0),
                ("@created", Iso(item.CreatedAt)),
                ("@updated", Iso(item.UpdatedAt)),
                ("@deleted", item.DeletedAt.HasValue ? Iso(item.DeletedAt.Value) : null));
            item.Id = Convert.ToInt64(id);
            return item.Id;
        }

        public void UpdateItem(LibraryItem item)
        {
            item.UpdatedAt = DateTime.Now;
            _db.Execute(@"
                UPDATE items SET title=@title, content=@content, content_rtf=@rtf, url=@url,
                    file_path=@file, thumb_path=@thumb, mime=@mime, size_bytes=@size, width=@width,
                    height=@height, duration=@duration, tags=@tags, folder_id=@folder,
                    is_favorite=@fav, updated_at=@updated
                WHERE id=@id",
                ("@title", string.IsNullOrWhiteSpace(item.Title) ? "Naamloos" : item.Title.Trim()),
                ("@content", item.Content ?? ""),
                ("@rtf", item.ContentRtf),
                ("@url", item.Url),
                ("@file", item.FilePath),
                ("@thumb", item.ThumbPath),
                ("@mime", item.Mime),
                ("@size", item.SizeBytes),
                ("@width", item.Width),
                ("@height", item.Height),
                ("@duration", item.Duration),
                ("@tags", item.Tags ?? ""),
                ("@folder", item.FolderId),
                ("@fav", item.IsFavorite ? 1 : 0),
                ("@updated", Iso(item.UpdatedAt)),
                ("@id", item.Id));
        }

        public void UpdateNote(long id, string title, string content, string? rtf)
        {
            _db.Execute("UPDATE items SET title=@title, content=@content, content_rtf=@rtf, updated_at=@updated WHERE id=@id",
                ("@title", string.IsNullOrWhiteSpace(title) ? "Naamloos" : title.Trim()),
                ("@content", content ?? ""),
                ("@rtf", rtf),
                ("@updated", Iso(DateTime.Now)),
                ("@id", id));
        }

        public void SetFavorite(long id, bool favorite)
        {
            _db.Execute("UPDATE items SET is_favorite=@fav, updated_at=@updated WHERE id=@id",
                ("@fav", favorite ? 1 : 0), ("@updated", Iso(DateTime.Now)), ("@id", id));
        }

        public void SetFolder(long id, long? folderId)
        {
            _db.Execute("UPDATE items SET folder_id=@folder, updated_at=@updated WHERE id=@id",
                ("@folder", folderId), ("@updated", Iso(DateTime.Now)), ("@id", id));
        }

        public void SoftDelete(long id)
        {
            _db.Execute("UPDATE items SET deleted_at=@deleted, updated_at=@updated WHERE id=@id",
                ("@deleted", Iso(DateTime.Now)), ("@updated", Iso(DateTime.Now)), ("@id", id));
        }

        public void Restore(long id)
        {
            _db.Execute("UPDATE items SET deleted_at=NULL, updated_at=@updated WHERE id=@id",
                ("@updated", Iso(DateTime.Now)), ("@id", id));
        }

        public void DeletePermanently(long id)
        {
            var item = GetItem(id);
            _db.Execute("DELETE FROM items WHERE id=@id", ("@id", id));
            if (item == null) return;
            if (AppPaths.IsInsideStorage(item.FilePath)) TryDelete(item.FilePath);
            if (AppPaths.IsInsideStorage(item.ThumbPath)) TryDelete(item.ThumbPath);
            if (item.Kind == ItemKind.Note) DeleteNoteFiles(id);
        }

        /// <summary>Verwijdert de bijlagenmap en het .noteagent-pakket van een notitie.</summary>
        private static void DeleteNoteFiles(long noteId)
        {
            try
            {
                var noteDir = System.IO.Path.Combine(AppPaths.Root, "Notes", noteId.ToString());
                if (Directory.Exists(noteDir)) Directory.Delete(noteDir, true);
                var package = NotePackageService.PackagePath(noteId);
                if (File.Exists(package)) File.Delete(package);
            }
            catch (Exception ex) { AppPaths.Log("Notitiebestanden verwijderen mislukt: " + ex.Message); }
        }

        public void EmptyTrash()
        {
            var trash = GetTrash();
            foreach (var item in trash) DeletePermanently(item.Id);
        }

        public int PurgeExpiredTrash(int retentionDays)
        {
            if (retentionDays <= 0) return 0;
            var cutoff = DateTime.Now.AddDays(-Math.Clamp(retentionDays, 1, 3650));
            var expired = GetTrash().Where(item => item.DeletedAt.HasValue && item.DeletedAt.Value <= cutoff).ToList();
            foreach (var item in expired) DeletePermanently(item.Id);
            return expired.Count;
        }

        private static void TryDelete(string? path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { AppPaths.Log("Verwijderen mislukt: " + ex.Message); }
        }

        public List<LibraryItem> Search(string query, int limit = AllItems)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<LibraryItem>();
            var tokens = System.Text.RegularExpressions.Regex.Matches(query, @"[\p{L}\p{N}]+")
                .Select(m => m.Value).Where(t => t.Length > 0).Take(8).ToList();
            if (tokens.Count == 0) return new List<LibraryItem>();

            if (_db.FullTextAvailable)
            {
                try
                {
                    var fts = string.Join(" AND ", tokens.Select(t => "\"" + t.Replace("\"", "") + "\"*"));
                    var sql = @"SELECT i.*, f.name AS folder_name, f.color AS folder_color
                        FROM items_fts JOIN items i ON i.id = items_fts.rowid
                        LEFT JOIN folders f ON f.id = i.folder_id
                        WHERE items_fts MATCH @q AND i.deleted_at IS NULL
                        ORDER BY bm25(items_fts) LIMIT " + Math.Clamp(limit, 1, AllItems);
                    var result = _db.Query(sql, Map, ("@q", fts));
                    if (result.Count > 0) return result;
                }
                catch (Exception ex) { AppPaths.Log("FTS zoekfout: " + ex.Message); }
            }

            var like = "%" + query.Trim() + "%";
            return _db.Query(SelectItems + @" WHERE i.deleted_at IS NULL
                AND (i.title LIKE @like OR i.content LIKE @like OR i.tags LIKE @like OR i.url LIKE @like)
                ORDER BY i.updated_at DESC LIMIT " + Math.Clamp(limit, 1, AllItems), Map, ("@like", like));
        }

        public LibraryStats GetStats()
        {
            var stats = new LibraryStats();
            var rows = _db.Query("SELECT kind, COUNT(*) AS c FROM items WHERE deleted_at IS NULL GROUP BY kind",
                r => (Kind: r.GetString(0), Count: r.GetInt32(1)));
            foreach (var row in rows)
            {
                switch (row.Kind)
                {
                    case "note": stats.Notes = row.Count; break;
                    case "link": stats.Links = row.Count; break;
                    case "image": stats.Images = row.Count; break;
                    case "video": stats.Videos = row.Count; break;
                    case "document": stats.Documents = row.Count; break;
                    case "audio": stats.Audios = row.Count; break;
                }
            }
            stats.Favorites = Convert.ToInt32(_db.Scalar("SELECT COUNT(*) FROM items WHERE is_favorite=1 AND deleted_at IS NULL") ?? 0);
            stats.Folders = Convert.ToInt32(_db.Scalar("SELECT COUNT(*) FROM folders") ?? 0);
            stats.Trash = Convert.ToInt32(_db.Scalar("SELECT COUNT(*) FROM items WHERE deleted_at IS NOT NULL") ?? 0);
            stats.MediaBytes = Convert.ToInt64(_db.Scalar("SELECT COALESCE(SUM(size_bytes),0) FROM items WHERE deleted_at IS NULL") ?? 0L);
            try
            {
                stats.DatabaseBytes = new[] { _db.Path, _db.Path + "-wal", _db.Path + "-shm" }
                    .Where(File.Exists).Sum(p => new FileInfo(p).Length);
            }
            catch { }
            return stats;
        }

        public List<string> GetAllTags()
        {
            var raw = _db.Query("SELECT tags FROM items WHERE tags <> '' AND deleted_at IS NULL", r => r.GetString(0));
            return raw.SelectMany(t => t.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToList();
        }

        // ---------- Folders ----------
        public List<Folder> GetFolders()
        {
            return _db.Query(@"SELECT f.*,
                    (SELECT COUNT(*) FROM items i WHERE i.folder_id=f.id AND i.deleted_at IS NULL) AS item_count,
                    (SELECT COUNT(*) FROM folders c WHERE c.parent_id=f.id) AS sub_count
                FROM folders f ORDER BY f.name COLLATE NOCASE",
                r => new Folder
                {
                    Id = r.GetInt64(r.GetOrdinal("id")),
                    Name = r.GetString(r.GetOrdinal("name")),
                    Color = r.GetString(r.GetOrdinal("color")),
                    ParentId = r.IsDBNull(r.GetOrdinal("parent_id")) ? null : r.GetInt64(r.GetOrdinal("parent_id")),
                    IsFavorite = r.GetInt64(r.GetOrdinal("is_favorite")) == 1,
                    CreatedAt = ParseDate(r, "created_at"),
                    ItemCount = r.GetInt32(r.GetOrdinal("item_count")),
                    SubfolderCount = r.GetInt32(r.GetOrdinal("sub_count"))
                });
        }

        public Folder? GetFolder(long id) => GetFolders().FirstOrDefault(f => f.Id == id);

        public void SetFolderFavorite(long id, bool favorite) =>
            _db.Execute("UPDATE folders SET is_favorite=@favorite WHERE id=@id", ("@favorite", favorite ? 1 : 0), ("@id", id));

        public long CreateFolder(string name, string color, long? parentId)
        {
            return Convert.ToInt64(_db.Scalar(
                "INSERT INTO folders (name,color,parent_id,created_at) VALUES (@name,@color,@parent,@created); SELECT last_insert_rowid();",
                ("@name", string.IsNullOrWhiteSpace(name) ? "Nieuwe map" : name.Trim()),
                ("@color", color), ("@parent", parentId), ("@created", Iso(DateTime.Now))));
        }

        public void UpdateFolder(long id, string name, string color, long? parentId)
        {
            _db.Execute("UPDATE folders SET name=@name, color=@color, parent_id=@parent WHERE id=@id",
                ("@name", name.Trim()), ("@color", color), ("@parent", parentId), ("@id", id));
        }

        public void DeleteFolder(long id)
        {
            _db.Execute("UPDATE items SET folder_id=NULL WHERE folder_id=@id", ("@id", id));
            _db.Execute("UPDATE folders SET parent_id=NULL WHERE parent_id=@id", ("@id", id));
            _db.Execute("DELETE FROM folders WHERE id=@id", ("@id", id));
        }

        // ---------- Linked folders ----------
        public List<LinkedFolder> GetLinkedFolders() =>
            _db.Query("SELECT * FROM linked_folders ORDER BY name COLLATE NOCASE", r => new LinkedFolder
            {
                Id = r.GetInt64(r.GetOrdinal("id")),
                Name = r.GetString(r.GetOrdinal("name")),
                Path = r.GetString(r.GetOrdinal("path")),
                AddedAt = ParseDate(r, "added_at")
            });

        public void AddLinkedFolder(string name, string path)
        {
            _db.Execute("INSERT OR IGNORE INTO linked_folders (name,path,added_at) VALUES (@name,@path,@added)",
                ("@name", name), ("@path", path), ("@added", Iso(DateTime.Now)));
        }

        public void RemoveLinkedFolder(long id) =>
            _db.Execute("DELETE FROM linked_folders WHERE id=@id", ("@id", id));

        // ---------- Settings ----------
        public string? GetSetting(string key) =>
            _db.Scalar("SELECT value FROM settings WHERE key=@key", ("@key", key)) as string;

        public void SetSetting(string key, string? value)
        {
            _db.Execute("INSERT INTO settings (key,value) VALUES (@key,@value) ON CONFLICT(key) DO UPDATE SET value=@value",
                ("@key", key), ("@value", value));
        }

        public void SeedIfEmpty()
        {
            var count = Convert.ToInt32(_db.Scalar("SELECT COUNT(*) FROM items") ?? 0);
            if (count > 0) return;

            var folders = new (string Name, string Color)[]
            {
                ("Werk", "#C8392B"),
                ("Inspiratie", "#147D74"),
                ("Persoonlijk", "#E9A13B")
            };
            foreach (var (name, color) in folders)
                CreateFolder(name, color, null);

            var welcome = new LibraryItem
            {
                Kind = ItemKind.Note,
                Title = "Welkom bij SaveSpace",
                Tags = "welkom, start",
                IsFavorite = true,
                Content = "Dit is jouw plek voor alles wat je wilt bewaren.\n\n" +
                          "Notities\nSchrijf ideeën, lijsten en langere teksten. SaveSpace bewaart ze automatisch terwijl je typt.\n\n" +
                          "Weblinks\nBewaar interessante pagina's met titel en omschrijving. Klik op een link om hem in je browser te openen.\n\n" +
                          "Afbeeldingen en video's\nImporteer foto's en films. Bekijk ze groot, zoom in, draai ze en speel video's direct af.\n\n" +
                          "Documenten\nVoeg pdf's, Word-bestanden en andere documenten toe. Je opent ze met het programma dat je al gebruikt.\n\n" +
                          "Mappen\nMaak eigen mappen en submappen, of koppel een bestaande map van je computer via Instellingen.\n\n" +
                          "Alles blijft lokaal op deze computer. Originele bestanden worden nooit automatisch verwijderd."
            };
            InsertItem(welcome);
        }
    }
}
