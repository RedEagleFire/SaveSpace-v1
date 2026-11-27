using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SaveSpace.Models;
using SaveSpace.Services;
using SaveSpace.Views.Dialogs;

namespace SaveSpace
{
    public partial class VideoPlayerWindow : Window
    {
        private readonly LibraryItem _item;
        private readonly DispatcherTimer _timer;
        private bool _updating;
        private bool _isPlaying;
        private bool _isFullscreen;
        private WindowState _previousState = WindowState.Normal;
        private bool _failed;

        public VideoPlayerWindow(LibraryItem item)
        {
            InitializeComponent();
            _item = item;
            TitleText.Text = item.Title;
            Title = item.Title + " — SaveSpace";
            SubtitleText.Text = $"{item.MetaLine} · {item.SizeLabel} · {item.DateLabel}";
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _timer.Tick += Timer_Tick;
            Loaded += Window_Loaded;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_item.FilePath) || !File.Exists(_item.FilePath))
            {
                ShowError("Het videobestand is niet gevonden. Het kan verplaatst of verwijderd zijn.\n\n" + _item.FilePath);
                return;
            }
            try
            {
                Player.Source = new Uri(_item.FilePath, UriKind.Absolute);
                Player.Volume = VolumeSlider.Value;
                Player.Play();
                _isPlaying = true;
                PlayPauseButton.Content = "\uE769";
                CenterPlayButton.Visibility = Visibility.Collapsed;
                _timer.Start();
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void Player_MediaOpened(object sender, RoutedEventArgs e)
        {
            ErrorPanel.Visibility = Visibility.Collapsed;
            _failed = false;
            if (Player.NaturalDuration.HasTimeSpan)
            {
                _updating = true;
                Timeline.Maximum = Player.NaturalDuration.TimeSpan.TotalSeconds;
                TotalTime.Text = Format(Player.NaturalDuration.TimeSpan);
                _updating = false;
            }
            Player.Play();
            _isPlaying = true;
            PlayPauseButton.Content = "\uE769";
            CenterPlayButton.Visibility = Visibility.Collapsed;
        }

        private void Player_MediaEnded(object sender, RoutedEventArgs e)
        {
            _isPlaying = false;
            PlayPauseButton.Content = "\uE768";
            CenterPlayButton.Visibility = Visibility.Visible;
            _timer.Stop();
            Player.Position = TimeSpan.Zero;
            Timeline.Value = 0;
        }

        private void Player_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            ShowError("De ingebouwde speler ondersteunt dit bestandsformaat of deze codec niet.\n\n" +
                      "Je kunt de video openen met het programma dat op deze computer is geïnstalleerd. Je originele bestand is niet aangepast.");
        }

        private void ShowError(string message)
        {
            _failed = true;
            _timer.Stop();
            ErrorPanel.Visibility = Visibility.Visible;
            ErrorText.Text = message;
            CenterPlayButton.Visibility = Visibility.Collapsed;
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (_failed || !Player.NaturalDuration.HasTimeSpan) return;
            if (!_updating)
            {
                Timeline.Value = Math.Clamp(Player.Position.TotalSeconds, 0, Timeline.Maximum);
                CurrentTime.Text = Format(Player.Position);
            }
        }

