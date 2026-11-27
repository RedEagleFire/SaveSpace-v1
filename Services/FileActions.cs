using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace SaveSpace.Services
{
    public static class FileActions
    {
        /// <summary>Bestandstypen die code kunnen uitvoeren; die openen we alleen na bevestiging.</summary>
        private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".bat", ".cmd", ".com", ".scr", ".msi", ".ps1", ".vbs", ".js", ".jar", ".lnk"
        };

        public static void OpenFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Dit item heeft geen bestandspad.");
            if (!File.Exists(path)) throw new FileNotFoundException("Het bestand is niet gevonden. Het kan verplaatst of verwijderd zijn.", path);

            if (ExecutableExtensions.Contains(Path.GetExtension(path)))
            {
                var answer = MessageBox.Show(
                    $"Dit is een programmabestand ({Path.GetFileName(path)}). Programma's kunnen je computer beïnvloeden.\n\nWeet je zeker dat je het wilt openen?",
                    "SaveSpace", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes) return;
            }
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }

        public static void OpenUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("Deze weblink heeft geen adres.");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("Alleen veilige http- en https-links kunnen worden geopend.");
            Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
        }

        /// <summary>Opent Verkenner met het bestand geselecteerd. Pad wordt als los argument doorgegeven, dus quotes in namen breken niets.</summary>
        public static void RevealInExplorer(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string? folder = null;
            var psi = new ProcessStartInfo { FileName = "explorer.exe", UseShellExecute = true };
            if (File.Exists(path))
            {
                psi.ArgumentList.Add("/select," + path);
            }
            else if (Directory.Exists(path))
            {
                folder = path;
            }
            else
            {
                folder = Path.GetDirectoryName(path);
                if (folder == null || !Directory.Exists(folder))
                    throw new DirectoryNotFoundException("De map bestaat niet meer.");
            }
            if (folder != null) psi.ArgumentList.Add(folder);
            Process.Start(psi);
        }

        public static void OpenFolder(string path)
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }

        public static string? PickFolder(Window? owner, string title = "Kies een map")
        {
            var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
            var result = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
            return result == true ? dialog.FolderName : null;
        }

        public static void ShowError(Window? owner, string message, string title = "SaveSpace")
        {
            if (owner != null) MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
            else MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
