using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SaveSpace.Models;
using SaveSpace.Services;

namespace SaveSpace.Views.Dialogs
{
    public class LinkDraft
    {
        public string Url { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Tags { get; set; } = "";
        public long? FolderId { get; set; }
        public bool IsFavorite { get; set; }
    }

    public partial class LinkEditorDialog : Window
    {
        private readonly LibraryItem? _existing;
        private readonly List<Folder> _folders;
        private long? _folderId;

        public LinkDraft? Result { get; private set; }

        public LinkEditorDialog(LibraryItem? existing)
        {
            InitializeComponent();
            _existing = existing;
            _folders = AppServices.Library.GetFolders();

            if (existing != null)
            {
                HeaderTitle.Text = "Weblink bewerken";
                UrlBox.Text = existing.Url ?? "";
                TitleBox.Text = existing.Title;
                DescriptionBox.Text = existing.Content;
                TagsBox.Text = existing.Tags;
                _folderId = existing.FolderId;
                FavoriteToggle.IsChecked = existing.IsFavorite;
                FolderLabel.Text = string.IsNullOrWhiteSpace(existing.FolderName) ? "Geen map" : existing.FolderName;
                UpdateDomainPreview();
            }
            else
            {
                FolderLabel.Text = "Geen map";
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }

        private void UrlBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateDomainPreview();

        private void UpdateDomainPreview()
        {
            var raw = UrlBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                DomainPreview.Text = "Alleen http- en https-adressen worden geopend.";
                return;
            }
            if (!raw.Contains("://", StringComparison.Ordinal)) raw = "https://" + raw;
            if (Uri.TryCreate(raw, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                DomainPreview.Text = uri.Host.Replace("www.", "") + (uri.AbsolutePath == "/" ? "" : uri.AbsolutePath);
            else
                DomainPreview.Text = "Dit lijkt geen geldig webadres.";
        }

        private void UrlBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(TitleBox.Text)) return;
            var raw = UrlBox.Text.Trim();
            if (raw.Length == 0) return;
            if (!raw.Contains("://", StringComparison.Ordinal)) raw = "https://" + raw;
            if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
                TitleBox.Text = SuggestTitle(uri);
        }

        private static string SuggestTitle(Uri uri)
        {
            var segment = uri.Segments.LastOrDefault()?.Trim('/');
            if (!string.IsNullOrWhiteSpace(segment))
            {
                var name = Uri.UnescapeDataString(segment);
                name = System.IO.Path.GetFileNameWithoutExtension(name).Replace('-', ' ').Replace('_', ' ').Trim();
                if (name.Length > 2) return char.ToUpperInvariant(name[0]) + name[1..];
            }
            return uri.Host.Replace("www.", "");
        }

        private static bool TryNormalizeUrl(string raw, out string normalized, out string error)
        {
            normalized = "";
            error = "";
            raw = raw.Trim();
            if (raw.Length == 0)
            {
                error = "Vul een webadres in.";
                return false;
            }
            if (!raw.Contains("://", StringComparison.Ordinal)) raw = "https://" + raw;
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            {
                error = "Dit is geen geldig webadres. Controleer het adres en probeer opnieuw.";
                return false;
            }
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                error = "Alleen http- en https-links zijn toegestaan.";
                return false;
            }
            normalized = uri.AbsoluteUri;
            return true;
        }

        private void Folder_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPickerWindow(_folders, _folderId) { Owner = this };
            if (picker.ShowDialog() != true) return;
            _folderId = picker.SelectedFolderId;
            FolderLabel.Text = _folderId.HasValue
                ? _folders.FirstOrDefault(f => f.Id == _folderId.Value)?.Name ?? "Geen map"
                : "Geen map";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!TryNormalizeUrl(UrlBox.Text, out var url, out var error))
            {
                ErrorText.Text = error;
                ErrorText.Visibility = Visibility.Visible;
                UrlBox.Focus();
                return;
            }

            var title = TitleBox.Text.Trim();
            if (title.Length == 0 && Uri.TryCreate(url, UriKind.Absolute, out var uri))
                title = SuggestTitle(uri);

            Result = new LinkDraft
            {
                Url = url,
                Title = title,
                Description = DescriptionBox.Text.Trim(),
                Tags = TagsBox.Text.Trim(),
                FolderId = _folderId,
                IsFavorite = FavoriteToggle.IsChecked == true
            };
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
