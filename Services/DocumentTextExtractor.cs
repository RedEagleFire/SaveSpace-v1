using System.IO.Compression;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SaveSpace.Services
{
    /// <summary>Reads searchable text from common local document formats without changing the source file.</summary>
    public static class DocumentTextExtractor
    {
        private const int MaxCharacters = 1_000_000;
        private static readonly XNamespace Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private static readonly XNamespace Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";

        public static string Extract(string path)
        {
            try
            {
                return Path.GetExtension(path).ToLowerInvariant() switch
                {
                    ".docx" => ExtractWord(path),
                    ".pptx" => ExtractPresentation(path),
                    ".xlsx" => ExtractWorkbook(path),
                    ".txt" or ".md" or ".csv" or ".json" or ".xml" or ".html" or ".htm" => ExtractText(path),
                    ".pdf" => ExtractBasicPdfText(path),
                    _ => ""
                };
            }
            catch (Exception ex) { AppPaths.Log($"Documenttekst niet gelezen ({Path.GetFileName(path)}): {ex.Message}"); return ""; }
        }

        private static string ExtractWord(string path)
        {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.GetEntry("word/document.xml");
            if (entry == null) return "";
            using var input = entry.Open();
            var doc = XDocument.Load(input);
            return Limit(string.Join(" ", doc.Descendants(Word + "t").Select(t => t.Value)));
        }

        private static string ExtractPresentation(string path)
        {
            using var zip = ZipFile.OpenRead(path);
            var slides = zip.Entries.Where(e => Regex.IsMatch(e.FullName, @"^ppt/slides/slide\d+\.xml$", RegexOptions.IgnoreCase))
                .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase);
            var text = new StringBuilder();
            foreach (var slide in slides)
            {
                using var input = slide.Open();
                var doc = XDocument.Load(input);
                text.Append(' ').AppendJoin(' ', doc.Descendants(Drawing + "t").Select(t => t.Value));
                if (text.Length >= MaxCharacters) break;
            }
            return Limit(text.ToString());
        }

        private static string ExtractWorkbook(string path)
        {
            using var zip = ZipFile.OpenRead(path);
            var text = new StringBuilder();
            foreach (var entry in zip.Entries.Where(e => e.FullName.Equals("xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase) ||
                         Regex.IsMatch(e.FullName, @"^xl/worksheets/sheet\d+\.xml$", RegexOptions.IgnoreCase)))
            {
                using var input = entry.Open();
                var doc = XDocument.Load(input);
                text.Append(' ').AppendJoin(' ', doc.DescendantNodes().OfType<XText>().Select(t => t.Value));
                if (text.Length >= MaxCharacters) break;
            }
            return Limit(text.ToString());
        }

        private static string ExtractText(string path)
        {
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var buffer = new char[16_384];
            var text = new StringBuilder();
            while (text.Length < MaxCharacters)
            {
                var read = reader.Read(buffer, 0, Math.Min(buffer.Length, MaxCharacters - text.Length));
                if (read == 0) break;
                text.Append(buffer, 0, read);
            }
            var plain = Regex.Replace(text.ToString(), "<[^>]+>", " ");
            return Limit(plain);
        }

        // Ondersteunt zichtbare tekstoperatoren in ongecomprimeerde PDF-contentstreams.
        // Bestanden met gecomprimeerde streams of scans blijven wel bewaard, maar leveren hier geen doorzoekbare tekst op.
        private static string ExtractBasicPdfText(string path)
        {
            using var input = File.OpenRead(path);
            var bytes = new byte[(int)Math.Min(input.Length, 32L * 1024 * 1024)];
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = input.Read(bytes, offset, bytes.Length - offset);
                if (read == 0) break;
                offset += read;
            }
            if (offset != bytes.Length) Array.Resize(ref bytes, offset);
            var raw = Encoding.Latin1.GetString(bytes);
            var matches = Regex.Matches(raw, @"\(((?:\\.|[^\\)])*)\)\s*Tj|\[((?:.|\r|\n)*?)\]\s*TJ");
            var text = new StringBuilder();
            foreach (Match match in matches)
            {
                var fragment = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                foreach (Match part in Regex.Matches(fragment, @"\(((?:\\.|[^\\)])*)\)")) text.Append(' ').Append(UnescapePdf(part.Groups[1].Value));
                if (match.Groups[1].Success) text.Append(' ').Append(UnescapePdf(fragment));
                if (text.Length >= MaxCharacters) break;
            }
            return Limit(text.ToString());
        }

        private static string UnescapePdf(string value) => Regex.Replace(value, @"\\([nrtbf()\\])", m => m.Groups[1].Value switch
        {
            "n" => " ", "r" => " ", "t" => " ", "b" => "", "f" => "", _ => m.Groups[1].Value
        });

        private static string Limit(string value) => value.Length > MaxCharacters ? value[..MaxCharacters] : value;
    }
}
