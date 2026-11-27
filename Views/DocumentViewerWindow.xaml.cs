using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using SaveSpace.Models;
using SaveSpace.Services;

namespace SaveSpace.Views
{
    public partial class DocumentViewerWindow : Window
    {
        private static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".tif", ".tiff", ".ico" };
        private static readonly string[] AudioExt = { ".mp3", ".wav", ".m4a", ".ogg", ".flac", ".wma", ".aac" };
        private static readonly string[] VideoExt = { ".mp4", ".mov", ".webm", ".m4v", ".wmv", ".avi", ".mkv" };
        private static readonly string[] WebExt = { ".pdf", ".txt", ".md", ".html", ".htm", ".log", ".csv", ".json", ".xml", ".rtf" };

        private readonly List<LibraryItem> _context = new();
        private LibraryItem _item;
        private int _index;
        private double _zoom = 1;
        private bool _isFullscreen;
        private bool _updatingSlider;
        private bool _mediaReady;
        private bool _webReady;
        private bool _playing;
        private string? _temporaryRenderFile;
        private DispatcherTimer? _timer;

        public DocumentViewerWindow(LibraryItem item, IList<LibraryItem>? context = null)
        {
            InitializeComponent();
            _item = item;
            _context = (context ?? new List<LibraryItem> { item })
                .Where(i => i.Kind == ItemKind.Document)
                .ToList();
            if (_context.Count == 0) _context.Add(item);
            _index = Math.Max(0, _context.FindIndex(i => i.Id == item.Id));
            Loaded += (_, _) => LoadItem(item);
            Closed += (_, _) =>
            {
                StopMedia();
                try { if (_webReady) Web.Dispose(); } catch { }
                try { if (_temporaryRenderFile != null && File.Exists(_temporaryRenderFile)) File.Delete(_temporaryRenderFile); } catch { }
            };
            SizeChanged += (_, _) => { if (ImagePanel.Visibility == Visibility.Visible) FitImage(false); };
        }

