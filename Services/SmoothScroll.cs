using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace SaveSpace.Services
{
    /// <summary>
    /// Maakt het muiswiel vloeiend: in plaats van één grote sprong animeert de scrollpositie
    /// met een zachte uitloop. Geneste lijsten krijgen het wiel eerst, daarna pas de buitenste.
    /// </summary>
    public static class SmoothScroll
    {
        private const double PixelsPerNotch = 48;
        private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(170);

        private static readonly DependencyProperty AnimatedOffsetProperty =
            DependencyProperty.RegisterAttached("AnimatedOffset", typeof(double), typeof(SmoothScroll),
                new PropertyMetadata(0.0, OnAnimatedOffsetChanged));

        public static void Enable()
        {
            EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.MouseWheelEvent,
                new MouseWheelEventHandler(OnMouseWheel), handledEventsToo: false);
        }

        private static void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled || sender is not ScrollViewer sv) return;
            if (sv.ScrollableHeight <= 0 || e.Delta == 0) return;
            if (Keyboard.Modifiers != ModifierKeys.None) return;

            e.Handled = true;
            var start = sv.VerticalOffset;
            var target = Math.Clamp(start - Math.Sign(e.Delta) * PixelsPerNotch, 0, sv.ScrollableHeight);
            if (Math.Abs(target - start) < 0.5) return;

            var animation = new DoubleAnimation(start, target, Duration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            sv.BeginAnimation(AnimatedOffsetProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private static void OnAnimatedOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer sv && e.NewValue is double offset)
                sv.ScrollToVerticalOffset(offset);
        }
    }
}
