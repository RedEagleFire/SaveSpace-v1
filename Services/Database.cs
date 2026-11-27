using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace SaveSpace.Services
{
    public class Database
    {
        private readonly string _path;
        public string Path => _path;
        public bool FullTextAvailable { get; private set; } = true;

        public Database(string path)
        {
            _path = path;
        }

        public SqliteConnection Open()
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = _path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                ForeignKeys = true,
                Pooling = true
            };
            var conn = new SqliteConnection(builder.ToString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=6000;";
            cmd.ExecuteNonQuery();
            return conn;
        }

        /// <summary>Controleert vooraf of de map van de database bestaat en beschrijfbaar is, met een duidelijke melding.</summary>
        private void EnsureAccessible()
        {
            var dir = System.IO.Path.GetDirectoryName(_path) ?? "";
            try
            {
                System.IO.Directory.CreateDirectory(dir);
                var probe = System.IO.Path.Combine(dir, ".savespace-schrijftest");
                System.IO.File.WriteAllText(probe, "ok");
                System.IO.File.Delete(probe);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "SaveSpace kan niet schrijven in deze map:\n" + dir + "\n\n" + ex.Message +
                    "\n\nControleer of de map bestaat en of je er in mag schrijven. " +
                    "Start SaveSpace vanuit je eigen Windows-account, niet vanuit een sandbox of AI-agent. " +
                    "Staat de map in je config.json? Pas die dan aan of verwijder dat bestand.", ex);
            }
        }

        public void Initialize()
        {
            EnsureAccessible();
            using var conn = Open();
            var statements = new[]
            {
                @"CREATE TABLE IF NOT EXISTS folders (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    color TEXT NOT NULL DEFAULT '#C8392B',
                    parent_id INTEGER NULL REFERENCES folders(id) ON DELETE SET NULL,
                    created_at TEXT NOT NULL
                );",
                @"CREATE TABLE IF NOT EXISTS items (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    kind TEXT NOT NULL,
                    title TEXT NOT NULL DEFAULT '',
                    content TEXT NOT NULL DEFAULT '',
                    content_rtf TEXT NULL,
                    url TEXT NULL,
                    file_path TEXT NULL,
                    thumb_path TEXT NULL,
                    mime TEXT NULL,
                    size_bytes INTEGER NOT NULL DEFAULT 0,
                    width INTEGER NOT NULL DEFAULT 0,
                    height INTEGER NOT NULL DEFAULT 0,
                    duration REAL NOT NULL DEFAULT 0,
                    tags TEXT NOT NULL DEFAULT '',
                    folder_id INTEGER NULL REFERENCES folders(id) ON DELETE SET NULL,
                    is_favorite INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    deleted_at TEXT NULL
                );",
                @"CREATE TABLE IF NOT EXISTS linked_folders (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    path TEXT NOT NULL UNIQUE,
                    added_at TEXT NOT NULL
                );",
                @"CREATE TABLE IF NOT EXISTS settings (
                    key TEXT PRIMARY KEY,
                    value TEXT NULL
                );",
                @"CREATE INDEX IF NOT EXISTS idx_items_kind ON items(kind);",
                @"CREATE INDEX IF NOT EXISTS idx_items_folder ON items(folder_id);",
                @"CREATE INDEX IF NOT EXISTS idx_items_deleted ON items(deleted_at);",
                @"CREATE INDEX IF NOT EXISTS idx_items_updated ON items(updated_at);"
            };

            foreach (var sql in statements)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }

            using (var pragma = conn.CreateCommand())
            {
                // WAL is blijvend in het bestand; één keer instellen is genoeg.
                pragma.CommandText = "PRAGMA journal_mode=WAL;";
                pragma.ExecuteNonQuery();
            }

            // Stabiele id per notitie, zodat een geïmporteerd notitiebestand herkend kan worden.
            EnsureColumn(conn, "items", "uuid", "TEXT NULL");
            EnsureColumn(conn, "folders", "is_favorite", "INTEGER NOT NULL DEFAULT 0");
            using (var idx = conn.CreateCommand())
            {
                idx.CommandText = "CREATE INDEX IF NOT EXISTS idx_items_uuid ON items(uuid);";
                idx.ExecuteNonQuery();
            }
            using (var fill = conn.CreateCommand())
            {
                fill.CommandText = "UPDATE items SET uuid = lower(hex(randomblob(16))) WHERE uuid IS NULL OR uuid = '';";
                fill.ExecuteNonQuery();
            }

            try
            {
                var ftsExisted = TableExists(conn, "items_fts");
                var fts = new[]
                {
                    @"CREATE VIRTUAL TABLE IF NOT EXISTS items_fts USING fts5(
                        title, content, tags,
                        content='items', content_rowid='id',
                        tokenize='unicode61 remove_diacritics 2'
                    );",
                    @"CREATE TRIGGER IF NOT EXISTS items_fts_ai AFTER INSERT ON items BEGIN
                        INSERT INTO items_fts(rowid, title, content, tags) VALUES (new.id, new.title, new.content, new.tags);
                    END;",
                    @"CREATE TRIGGER IF NOT EXISTS items_fts_ad AFTER DELETE ON items BEGIN
                        INSERT INTO items_fts(items_fts, rowid, title, content, tags) VALUES('delete', old.id, old.title, old.content, old.tags);
                    END;",
                    @"CREATE TRIGGER IF NOT EXISTS items_fts_au AFTER UPDATE ON items BEGIN
                        INSERT INTO items_fts(items_fts, rowid, title, content, tags) VALUES('delete', old.id, old.title, old.content, old.tags);
                        INSERT INTO items_fts(rowid, title, content, tags) VALUES (new.id, new.title, new.content, new.tags);
                    END;"
                };
                foreach (var sql in fts)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                }
                if (!ftsExisted)
                {
                    // Alleen bij een nieuwe index: bestaande index blijft via de triggers actueel.
                    using var rebuild = conn.CreateCommand();
                    rebuild.CommandText = "INSERT INTO items_fts(items_fts) VALUES('rebuild');";
                    rebuild.ExecuteNonQuery();
                }
                FullTextAvailable = true;
            }
            catch (Exception ex)
            {
                FullTextAvailable = false;
                AppPaths.Log("FTS5 niet beschikbaar, LIKE-fallback actief: " + ex.Message);
            }
        }

        private static bool TableExists(SqliteConnection conn, string name)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name";
            cmd.Parameters.AddWithValue("@name", name);
            return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        }

        private static void EnsureColumn(SqliteConnection conn, string table, string column, string definition)
        {
            using (var check = conn.CreateCommand())
            {
                check.CommandText = $"PRAGMA table_info({table});";
                using var reader = check.ExecuteReader();
                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
                }
            }
            using var alter = conn.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
            alter.ExecuteNonQuery();
        }

        public int Execute(string sql, params (string Name, object? Value)[] args)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (name, value) in args)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            return cmd.ExecuteNonQuery();
        }

        public object? Scalar(string sql, params (string Name, object? Value)[] args)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (name, value) in args)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            return cmd.ExecuteScalar();
        }

        public List<T> Query<T>(string sql, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] args)
        {
            var list = new List<T>();
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (name, value) in args)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) list.Add(map(reader));
            return list;
        }

        public T? QueryOne<T>(string sql, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] args) where T : class
        {
            var list = Query(sql, map, args);
            return list.Count > 0 ? list[0] : null;
        }

        public void Checkpoint()
        {
            try
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                cmd.ExecuteNonQuery();
            }
            catch { }
        }
    }
}
