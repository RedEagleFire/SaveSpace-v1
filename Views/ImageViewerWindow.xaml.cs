using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SaveSpace.Models;
using SaveSpace.Services;
using SaveSpace.Views.Dialogs;

namespace SaveSpace
{
    public partial class ImageViewerWindow : Window
    {
        private readonly List<LibraryItem> _items;
        private LibraryItem _item;
        private BitmapSource? _source;
        private double _zoom = 1;
        private int _rotation;
        private bool _dragging;
        private Point _dragStart;
        private double _startH;
        private double _startV;

        public ImageViewerWindow(LibraryItem item, IList<LibraryItem> context)
        {
            InitializeComponent();
            _items = context.Where(i => i.Kind == ItemKind.Image).ToList();
            if (!_items.Any(i => i.Id == item.Id)) _items.Insert(0, item);
            _item = item;
            Loaded += (_, _) => { ShowItem(); Dispatcher.BeginInvoke(new Action(FitToWindow)); };
        }

        private void ShowItem()
        {
            _rotation = 0;
            _zoom = 1;
            _source = null;
            TitleText.Text = _item.Title;
            Title = _item.Title + " — SaveSpace";
            FavoriteGlyph.Text = _item.IsFavorite ? "\uE735" : "\uE734";
            FavoriteLabel.Text = _item.IsFavorite ? "Favoriet" : "Favoriet";
            var index = _items.FindIndex(i => i.Id == _item.Id);
            CounterText.Text = $"{index + 1} van {_items.Count} · {_item.KindLabel}";
            PrevButton.Visibility = _items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            NextButton.Visibility = _items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            InfoText.Text = $"{_item.MetaLine} · {_item.SizeLabel} · {_item.DateLabel}";

            if (string.IsNullOrWhiteSpace(_item.FilePath) || !File.Exists(_item.FilePath))
            {
                Photo.Source = null;
                ErrorPanel.Visibility = Visibility.Visible;
                ErrorText.Text = "Het bestand is niet gevonden. Het kan verplaatst of verwijderd zijn.\n\n" + _item.FilePath;
                return;
            }

            try
            {
                var raw = ImageTools.LoadRaw(_item.FilePath);
                if (raw == null)
                {
                    ErrorPanel.Visibility = Visibility.Visible;
                    ErrorText.Text = "Dit bestandsformaat wordt niet ondersteund door de ingebouwde viewer.\n\n" + _item.FilePath;
                    Photo.Source = null;
                    return;
                }
                _source = ImageTools.Orient(raw, ImageTools.GetOrientation(raw));
                Photo.Source = _source;
                Photo.Width = _source.PixelWidth;
                Photo.Height = _source.PixelHeight;
                ErrorPanel.Visibility = Visibility.Collapsed;
                ApplyTransform();
                Dispatcher.BeginInvoke(new Action(FitToWindow));
            }
            catch (Exception ex)
            {
                ErrorPanel.Visibility = Visibility.Visible;
                ErrorText.Text = ex.Message;
                Photo.Source = null;
            }
        }

        private void ApplyTransform()
        {
            var group = new TransformGroup();
            group.Children.Add(new ScaleTransform(_zoom, _zoom));
            group.Children.Add(new RotateTransform(_rotation));
            Photo.LayoutTransform = group;
            ZoomText.Text = $"{_zoom * 100:0}%";
        }

        private void FitToWindow()
        {
            if (_source == null) return;
            var vw = ImageScroll.ViewportWidth - 92;
            var vh = ImageScroll.ViewportHeight - 92;
            if (vw <= 0 || vh <= 0) return;
            var rotated = _rotation % 180 != 0;
            var w = rotated ? _source.PixelHeight : _source.PixelWidth;
            var h = rotated ? _source.PixelWidth : _source.PixelHeight;
            _zoom = Math.Min(vw / w, vh / h);
            if (_zoom > 1) _zoom = 1;
            _zoom = Math.Clamp(_zoom, 0.02, 8);
            ApplyTransform();
        }

        private void SetZoom(double value)
        {
            _zoom = Math.Clamp(value, 0.03, 8);
            ApplyTransform();
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom * 1.2);
        private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom / 1.2);
        private void Fit_Click(object sender, RoutedEventArgs e) => FitToWindow();

        private void RotateLeft_Click(object sender, RoutedEventArgs e) => Rotate(-90);
        private void RotateRight_Click(object sender, RoutedEventArgs e) => Rotate(90);

        private void Rotate(int delta)
        {
            if (_source == null) return;
            _rotation = (_rotation + delta + 360) % 360;
            ApplyTransform();
            Dispatcher.BeginInvoke(new Action(FitToWindow));
        }

