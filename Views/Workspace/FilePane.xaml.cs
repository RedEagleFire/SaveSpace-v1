using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SaveSpace.Services;

namespace SaveSpace.Views.Workspace
{
    public partial class FilePane : UserControl
    {
        private static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff", ".ico" };
        private static readonly string[] AudioExt = { ".mp3", ".wav", ".m4a", ".ogg", ".flac", ".wma", ".aac" };
        private static readonly string[] VideoExt = { ".mp4", ".mov", ".webm", ".m4v", ".wmv", ".avi", ".mkv" };
        private static readonly string[] OfficeExt = { ".doc", ".docx", ".ppt", ".pptx", ".xls", ".xlsx", ".odt", ".odp", ".ods" };

        private readonly string _path;
        private double _zoom = 1;
        private bool _webReady;
        private bool _mediaReady;
        private bool _playing;
        private bool _updatingSlider;
        private string? _temporaryRenderFile;
        private DispatcherTimer? _timer;

        public FilePane(string path, string title)
        {
            InitializeComponent();
            _path = path;
            TitleText.Text = title;
            TitleText.ToolTip = path;
            ToolTip = path;
            Loaded += (_, _) => Open();
            Unloaded += (_, _) => Dispose();
            SizeChanged += (_, _) => { if (ImageScroll.Visibility == Visibility.Visible) FitImage(); };
        }

        public string FilePath => _path;

        private void Open()
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            if (string.IsNullOrWhiteSpace(_path) || !File.Exists(_path))
            {
                ShowFallback("Bestand niet gevonden",
                    "Dit bestand staat niet meer op de plek waar SaveSpace het verwacht. Het is verplaatst of verwijderd buiten de app.");
                return;
            }

            var ext = Path.GetExtension(_path).ToLowerInvariant();
            if (ext is ".pdf" or ".txt" or ".md" or ".html" or ".htm" or ".log" or ".csv" or ".json" or ".xml" or ".gif")
            {
                KindIcon.Text = ext == ".pdf" ? "\uE8A5" : "\uE8A5";
                ShowWeb();
            }
            else if (ImageExt.Contains(ext))
            {
                KindIcon.Text = "\uEB9F";
                KindIcon.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Image");
                ShowImage();
            }
            else if (AudioExt.Contains(ext) || VideoExt.Contains(ext))
            {
                KindIcon.Text = AudioExt.Contains(ext) ? "\uE8D6" : "\uE714";
                KindIcon.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Video");
                ShowMedia(AudioExt.Contains(ext));
            }
            else if (OfficeExt.Contains(ext))
            {
                KindIcon.Text = "\uE8A5";
                if (ext == ".docx")
                {
                    try
                    {
                        _temporaryRenderFile = DocxHtmlRenderer.RenderToFile(_path);
                        ShowWeb(_temporaryRenderFile);
                    }
                    catch (Exception ex)
                    {
                        ShowFallback("Word-document kan niet worden weergegeven", "Dit DOCX-bestand kon niet worden gelezen. Open het eventueel extern.\n\n" + ex.Message);
                    }
                }
                else if (ext is ".pptx" or ".xlsx")
                {
                    try
                    {
                        _temporaryRenderFile = OfficeXmlRenderer.RenderToFile(_path);
                        ShowWeb(_temporaryRenderFile);
                    }
                    catch (Exception ex)
                    {
                        ShowFallback("Office-bestand kan niet worden weergegeven", "Dit bestand kon niet worden gelezen. Open het eventueel extern.\n\n" + ex.Message);
                    }
                }
                else ShowFallback("Office-bestand",
                    "Dit oudere Office-bestandstype heeft hier nog geen ingebouwde weergave. Je kunt het wel extern openen.");
            }
            else
            {
                ShowFallback("Geen ingebouwde viewer",
                    "Voor dit bestandstype is geen ingebouwde weergave. Je kunt het openen met de standaardapp van Windows.");
            }
        }

        private async void ShowWeb(string? displayPath = null)
        {
            Web.Visibility = Visibility.Visible;
            LoadingPanel.Visibility = Visibility.Visible;
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
                        try { FileActions.OpenUrl(e.Uri); } catch { }
                    };
                    _webReady = true;
                }
                Web.CoreWebView2.Navigate(new Uri(displayPath ?? _path, UriKind.Absolute).AbsoluteUri);
                await System.Threading.Tasks.Task.Delay(700);
                LoadingPanel.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
                Web.Visibility = Visibility.Collapsed;
                ShowFallback("Bestand niet in de app te openen",
                    "De ingebouwde weergave kon niet starten. Installeer de WebView2-runtime of open het bestand extern.\n\n" + ex.Message);
            }
        }

        private void ShowImage()
        {
            ImageScroll.Visibility = Visibility.Visible;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(_path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                Picture.Source = bmp;
                Dispatcher.BeginInvoke(new Action(FitImage), DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                ShowFallback("Afbeelding kan niet worden getoond", ex.Message);
            }
        }

        private void FitImage()
        {
            if (Picture.Source is not BitmapSource bmp || ImageScroll.ViewportWidth <= 0) return;
            var width = ImageScroll.ViewportWidth - 24;
            var height = ImageScroll.ViewportHeight - 24;
            if (width <= 0 || height <= 0) return;
            _zoom = Math.Clamp(Math.Min(width / bmp.PixelWidth, height / bmp.PixelHeight), 0.05, 3);
            ImageScale.ScaleX = _zoom;
            ImageScale.ScaleY = _zoom;
        }

        private void ShowMedia(bool audioOnly)
        {
            MediaPanel.Visibility = Visibility.Visible;
            MediaIcon.Text = audioOnly ? "\uE8D6" : "\uE714";
            MediaTitle.Text = TitleText.Text;
            Player.Source = new Uri(_path, UriKind.Absolute);
            Player.Volume = VolumeSlider.Value;
            Player.Play();
            _playing = true;
            _mediaReady = true;
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

        private static string Format(TimeSpan t) =>
            t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

        private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom + 0.15);
        private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom - 0.15);

        private void SetZoom(double value)
        {
            _zoom = Math.Clamp(value, 0.2, 4);
            if (ImageScroll.Visibility == Visibility.Visible)
            {
                ImageScale.ScaleX = _zoom;
                ImageScale.ScaleY = _zoom;
            }
            else if (_webReady)
            {
                try { Web.ZoomFactor = _zoom; } catch { }
            }
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

        private void Position_DragStarted(object sender, DragStartedEventArgs e) => _updatingSlider = true;
        private void Position_DragCompleted(object sender, DragCompletedEventArgs e)
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

        private void External_Click(object sender, RoutedEventArgs e)
        {
            try { FileActions.OpenFile(_path); }
            catch (Exception ex) { FileActions.ShowError(Window.GetWindow(this), ex.Message); }
        }

        private void ShowFallback(string title, string text)
        {
            FallbackPanel.Visibility = Visibility.Visible;
            FallbackTitle.Text = title;
            FallbackText.Text = text;
        }

        public void Dispose()
        {
            _timer?.Stop();
            _timer = null;
            _mediaReady = false;
            try
            {
                Player.Stop();
                Player.Close();
                Player.Source = null;
            }
            catch { }
            try
            {
                if (_webReady) Web.Dispose();
            }
            catch { }
            _webReady = false;
            try { if (_temporaryRenderFile != null && File.Exists(_temporaryRenderFile)) File.Delete(_temporaryRenderFile); } catch { }
            _temporaryRenderFile = null;
        }
    }
}
