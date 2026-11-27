using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SaveSpace.Models;

namespace SaveSpace.Services
{
    public class ImportResult
    {
        public List<LibraryItem> Items { get; } = new();
        public List<string> Errors { get; } = new();
    }

    public sealed record ImportProgress(int Current, int Total, string FileName);

    public static class ImageTools
    {
        public static BitmapImage? LoadRaw(string path, int decodeWidth = 0)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                if (decodeWidth > 0) bmp.DecodePixelWidth = decodeWidth;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        public static int GetOrientation(BitmapSource source)
        {
            try
            {
                if (source.Metadata is BitmapMetadata meta)
                {
                    var value = meta.GetQuery("/app1/ifd/{ushort=274}") ?? meta.GetQuery("/ifd/{ushort=274}");
                    if (value is ushort u) return u;
                    if (value is int i) return i;
                }
            }
            catch { }
            return 1;
        }

        public static BitmapSource Orient(BitmapSource source, int orientation)
        {
            if (orientation is <= 1 or > 8) return source;
            try
            {
                var swap = orientation is 5 or 6 or 7 or 8;
                var width = swap ? source.PixelHeight : source.PixelWidth;
                var height = swap ? source.PixelWidth : source.PixelHeight;
                double angle = orientation switch { 3 => 180, 6 => 90, 8 => 270, _ => 0 };
                var flipX = orientation is 2 or 5 or 7;
                var flipY = orientation is 4 or 5 or 7;

                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.PushTransform(new TranslateTransform(width / 2.0, height / 2.0));
                    if (angle != 0) dc.PushTransform(new RotateTransform(angle));
                    if (flipX || flipY) dc.PushTransform(new ScaleTransform(flipX ? -1 : 1, flipY ? -1 : 1));
                    dc.DrawImage(source, new Rect(-source.PixelWidth / 2.0, -source.PixelHeight / 2.0,
                        source.PixelWidth, source.PixelHeight));
                }
                var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(visual);
                rtb.Freeze();
                return rtb;
            }
            catch { return source; }
        }

