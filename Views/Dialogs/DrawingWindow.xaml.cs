using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SaveSpace.Views.Dialogs
{
    public partial class DrawingWindow : Window
    {
        public byte[]? PngBytes { get; private set; }

        public DrawingWindow(BitmapSource? backgroundImage = null)
        {
            InitializeComponent();
            if (backgroundImage != null)
            {
                Canvas.Background = new ImageBrush(backgroundImage) { Stretch = Stretch.Uniform };
                Title = "Afbeelding markeren";
            }
            Canvas.DefaultDrawingAttributes = new DrawingAttributes
            {
                Color = Color.FromRgb(242, 29, 47),
                Width = 3.2,
                Height = 3.2,
                FitToCurve = true,
                IgnorePressure = true
            };
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (Canvas.ActualWidth < 1 || Canvas.ActualHeight < 1) return;
            var width = (int)Math.Ceiling(Canvas.ActualWidth);
            var height = (int)Math.Ceiling(Canvas.ActualHeight);
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(Canvas);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            PngBytes = stream.ToArray();
            DialogResult = true;
        }

        private void Clear_Click(object sender, RoutedEventArgs e) => Canvas.Strokes.Clear();
        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }
    }
}