        private void LoadItem(LibraryItem item)
        {
            _item = item;
            _zoom = 1;
            ZoomText.Text = "100%";
            MessageBar.Visibility = Visibility.Collapsed;
            WebPanel.Visibility = ImagePanel.Visibility = MediaPanel.Visibility = FallbackPanel.Visibility = Visibility.Collapsed;
            StopMedia();
            RotateButton.Visibility = Visibility.Collapsed;

            Title = item.Title + " — SaveSpace";
            TitleText.Text = item.Title;
            MetaText.Text = BuildMeta(item);
            KindIcon.Text = item.Icon;
            KindIcon.Foreground = item.AccentBrush;
            KindBadge.Background = item.AccentBrush is SolidColorBrush solid
                ? new SolidColorBrush(solid.Color) { Opacity = 0.16 }
                : (Brush)FindResource("Brush.DocumentSoft");

            var path = item.FilePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                ShowFallback("Bestand niet gevonden",
                    "SaveSpace kan dit bestand niet openen. Het is verplaatst of verwijderd buiten de app.",
                    path ?? "");
                UpdateNav();
                return;
            }

            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".pdf" || ext == ".txt" || ext == ".md" || ext == ".html" || ext == ".htm" ||
                ext == ".log" || ext == ".csv" || ext == ".json" || ext == ".xml")
                ShowWeb(path);
            else if (ext == ".docx")
            {
                try
                {
                    _temporaryRenderFile = DocxHtmlRenderer.RenderToFile(path);
                    ShowWeb(_temporaryRenderFile);
                }
                catch (Exception ex)
                {
                    ShowFallback("Word-document kan niet worden weergegeven", "Dit DOCX-bestand kon niet worden gelezen. Je kunt het nog steeds extern openen.\n\n" + ex.Message, path);
                }
            }
            else if (ext is ".pptx" or ".xlsx")
            {
                try
                {
                    _temporaryRenderFile = OfficeXmlRenderer.RenderToFile(path);
                    ShowWeb(_temporaryRenderFile);
                }
                catch (Exception ex)
                {
                    ShowFallback("Office-bestand kan niet worden weergegeven", "Dit bestand kon niet worden gelezen. Je kunt het nog steeds extern openen.\n\n" + ex.Message, path);
                }
            }
            else if (ImageExt.Contains(ext))
                ShowImage(path);
            else if (AudioExt.Contains(ext) || VideoExt.Contains(ext))
                ShowMedia(path, AudioExt.Contains(ext));
            else
                ShowFallback(ext is ".doc" or ".docx" or ".ppt" or ".pptx" or ".xls" or ".xlsx"
                        ? "Office-bestand"
                        : "Geen ingebouwde viewer",
                    ext is ".doc" or ".ppt" or ".xls"
                        ? "Dit oudere Office-bestandstype heeft geen ingebouwde weergave. Open het veilig in een Office-app."
                        : "Voor dit bestandstype is geen ingebouwde viewer. Je kunt het openen met de standaardapp van Windows. Het origineel blijft ongewijzigd.",
                    path);

            UpdateNav();
        }

        private static string BuildMeta(LibraryItem item)
        {
            var ext = Path.GetExtension(item.FilePath ?? "").TrimStart('.').ToUpperInvariant();
            var kind = string.IsNullOrEmpty(ext) ? "Document" : ext + "-document";
            return item.SizeBytes > 0 ? $"{kind} · {LibraryItem.FormatSize(item.SizeBytes)}" : kind;
        }

        private async void ShowWeb(string path)
        {
            WebPanel.Visibility = Visibility.Visible;
            WebLoading.Visibility = Visibility.Visible;
            try
            {
                if (!_webReady)
                {
                    var env = await WebRuntime.GetAsync();
                    await Web.EnsureCoreWebView2Async(env);
                    Web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    Web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    Web.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    Web.CoreWebView2.NewWindowRequested += (_, e) =>
                    {
                        e.Handled = true;
                        TryOpenExternal(e.Uri);
                    };
                    Web.CoreWebView2.NavigationStarting += (_, e) =>
                    {
                        if (!e.Uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase) &&
                            !e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase) &&
                            !e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                        {
                            e.Cancel = true;
                            TryOpenExternal(e.Uri);
                        }
                    };
                    _webReady = true;
                }
                Web.CoreWebView2.Navigate(new Uri(path).AbsoluteUri);
                _ = HideLoadingSoonAsync();
            }
            catch (Exception ex)
            {
                WebPanel.Visibility = Visibility.Collapsed;
                ShowFallback("WebView2 is niet beschikbaar",
                    "De ingebouwde documentweergave kon niet starten. Installeer de Microsoft Edge WebView2 Runtime of open het bestand extern.\n\n" + ex.Message,
                    path);
            }
        }

        private async System.Threading.Tasks.Task HideLoadingSoonAsync()
        {
            await System.Threading.Tasks.Task.Delay(1200);
            WebLoading.Visibility = Visibility.Collapsed;
        }

        private void ShowImage(string path)
        {
            ImagePanel.Visibility = Visibility.Visible;
            RotateButton.Visibility = Visibility.Visible;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                Picture.Source = bmp;
                ImageRotate.Angle = 0;
                Dispatcher.BeginInvoke(new Action(() => FitImage(false)), DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                ShowFallback("Afbeelding kan niet worden getoond", ex.Message, path);
            }
        }

        private void ShowMedia(string path, bool audioOnly)
        {
            MediaPanel.Visibility = Visibility.Visible;
            AudioCover.Visibility = audioOnly ? Visibility.Visible : Visibility.Collapsed;
            AudioTitle.Text = _item.Title;
            AudioHint.Text = audioOnly ? "Audio lokaal afspelen" : "";
            Player.Source = new Uri(path, UriKind.Absolute);
            Player.Volume = VolumeSlider.Value;
            _mediaReady = true;
            Player.Play();
            _playing = true;
            PlayButton.Content = "\uE769";
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += (_, _) =>
            {
                if (_updatingSlider || !Player.NaturalDuration.HasTimeSpan) return;
                PositionSlider.Maximum = Player.NaturalDuration.TimeSpan.TotalSeconds;
                PositionSlider.Value = Player.Position.TotalSeconds;
                TimeText.Text = $"{Format(Player.Position)} / {Format(Player.NaturalDuration.TimeSpan)}";
            };
            _timer.Start();
        }

        private void StopMedia()
        {
            _mediaReady = false;
            _playing = false;
            _timer?.Stop();
            _timer = null;
            try
            {
                Player.Stop();
                Player.Close();
                Player.Source = null;
            }
            catch { }
        }

        private void ShowFallback(string title, string text, string path)
        {
            FallbackPanel.Visibility = Visibility.Visible;
            FallbackTitle.Text = title;
            FallbackText.Text = text;
            FallbackPath.Text = path;
            FallbackIcon.Text = _item.Icon;
        }

        private void UpdateNav()
        {
            PrevButton.IsEnabled = _index > 0;
            NextButton.IsEnabled = _index < _context.Count - 1;
            PrevButton.Opacity = PrevButton.IsEnabled ? 1 : 0.3;
            NextButton.Opacity = NextButton.IsEnabled ? 1 : 0.3;
        }

        private void Prev_Click(object sender, RoutedEventArgs e)
        {
            if (_index <= 0) return;
            _index--;
            LoadItem(_context[_index]);
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_index >= _context.Count - 1) return;
            _index++;
            LoadItem(_context[_index]);
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom + 0.15);
        private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom - 0.15);

        private void SetZoom(double value)
        {
            _zoom = Math.Clamp(value, 0.25, 4);
            ZoomText.Text = $"{_zoom * 100:0}%";
            if (WebPanel.Visibility == Visibility.Visible && _webReady)
                Web.ZoomFactor = _zoom;
            if (ImagePanel.Visibility == Visibility.Visible)
            {
                ImageScale.ScaleX = _zoom;
                ImageScale.ScaleY = _zoom;
            }
        }

        private void Fit_Click(object sender, RoutedEventArgs e)
        {
            if (ImagePanel.Visibility == Visibility.Visible)
                FitImage(true);
            else
                SetZoom(1);
        }

        private void FitImage(bool updateZoom)
        {
            if (Picture.Source is not BitmapSource bmp || ImageScroll.ViewportWidth <= 0) return;
            var available = ImageScroll.ViewportWidth - 60;
            var height = ImageScroll.ViewportHeight - 60;
            if (available <= 0 || height <= 0) return;
            var scale = Math.Min(available / bmp.PixelWidth, height / bmp.PixelHeight);
            scale = Math.Clamp(scale, 0.05, 3);
            if (updateZoom) _zoom = scale;
            else _zoom = 1;
            var fit = updateZoom ? scale : 1;
            ImageScale.ScaleX = fit;
            ImageScale.ScaleY = fit;
            ZoomText.Text = $"{fit * 100:0}%";
        }

        private void Rotate_Click(object sender, RoutedEventArgs e)
        {
            ImageRotate.Angle = (ImageRotate.Angle + 90) % 360;
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            if (!_mediaReady) return;
            if (_playing)
            {
                Player.Pause();
                _playing = false;
                PlayButton.Content = "\uE768";
            }
            else
            {
                Player.Play();
                _playing = true;
                PlayButton.Content = "\uE769";
            }
        }

        private void PositionSlider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e) => _updatingSlider = true;
        private void PositionSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            _updatingSlider = false;
            if (_mediaReady) Player.Position = TimeSpan.FromSeconds(PositionSlider.Value);
        }

        private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingSlider && _mediaReady)
                Player.Position = TimeSpan.FromSeconds(PositionSlider.Value);
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Player != null) Player.Volume = VolumeSlider.Value;
        }

        private void Player_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (!Player.NaturalDuration.HasTimeSpan) return;
            PositionSlider.Maximum = Player.NaturalDuration.TimeSpan.TotalSeconds;
            TimeText.Text = $"0:00 / {Format(Player.NaturalDuration.TimeSpan)}";
        }

        private void Player_MediaEnded(object sender, RoutedEventArgs e)
        {
            _playing = false;
            PlayButton.Content = "\uE768";
            Player.Position = TimeSpan.Zero;
        }

        private static string Format(TimeSpan t) =>
            t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

        private void OpenExternal_Click(object sender, RoutedEventArgs e) => TryOpenExternal(_item.FilePath);

        private void TryOpenExternal(string? pathOrUrl)
        {
            if (string.IsNullOrWhiteSpace(pathOrUrl)) return;
            try
            {
                if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uri) && uri.Scheme.StartsWith("http"))
                    FileActions.OpenUrl(pathOrUrl);
                else
                    FileActions.OpenFile(pathOrUrl);
            }
            catch (Exception ex)
            {
                FileActions.ShowError(this, ex.Message);
            }
        }

        private void ShowInFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_item.FilePath)) return;
                SaveSpace.Services.FileActions.RevealInExplorer(_item.FilePath);
            }
            catch (Exception ex)
            {
                FileActions.ShowError(this, ex.Message);
            }
        }

        private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

        private void ToggleFullscreen()
        {
            _isFullscreen = !_isFullscreen;
            Shell.Margin = _isFullscreen ? new Thickness(0) : new Thickness(14);
            TitleBar.Visibility = _isFullscreen ? Visibility.Collapsed : Visibility.Visible;
            Footer.Visibility = _isFullscreen ? Visibility.Collapsed : Visibility.Visible;
            FooterRow.Height = _isFullscreen ? new GridLength(0) : new GridLength(42);
            WindowState = _isFullscreen ? WindowState.Maximized : WindowState.Normal;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                return;
            }
            try { DragMove(); } catch { }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (_isFullscreen) ToggleFullscreen();
                else Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.None) Prev_Click(sender, e);
            else if (e.Key == Key.Right && Keyboard.Modifiers == ModifierKeys.None) Next_Click(sender, e);
            else if (e.Key == Key.Space && MediaPanel.Visibility == Visibility.Visible)
            {
                Play_Click(sender, e);
                e.Handled = true;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
