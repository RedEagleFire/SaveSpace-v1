using System;
using System.IO;
using System.Linq;
using System.Text;
using SaveSpace.Models;

namespace SaveSpace.Services
{
    /// <summary>Zet een notitie om naar een leesbaar Markdown-bestand, zonder dat er iets van de bibliotheek verloren gaat.</summary>
    public static class NoteExport
    {
        public static string ToMarkdown(LibraryItem note)
        {
            var sb = new StringBuilder();
            sb.Append("# ").AppendLine(string.IsNullOrWhiteSpace(note.Title) ? "Naamloos" : note.Title.Trim());
            sb.AppendLine();

            var meta = new[]
            {
                string.IsNullOrWhiteSpace(note.FolderName) ? null : "Project: " + note.FolderName,
                string.IsNullOrWhiteSpace(note.Tags) ? null : "Tags: " + note.Tags,
                "Bijgewerkt: " + note.UpdatedAt.ToString("d MMMM yyyy, HH:mm")
            }.Where(m => m != null);
            foreach (var line in meta) sb.Append("_").Append(line).AppendLine("_  ");
            sb.AppendLine();

            sb.AppendLine((note.Content ?? "").TrimEnd());

            var dir = AppPaths.NoteFilesDir(note.Id);
            if (Directory.Exists(dir))
            {
                var files = Directory.GetFiles(dir).Select(f => Path.GetFileName(f)).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (files.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("## Bijlagen");
                    sb.AppendLine();
                    foreach (var file in files) sb.Append("- ").AppendLine(file);
                }
            }
            return sb.ToString();
        }

        public static void SaveAs(LibraryItem note, string path)
        {
            File.WriteAllText(path, ToMarkdown(note), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
    }
}