        public static void SavePng(BitmapSource source, string path)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }
    }

    public static class ShellThumbnail
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx; public int cy; }

        [Flags]
        private enum SIIGBF
        {
            ResizeToFit = 0x00,
            BiggerSizeOk = 0x01,
            MemoryOnly = 0x02,
            IconOnly = 0x04,
            ThumbnailOnly = 0x08,
            InCacheOnly = 0x10
        }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            void GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object shellItem);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        public static BitmapSource? GetImage(string path, int size = 640)
        {
            IntPtr hBitmap = IntPtr.Zero;
            try
            {
                var iid = typeof(IShellItemImageFactory).GUID;
                SHCreateItemFromParsingName(path, IntPtr.Zero, iid, out var item);
                var factory = (IShellItemImageFactory)item;
                factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF.ThumbnailOnly | SIIGBF.BiggerSizeOk, out hBitmap);
                if (hBitmap == IntPtr.Zero) return null;
                var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
            }
        }
    }

    public class MediaImportService
    {
        public static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".ico", ".webp" };
        public static readonly string[] VideoExtensions = { ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm", ".mpg", ".mpeg" };
        public static readonly string[] DocumentExtensions =
        {
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".md", ".rtf", ".csv",
            ".epub", ".zip", ".rar", ".7z", ".json", ".xml", ".html", ".htm", ".odt", ".ods", ".odp"
        };
        public static readonly string[] AudioExtensions = { ".mp3", ".wav", ".m4a", ".ogg", ".flac", ".aac", ".wma" };

        public ImportResult Import(IEnumerable<string> files, ItemKind kind, long? folderId = null,
            IProgress<ImportProgress>? progress = null, Func<string, bool>? duplicateDecision = null)
        {
            var result = new ImportResult();
            var fileList = files.Where(File.Exists).ToList();
            for (var index = 0; index < fileList.Count; index++)
            {
                var file = fileList[index];
                try
                {
                    if (IsDuplicate(file, kind) && duplicateDecision != null && !duplicateDecision(file)) continue;
                    var item = kind switch
                    {
                        ItemKind.Image => ImportImage(file, folderId),
                        ItemKind.Video => ImportVideo(file, folderId),
                        ItemKind.Audio => ImportAudio(file, folderId),
                        _ => ImportDocument(file, folderId)
                    };
                    if (item != null) result.Items.Add(item);
                }
                catch (Exception ex)
                {
                    AppPaths.Log($"Import mislukt ({file}): {ex.Message}");
                    result.Errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                }
                finally { progress?.Report(new ImportProgress(index + 1, fileList.Count, Path.GetFileName(file))); }
            }
            return result;
        }

        private static bool IsDuplicate(string source, ItemKind kind)
        {
            try
            {
                var size = new FileInfo(source).Length;
                var candidates = AppServices.Library.GetItems(kind, limit: LibraryService.AllItems)
                    .Where(item => item.SizeBytes == size && !string.IsNullOrWhiteSpace(item.FilePath) && File.Exists(item.FilePath));
                using var input = File.OpenRead(source);
                var hash = SHA256.HashData(input);
                foreach (var item in candidates)
                {
                    using var existing = File.OpenRead(item.FilePath!);
                    if (CryptographicOperations.FixedTimeEquals(hash, SHA256.HashData(existing))) return true;
                }
            }
            catch (Exception ex) { AppPaths.Log("Dubbel bestand controleren mislukt: " + ex.Message); }
            return false;
        }

        private static string UniqueDestination(string directory, string sourceName)
        {
            var safeName = string.Join("_", sourceName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "bestand";
            var name = Path.GetFileNameWithoutExtension(safeName);
            var ext = Path.GetExtension(safeName).ToLowerInvariant();
            var target = Path.Combine(directory, name + ext);
            var counter = 2;
            while (File.Exists(target))
            {
                target = Path.Combine(directory, $"{name} ({counter}){ext}");
                counter++;
            }
            return target;
        }

        public LibraryItem? ImportImage(string path, long? folderId)
        {
            var target = UniqueDestination(AppPaths.ImagesDir, Path.GetFileName(path));
            File.Copy(path, target, false);
            var raw = ImageTools.LoadRaw(target, 720);
            var orientation = raw != null ? ImageTools.GetOrientation(raw) : 1;
            var width = raw?.PixelWidth ?? 0;
            var height = raw?.PixelHeight ?? 0;
            if (orientation is 5 or 6 or 7 or 8) (width, height) = (height, width);
            string? thumb = null;
            if (raw != null)
            {
                var oriented = ImageTools.Orient(raw, orientation);
                thumb = Path.Combine(AppPaths.ThumbsDir, "img-" + Guid.NewGuid().ToString("N") + ".png");
                ImageTools.SavePng(oriented, thumb);
            }
            var item = new LibraryItem
            {
                Kind = ItemKind.Image,
                Title = Path.GetFileNameWithoutExtension(path),
                FilePath = target,
                ThumbPath = thumb,
                Mime = MimeFor(Path.GetExtension(path)),
                SizeBytes = new FileInfo(target).Length,
                Width = width,
                Height = height,
                FolderId = folderId
            };
            AppServices.Library.InsertItem(item);
            return item;
        }

        public LibraryItem? ImportVideo(string path, long? folderId)
        {
            var target = UniqueDestination(AppPaths.VideosDir, Path.GetFileName(path));
            File.Copy(path, target, false);
            string? thumb = null;
            try
            {
                var image = ShellThumbnail.GetImage(target, 720);
                if (image != null)
                {
                    thumb = Path.Combine(AppPaths.ThumbsDir, "vid-" + Guid.NewGuid().ToString("N") + ".png");
                    ImageTools.SavePng(image, thumb);
                }
            }
            catch (Exception ex) { AppPaths.Log("Videominiatuur mislukt: " + ex.Message); }

            var item = new LibraryItem
            {
                Kind = ItemKind.Video,
                Title = Path.GetFileNameWithoutExtension(path),
                FilePath = target,
                ThumbPath = thumb,
                Mime = MimeFor(Path.GetExtension(path)),
                SizeBytes = new FileInfo(target).Length,
                FolderId = folderId
            };
            AppServices.Library.InsertItem(item);
            return item;
        }

        public LibraryItem? ImportDocument(string path, long? folderId)
        {
            var target = UniqueDestination(AppPaths.DocumentsDir, Path.GetFileName(path));
            File.Copy(path, target, false);
            var item = new LibraryItem
            {
                Kind = ItemKind.Document,
                Title = Path.GetFileNameWithoutExtension(path),
                Content = DocumentTextExtractor.Extract(target),
                FilePath = target,
                Mime = MimeFor(Path.GetExtension(path)),
                SizeBytes = new FileInfo(target).Length,
                FolderId = folderId
            };
            AppServices.Library.InsertItem(item);
            return item;
        }

        public LibraryItem? ImportAudio(string path, long? folderId)
        {
            var target = UniqueDestination(AppPaths.AudioDir, Path.GetFileName(path));
            File.Copy(path, target, false);
            var item = new LibraryItem
            {
                Kind = ItemKind.Audio,
                Title = Path.GetFileNameWithoutExtension(path),
                FilePath = target,
                Mime = MimeFor(Path.GetExtension(path)),
                SizeBytes = new FileInfo(target).Length,
                FolderId = folderId
            };
            AppServices.Library.InsertItem(item);
            return item;
        }

        public static void RegisterExternalFile(string path, ItemKind kind, long? folderId)
        {
            var item = new LibraryItem
            {
                Kind = kind,
                Title = Path.GetFileNameWithoutExtension(path),
                FilePath = path,
                Content = kind == ItemKind.Document ? DocumentTextExtractor.Extract(path) : "",
                SizeBytes = File.Exists(path) ? new FileInfo(path).Length : 0,
                Mime = MimeFor(Path.GetExtension(path)),
                FolderId = folderId
            };
            if (kind == ItemKind.Image)
            {
                var raw = ImageTools.LoadRaw(path, 720);
                if (raw != null)
                {
                    item.Width = raw.PixelWidth;
                    item.Height = raw.PixelHeight;
                    var thumb = Path.Combine(AppPaths.ThumbsDir, "img-" + Guid.NewGuid().ToString("N") + ".png");
                    ImageTools.SavePng(ImageTools.Orient(raw, ImageTools.GetOrientation(raw)), thumb);
                    item.ThumbPath = thumb;
                }
            }
            AppServices.Library.InsertItem(item);
        }

        public static string MimeFor(string? extension)
        {
            return (extension ?? "").ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" or ".jfif" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".tif" or ".tiff" => "image/tiff",
                ".webp" => "image/webp",
                ".ico" => "image/x-icon",
                ".mp4" or ".m4v" => "video/mp4",
                ".mov" => "video/quicktime",
                ".wmv" => "video/x-ms-wmv",
                ".avi" => "video/x-msvideo",
                ".mkv" => "video/x-matroska",
                ".webm" => "video/webm",
                ".mpg" or ".mpeg" => "video/mpeg",
                ".pdf" => "application/pdf",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".ppt" => "application/vnd.ms-powerpoint",
                ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                ".txt" or ".md" => "text/plain",
                ".rtf" => "application/rtf",
                ".csv" => "text/csv",
                ".zip" => "application/zip",
                ".rar" => "application/vnd.rar",
                ".7z" => "application/x-7z-compressed",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".html" or ".htm" => "text/html",
                ".epub" => "application/epub+zip",
                _ => "application/octet-stream"
            };
        }
    }
}