        private static string Format(TimeSpan t) =>
            t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_failed) return;
            if (_isPlaying)
            {
                Player.Pause();
                _isPlaying = false;
                PlayPauseButton.Content = "\uE768";
                CenterPlayButton.Visibility = Visibility.Visible;
            }
            else
            {
                Player.Play();
                _isPlaying = true;
                PlayPauseButton.Content = "\uE769";
                CenterPlayButton.Visibility = Visibility.Collapsed;
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e) => Seek(-10);
        private void Forward_Click(object sender, RoutedEventArgs e) => Seek(10);

        private void Seek(double seconds)
        {
            if (_failed || !Player.NaturalDuration.HasTimeSpan) return;
            var target = Player.Position.TotalSeconds + seconds;
            target = Math.Clamp(target, 0, Player.NaturalDuration.TimeSpan.TotalSeconds);
            Player.Position = TimeSpan.FromSeconds(target);
            Timeline.Value = target;
        }

        private void Timeline_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updating || _failed || !Player.NaturalDuration.HasTimeSpan) return;
            Player.Position = TimeSpan.FromSeconds(Math.Clamp(e.NewValue, 0, Player.NaturalDuration.TimeSpan.TotalSeconds));
            CurrentTime.Text = Format(Player.Position);
        }

        private void Timeline_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _updating = true;

        private void Timeline_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_failed && Player.NaturalDuration.HasTimeSpan)
                Player.Position = TimeSpan.FromSeconds(Math.Clamp(Timeline.Value, 0, Player.NaturalDuration.TimeSpan.TotalSeconds));
            _updating = false;
        }

        private void Volume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Player == null) return;
            Player.Volume = e.NewValue;
            if (MuteButton != null)
                MuteButton.Content = e.NewValue <= 0.001 ? "\uE74F" : e.NewValue < 0.5 ? "\uE993" : "\uE767";
        }

        private void Mute_Click(object sender, RoutedEventArgs e)
        {
            if (Player.Volume > 0)
            {
                Player.Volume = 0;
                VolumeSlider.Value = 0;
                MuteButton.Content = "\uE74F";
            }
            else
            {
                Player.Volume = 0.8;
                VolumeSlider.Value = 0.8;
                MuteButton.Content = "\uE767";
            }
        }

        private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

        private void ToggleFullscreen()
        {
            _isFullscreen = !_isFullscreen;
            if (_isFullscreen)
            {
                _previousState = WindowState;
                TopBar.Visibility = Visibility.Collapsed;
                BottomBar.Visibility = Visibility.Collapsed;
                TopRow.Height = new GridLength(0);
                BottomRow.Height = new GridLength(0);
                WindowState = WindowState.Maximized;
                FullscreenButton.Content = "\uE73F";
            }
            else
            {
                TopBar.Visibility = Visibility.Visible;
                BottomBar.Visibility = Visibility.Visible;
                TopRow.Height = GridLength.Auto;
                BottomRow.Height = GridLength.Auto;
                WindowState = _previousState;
                FullscreenButton.Content = "\uE740";
            }
        }

        private void OpenExternal_Click(object sender, RoutedEventArgs e)
        {
            try { FileActions.OpenFile(_item.FilePath); }
            catch (Exception ex) { FileActions.ShowError(this, ex.Message); }
        }

        private void Reveal_Click(object sender, RoutedEventArgs e)
        {
            try { FileActions.RevealInExplorer(_item.FilePath); }
            catch (Exception ex) { FileActions.ShowError(this, ex.Message); }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmDialog.Show(this, "Video naar prullenbak?",
                    $"‘{_item.Title}’ wordt naar de prullenbak verplaatst. Je kunt de video daar later nog herstellen.",
                    "Naar prullenbak", true, "\uE74D")) return;
            Player.Stop();
            AppServices.Library.SoftDelete(_item.Id);
            AppServices.Notifications.Add("Video naar prullenbak", _item.Title, "\uE74D", "Brush.Danger");
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleFullscreen();
                return;
            }
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Space:
                    PlayPause_Click(sender, e);
                    e.Handled = true;
                    break;
                case Key.Left:
                    Seek(-5);
                    e.Handled = true;
                    break;
                case Key.Right:
                    Seek(5);
                    e.Handled = true;
                    break;
                case Key.Up:
                    VolumeSlider.Value = Math.Min(1, VolumeSlider.Value + 0.05);
                    e.Handled = true;
                    break;
                case Key.Down:
                    VolumeSlider.Value = Math.Max(0, VolumeSlider.Value - 0.05);
                    e.Handled = true;
                    break;
                case Key.F:
                    ToggleFullscreen();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    if (_isFullscreen) ToggleFullscreen();
                    else Close();
                    e.Handled = true;
                    break;
                case Key.Delete:
                    Delete_Click(sender, e);
                    e.Handled = true;
                    break;
            }
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            _timer.Stop();
            try { Player.Stop(); Player.Source = null; } catch { }
            if (Owner is MainWindow main) main.RefreshAfterChange();
        }
    }
}
