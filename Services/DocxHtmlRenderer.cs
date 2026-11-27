using System.IO.Compression;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SaveSpace.Services
{
    /// <summary>Renders the readable parts of a DOCX package to safe, local HTML for WebView2.</summary>
    public static class DocxHtmlRenderer
    {
        private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
        private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

        public static string RenderToFile(string docxPath)
        {
            using var archive = ZipFile.OpenRead(docxPath);
            var documentEntry = archive.GetEntry("word/document.xml")
                ?? throw new InvalidDataException("Dit Word-bestand bevat geen documenttekst.");
            using var documentStream = documentEntry.Open();
            var document = XDocument.Load(documentStream);

            var relationships = new Dictionary<string, string>(StringComparer.Ordinal);
            var relEntry = archive.GetEntry("word/_rels/document.xml.rels");
            if (relEntry != null)
            {
                using var relStream = relEntry.Open();
                var relDoc = XDocument.Load(relStream);
                foreach (var rel in relDoc.Root?.Elements(Rel + "Relationship") ?? Enumerable.Empty<XElement>())
                {
                    var id = (string?)rel.Attribute("Id");
                    var target = (string?)rel.Attribute("Target");
                    var mode = (string?)rel.Attribute("TargetMode");
                    if (id != null && target != null && !string.Equals(mode, "External", StringComparison.OrdinalIgnoreCase))
                        relationships[id] = NormalizeMediaPath(target);
                }
            }

            var body = document.Root?.Element(W + "body") ?? throw new InvalidDataException("De inhoud van dit Word-bestand ontbreekt.");
            var html = new StringBuilder(32_000);
            html.Append("<!doctype html><html lang=\"nl\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><style>")
                .Append("html{background:#141518;color:#26272b;font-family:'Segoe UI',Arial,sans-serif}body{margin:0;padding:28px 18px}.page{box-sizing:border-box;background:#fff;max-width:900px;min-height:100vh;margin:0 auto;padding:68px 76px;box-shadow:0 12px 40px #0005;line-height:1.55;overflow-wrap:anywhere}p{margin:0 0 10px}h1{font-size:26px;margin:22px 0 12px}h2{font-size:21px;margin:20px 0 10px}h3{font-size:17px;margin:16px 0 8px}table{border-collapse:collapse;width:100%;margin:12px 0}td,th{border:1px solid #c9cbd0;padding:6px 8px;vertical-align:top}img{max-width:100%;height:auto;object-fit:contain;margin:8px 0}li{margin:3px 0}.empty{color:#74777f;font-style:italic}@media(max-width:700px){body{padding:0}.page{padding:28px 24px;box-shadow:none}}")
                .Append("</style></head><body><main class=\"page\">");

            foreach (var child in body.Elements())
            {
                if (child.Name == W + "p") RenderParagraph(child, html, archive, relationships);
                else if (child.Name == W + "tbl") RenderTable(child, html, archive, relationships);
            }
            if (html.ToString().EndsWith("<main class=\"page\">", StringComparison.Ordinal))
                html.Append("<p class=\"empty\">Dit Word-bestand bevat geen leesbare tekst.</p>");
            html.Append("</main></body></html>");

            var outputDirectory = Path.Combine(AppPaths.Root, "Cache", "DocxRendered");
            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(outputDirectory, Guid.NewGuid().ToString("N") + ".html");
            File.WriteAllText(outputPath, html.ToString(), new UTF8Encoding(false));
            return outputPath;
        }

        private static void RenderParagraph(XElement paragraph, StringBuilder html, ZipArchive archive, Dictionary<string, string> relationships)
        {
            var properties = paragraph.Element(W + "pPr");
            var style = (string?)properties?.Element(W + "pStyle")?.Attribute(W + "val") ?? "";
            var heading = style.ToLowerInvariant() switch
            {
                "title" or "subtitle" => 1,
                "heading1" or "heading 1" => 1,
                "heading2" or "heading 2" => 2,
                "heading3" or "heading 3" => 3,
                _ => 0
            };
            var tag = heading switch { 1 => "h1", 2 => "h2", 3 => "h3", _ => "p" };
            var alignment = (string?)properties?.Element(W + "jc")?.Attribute(W + "val");
            var alignCss = alignment switch { "center" => "text-align:center;", "right" => "text-align:right;", "both" => "text-align:justify;", _ => "" };
            html.Append('<').Append(tag);
            if (alignCss.Length > 0) html.Append(" style=\"").Append(alignCss).Append('"');
            html.Append('>');
            if (properties?.Element(W + "numPr") != null) html.Append("• ");
            foreach (var item in paragraph.Elements())
            {
                if (item.Name == W + "r") RenderRun(item, html, archive, relationships);
                else if (item.Name == W + "hyperlink")
                    foreach (var run in item.Elements(W + "r")) RenderRun(run, html, archive, relationships);
            }
            html.Append("</").Append(tag).Append('>');
        }

        private static void RenderRun(XElement run, StringBuilder html, ZipArchive archive, Dictionary<string, string> relationships)
        {
            var props = run.Element(W + "rPr");
            var bold = IsOn(props?.Element(W + "b"));
            var italic = IsOn(props?.Element(W + "i"));
            var underline = IsOn(props?.Element(W + "u"));
            var strike = IsOn(props?.Element(W + "strike"));
            var color = (string?)props?.Element(W + "color")?.Attribute(W + "val");
            if (!string.IsNullOrEmpty(color) && Regex.IsMatch(color, "^[0-9A-Fa-f]{6}$")) html.Append("<span style=\"color:#").Append(color).Append("\">");
            if (bold) html.Append("<strong>");
            if (italic) html.Append("<em>");
            if (underline || strike)
            {
                html.Append("<span style=\"text-decoration:");
                if (underline) html.Append("underline");
                if (underline && strike) html.Append(' ');
                if (strike) html.Append("line-through");
                html.Append(";\">");
            }

            foreach (var child in run.Elements())
            {
                if (child.Name == W + "t") html.Append(WebUtility.HtmlEncode(child.Value));
                else if (child.Name == W + "tab") html.Append("&emsp;");
                else if (child.Name == W + "br" || child.Name == W + "cr") html.Append("<br>");
                else if (child.Name == W + "drawing") RenderDrawing(child, html, archive, relationships);
            }

            if (underline || strike) html.Append("</span>");
            if (italic) html.Append("</em>");
            if (bold) html.Append("</strong>");
            if (!string.IsNullOrEmpty(color) && Regex.IsMatch(color, "^[0-9A-Fa-f]{6}$")) html.Append("</span>");
        }

        private static void RenderDrawing(XElement drawing, StringBuilder html, ZipArchive archive, Dictionary<string, string> relationships)
        {
            var id = (string?)drawing.Descendants(A + "blip").FirstOrDefault()?.Attribute(R + "embed");
            if (id == null || !relationships.TryGetValue(id, out var mediaPath)) return;
            var entry = archive.GetEntry(mediaPath);
            if (entry == null || entry.Length > 24 * 1024 * 1024) return;
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var ext = Path.GetExtension(mediaPath).ToLowerInvariant();
            var mime = ext switch { ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".svg" => "image/svg+xml", _ => "image/png" };
            html.Append("<img alt=\"Afbeelding in Word-document\" src=\"data:").Append(mime).Append(";base64,")
                .Append(Convert.ToBase64String(memory.ToArray())).Append("\">");
        }

        private static void RenderTable(XElement table, StringBuilder html, ZipArchive archive, Dictionary<string, string> relationships)
        {
            html.Append("<table><tbody>");
            foreach (var row in table.Elements(W + "tr"))
            {
                html.Append("<tr>");
                foreach (var cell in row.Elements(W + "tc"))
                {
                    html.Append("<td>");
                    foreach (var child in cell.Elements())
                    {
                        if (child.Name == W + "p") RenderParagraph(child, html, archive, relationships);
                        else if (child.Name == W + "tbl") RenderTable(child, html, archive, relationships);
                    }
                    html.Append("</td>");
                }
                html.Append("</tr>");
            }
            html.Append("</tbody></table>");
        }

        private static bool IsOn(XElement? property) => property != null &&
            !string.Equals((string?)property.Attribute(W + "val"), "0", StringComparison.Ordinal) &&
            !string.Equals((string?)property.Attribute(W + "val"), "false", StringComparison.OrdinalIgnoreCase);

        private static string NormalizeMediaPath(string target)
        {
            var path = target.Replace('\\', '/').TrimStart('/');
            if (!path.StartsWith("word/", StringComparison.OrdinalIgnoreCase)) path = "word/" + path;
            return path;
        }
    }
}