        private void Photo_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_source == null) return;
            SetZoom(e.Delta > 0 ? _zoom * 1.14 : _zoom / 1.14);
            e.Handled = true;
        }

        private void Photo_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_source == null) return;
            _dragging = true;
            _dragStart = e.GetPosition(this);
            _startH = ImageScroll.HorizontalOffset;
            _startV = ImageScroll.VerticalOffset;
            Photo.CaptureMouse();
            Photo.Cursor = Cursors.SizeAll;
            e.Handled = true;
        }

        private void Photo_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var p = e.GetPosition(this);
            ImageScroll.ScrollToHorizontalOffset(_startH - (p.X - _dragStart.X));
            ImageScroll.ScrollToVerticalOffset(_startV - (p.Y - _dragStart.Y));
        }

        private void Photo_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _dragging = false;
            Photo.ReleaseMouseCapture();
            Photo.Cursor = Cursors.Hand;
            e.Handled = true;
        }

        private void Prev_Click(object sender, RoutedEventArgs e) => Navigate(-1);
        private void Next_Click(object sender, RoutedEventArgs e) => Navigate(1);

        private void Navigate(int delta)
        {
            if (_items.Count < 2) return;
            var index = _items.FindIndex(i => i.Id == _item.Id);
            index = (index + delta + _items.Count) % _items.Count;
            _item = _items[index];
            ShowItem();
        }

        private void Favorite_Click(object sender, RoutedEventArgs e) => ToggleFavorite();

        private void ToggleFavorite()
        {
            _item.IsFavorite = !_item.IsFavorite;
            AppServices.Library.SetFavorite(_item.Id, _item.IsFavorite);
            _item.Raise(nameof(LibraryItem.IsFavorite));
            _item.Raise(nameof(LibraryItem.FavoriteGlyph));
            FavoriteGlyph.Text = _item.IsFavorite ? "\uE735" : "\uE734";
            if (Owner is MainWindow main) main.RefreshHeader();
        }

        private void Annotate_Click(object sender, RoutedEventArgs e)
        {
            if (_source == null) return;
            var dialog = new DrawingWindow(_source) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.PngBytes == null) return;
            string? temporary = null;
            try
            {
                var cache = Path.Combine(AppPaths.Root, "Cache");
                Directory.CreateDirectory(cache);
                temporary = Path.Combine(cache, "marked-" + Guid.NewGuid().ToString("N") + ".png");
                File.WriteAllBytes(temporary, dialog.PngBytes);
                var marked = new MediaImportService().ImportImage(temporary, _item.FolderId);
                if (marked == null) throw new InvalidOperationException("De gemarkeerde afbeelding kon niet worden bewaard.");
                var index = _items.FindIndex(i => i.Id == _item.Id);
                _items.Insert(index < 0 ? 0 : index + 1, marked);
                AppServices.Notifications.Add("Gemarkeerde kopie opgeslagen", marked.Title, "\uE790", "Brush.Image");
                CounterText.Text = $"Nieuwe kopie · {_item.KindLabel}";
            }
            catch (Exception ex) { FileActions.ShowError(this, "De markering kon niet worden opgeslagen.\n\n" + ex.Message); }
            finally { try { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); } catch { } }
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
            if (!ConfirmDialog.Show(this, "Foto naar prullenbak?",
                    $"‘{_item.Title}’ wordt naar de prullenbak verplaatst. Je kunt de foto daar later nog herstellen.",
                    "Naar prullenbak", true, "\uE74D")) return;

            var index = _items.FindIndex(i => i.Id == _item.Id);
            AppServices.Library.SoftDelete(_item.Id);
            AppServices.Notifications.Add("Foto naar prullenbak", _item.Title, "\uE74D", "Brush.Danger");
            _items.RemoveAll(i => i.Id == _item.Id);
            if (Owner is MainWindow main) main.RefreshHeader();

            if (_items.Count == 0)
            {
                Close();
                return;
            }
            if (index >= _items.Count) index = 0;
            _item = _items[index];
            ShowItem();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                Dispatcher.BeginInvoke(new Action(FitToWindow));
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
                case Key.Escape:
                    Close();
                    e.Handled = true;
                    break;
                case Key.Left:
                    Navigate(-1);
                    e.Handled = true;
                    break;
                case Key.Right:
                    Navigate(1);
                    e.Handled = true;
                    break;
                case Key.Add:
                case Key.OemPlus:
                    SetZoom(_zoom * 1.2);
                    e.Handled = true;
                    break;
                case Key.Subtract:
                case Key.OemMinus:
                    SetZoom(_zoom / 1.2);
                    e.Handled = true;
                    break;
                case Key.D0:
                case Key.NumPad0:
                    FitToWindow();
                    e.Handled = true;
                    break;
                case Key.R:
                    Rotate(90);
                    e.Handled = true;
                    break;
                case Key.F:
                    ToggleFavorite();
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
            if (Owner is MainWindow main) main.RefreshAfterChange();
        }
    }
}
