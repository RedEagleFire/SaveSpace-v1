using System.IO.Compression;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SaveSpace.Services
{
    /// <summary>Builds safe, local HTML previews for Open XML PowerPoint and Excel files.</summary>
    public static class OfficeXmlRenderer
    {
        private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
        private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
        private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
        private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        public static string RenderToFile(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var html = ext == ".pptx" ? RenderPresentation(path) : ext == ".xlsx" ? RenderWorkbook(path)
                : throw new NotSupportedException("Alleen PPTX- en XLSX-bestanden kunnen hier worden weergegeven.");
            var directory = Path.Combine(AppPaths.Root, "Cache", "OfficeRendered");
            Directory.CreateDirectory(directory);
            var output = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".html");
            File.WriteAllText(output, html, new UTF8Encoding(false));
            return output;
        }

        private static string RenderPresentation(string path)
        {
            using var archive = ZipFile.OpenRead(path);
            var presentation = LoadXml(archive, "ppt/presentation.xml");
            var rels = LoadRelationships(archive, "ppt/_rels/presentation.xml.rels", "ppt");
            var slides = new List<(int Number, string Path)>();
            var number = 1;
            foreach (var slide in presentation.Root?.Element(P + "sldIdLst")?.Elements(P + "sldId") ?? Enumerable.Empty<XElement>())
            {
                var id = (string?)slide.Attribute(R + "id");
                if (id != null && rels.TryGetValue(id, out var entry)) slides.Add((number, entry));
                number++;
            }

            var html = HtmlStart("Presentatie", "presentation");
            html.Append("<header><h1>").Append(WebUtility.HtmlEncode(Path.GetFileNameWithoutExtension(path))).Append("</h1><p>")
                .Append(slides.Count).Append(" dia's</p></header>");
            foreach (var (slideNumber, slidePath) in slides)
            {
                var slide = LoadXml(archive, slidePath);
                html.Append("<section class=\"slide\"><div class=\"slide-number\">Dia ").Append(slideNumber).Append("</div>");
                var shapes = slide.Root?.Element(P + "cSld")?.Element(P + "spTree")?.Elements() ?? Enumerable.Empty<XElement>();
                foreach (var shape in shapes)
                {
                    if (shape.Name == P + "sp")
                    {
                        var paragraphs = shape.Descendants(A + "p").ToList();
                        foreach (var paragraph in paragraphs)
                        {
                            var text = string.Concat(paragraph.Descendants(A + "t").Select(t => t.Value));
                            if (string.IsNullOrWhiteSpace(text)) continue;
                            html.Append("<p>").Append(WebUtility.HtmlEncode(text)).Append("</p>");
                        }
                    }
                    else if (shape.Name == P + "graphicFrame")
                    {
                        foreach (var cell in shape.Descendants(A + "tc"))
                        {
                            var text = string.Concat(cell.Descendants(A + "t").Select(t => t.Value));
                            if (!string.IsNullOrWhiteSpace(text)) html.Append("<span class=\"cell\">").Append(WebUtility.HtmlEncode(text)).Append("</span>");
                        }
                    }
                }
                html.Append("</section>");
            }
            if (slides.Count == 0) html.Append("<p class=\"empty\">Deze presentatie bevat geen leesbare dia's.</p>");
            return HtmlEnd(html);
        }

        private static string RenderWorkbook(string path)
        {
            using var archive = ZipFile.OpenRead(path);
            var workbook = LoadXml(archive, "xl/workbook.xml");
            var rels = LoadRelationships(archive, "xl/_rels/workbook.xml.rels", "xl");
            var sharedStrings = new List<string>();
            var shared = archive.GetEntry("xl/sharedStrings.xml");
            if (shared != null)
            {
                using var stream = shared.Open();
                var xml = XDocument.Load(stream);
                sharedStrings = (xml.Root?.Elements(S + "si") ?? Enumerable.Empty<XElement>())
                    .Select(si => string.Concat(si.Descendants(S + "t").Select(t => t.Value))).ToList();
            }

            var html = HtmlStart("Werkmap", "workbook");
            html.Append("<header><h1>").Append(WebUtility.HtmlEncode(Path.GetFileNameWithoutExtension(path))).Append("</h1><p>Excel-werkmap · leesvoorbeeld</p></header>");
            var sheetCount = 0;
            foreach (var sheet in workbook.Root?.Element(S + "sheets")?.Elements(S + "sheet") ?? Enumerable.Empty<XElement>())
            {
                var name = (string?)sheet.Attribute("name") ?? $"Blad {sheetCount + 1}";
                var id = (string?)sheet.Attribute(R + "id");
                if (id == null || !rels.TryGetValue(id, out var sheetPath)) continue;
                var sheetXml = LoadXml(archive, sheetPath);
                html.Append("<section class=\"sheet\"><h2>").Append(WebUtility.HtmlEncode(name)).Append("</h2><div class=\"grid\"><table>");
                foreach (var row in sheetXml.Descendants(S + "sheetData").Elements(S + "row").Take(1000))
                {
                    html.Append("<tr>");
                    var cells = row.Elements(S + "c").Take(100).ToList();
                    var lastColumn = cells.Count == 0 ? 0 : ColumnIndex((string?)cells[^1].Attribute("r") ?? "A1");
                    var cellIndex = 0;
                    for (var column = 1; column <= Math.Max(1, lastColumn); column++)
                    {
                        var reference = cells.ElementAtOrDefault(cellIndex);
                        var refColumn = reference == null ? -1 : ColumnIndex((string?)reference.Attribute("r") ?? "A1");
                        if (reference == null || refColumn != column)
                        {
                            html.Append("<td></td>");
                            continue;
                        }
                        html.Append("<td>").Append(WebUtility.HtmlEncode(CellValue(reference, sharedStrings))).Append("</td>");
                        cellIndex++;
                    }
                    html.Append("</tr>");
                }
                html.Append("</table></div></section>");
                sheetCount++;
            }
            if (sheetCount == 0) html.Append("<p class=\"empty\">Deze werkmap bevat geen leesbare werkbladen.</p>");
            return HtmlEnd(html);
        }

        private static string CellValue(XElement cell, List<string> sharedStrings)
        {
            var type = (string?)cell.Attribute("t");
            if (type == "inlineStr") return string.Concat(cell.Descendants(S + "t").Select(t => t.Value));
            var value = (string?)cell.Element(S + "v") ?? "";
            if (type == "s" && int.TryParse(value, out var index) && index >= 0 && index < sharedStrings.Count)
                return sharedStrings[index];
            if (type == "b") return value == "1" ? "TRUE" : "FALSE";
            return value;
        }

        private static int ColumnIndex(string reference)
        {
            var letters = Regex.Match(reference, "^[A-Za-z]+").Value.ToUpperInvariant();
            var value = 0;
            foreach (var ch in letters) value = value * 26 + ch - 'A' + 1;
            return value;
        }

        private static Dictionary<string, string> LoadRelationships(ZipArchive archive, string path, string baseDirectory)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var entry = archive.GetEntry(path);
            if (entry == null) return result;
            using var stream = entry.Open();
            var xml = XDocument.Load(stream);
            foreach (var rel in xml.Root?.Elements(Rel + "Relationship") ?? Enumerable.Empty<XElement>())
            {
                var id = (string?)rel.Attribute("Id");
                var target = (string?)rel.Attribute("Target");
                var mode = (string?)rel.Attribute("TargetMode");
                if (id == null || target == null || mode == "External") continue;
                var joined = target.StartsWith('/') ? target.TrimStart('/') : baseDirectory + "/" + target;
                var parts = new List<string>();
                foreach (var part in joined.Replace('\\', '/').Split('/'))
                {
                    if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
                    else if (part.Length > 0 && part != ".") parts.Add(part);
                }
                result[id] = string.Join('/', parts);
            }
            return result;
        }

        private static XDocument LoadXml(ZipArchive archive, string entryName)
        {
            var entry = archive.GetEntry(entryName) ?? throw new InvalidDataException("Het Office-bestand bevat een ontbrekend onderdeel: " + entryName);
            using var stream = entry.Open();
            return XDocument.Load(stream);
        }

        private static StringBuilder HtmlStart(string title, string theme)
        {
            var html = new StringBuilder(16_000);
            html.Append("<!doctype html><html lang=\"nl\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><style>")
                .Append("html{background:#141518;color:#22242a;font-family:'Segoe UI',Arial,sans-serif}body{margin:0;padding:28px 18px;background:#141518}.workbook,header{max-width:1100px;margin:0 auto 20px;color:#f3f3f5}h1{font-size:22px;margin:0 0 4px}header p{color:#a5a7af;margin:0}.slide,.sheet{box-sizing:border-box;background:#fff;max-width:1100px;min-height:320px;margin:0 auto 20px;padding:48px 58px;box-shadow:0 8px 30px #0004;border-radius:6px}.slide{aspect-ratio:16/9;min-height:480px;display:flex;flex-direction:column;justify-content:center;position:relative}.slide p{font-size:22px;margin:8px 0;line-height:1.42}.slide-number{position:absolute;top:12px;right:16px;color:#888;font-size:12px}.sheet{min-height:200px;padding:22px}.sheet h2{font-size:17px;margin:0 0 12px}.grid{overflow:auto}table{border-collapse:collapse;white-space:pre-wrap}td{border:1px solid #d7d9de;min-width:70px;padding:6px 8px;font-size:13px}.empty{color:#90939a;font-style:italic;text-align:center;padding:60px 10px}@media(max-width:700px){body{padding:0}.slide,.sheet{padding:24px;min-height:280px;aspect-ratio:auto}.slide p{font-size:17px}}");
            html.Append("</style></head><body class=\"").Append(theme).Append("\">");
            return html;
        }

        private static string HtmlEnd(StringBuilder html)
        {
            html.Append("</body></html>");
            return html.ToString();
        }
    }
}
