using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SaveSpace.Services;

namespace SaveSpace.Views.Dialogs
{
    public partial class ProjectEditorDialog : Window
    {
        private static readonly string[] Colors = { "#E32636", "#8E1722", "#3FB5A6", "#E8A33D", "#7FA268", "#6D8CA8", "#B07ACC", "#A8A9B0" };
        private static readonly (string Glyph, string Label)[] Icons =
        {
            ("\uE8B7", "Map"), ("\uE70B", "Notitie"), ("\uE821", "Werk"), ("\uE7C3", "Pagina"),
            ("\uE8A5", "Document"), ("\uE735", "Ster"), ("\uE714", "Media"), ("\uE8D6", "Audio")
        };

        private string _color;
        private string _icon;

        public string ProjectName => NameBox.Text.Trim();
        public string SelectedColor => _color;
        public string SelectedIcon => _icon;

        public ProjectEditorDialog(string name, string color, string icon)
        {
            InitializeComponent();
            _color = string.IsNullOrWhiteSpace(color) ? Colors[0] : color;
            _icon = string.IsNullOrWhiteSpace(icon) ? "\uE8B7" : icon;
            NameBox.Text = name;
            TitleText.Text = string.IsNullOrWhiteSpace(name) ? "Nieuw project" : "Projectinstellingen";
            SaveButton.Content = string.IsNullOrWhiteSpace(name) ? "Project maken" : "Wijzigingen opslaan";
            BuildColors();
            BuildIcons();
            UpdatePreview();
            Loaded += (_, _) =>
            {
                NameBox.Focus();
                NameBox.SelectAll();
            };
        }

        private void BuildColors()
        {
            ColorPanel.Children.Clear();
            foreach (var hex in Colors)
            {
                var color = Parse(hex);
                var selected = string.Equals(hex, _color, StringComparison.OrdinalIgnoreCase);
                var outer = new Border
                {
                    Width = 34,
                    Height = 34,
                    Margin = new Thickness(0, 0, 7, 7),
                    CornerRadius = new CornerRadius(10),
                    BorderThickness = new Thickness(selected ? 2 : 1),
                    BorderBrush = selected ? (Brush)FindResource("Brush.Ink") : (Brush)FindResource("Brush.Line"),
                    Background = new SolidColorBrush(color),
                    Cursor = Cursors.Hand,
                    ToolTip = hex
                };
                outer.MouseLeftButtonUp += (_, _) =>
                {
                    _color = hex;
                    BuildColors();
                    UpdatePreview();
                };
                ColorPanel.Children.Add(outer);
            }
        }

        private void BuildIcons()
        {
            IconPanel.Children.Clear();
            foreach (var (glyph, label) in Icons)
            {
                var selected = glyph == _icon;
                var button = new Border
                {
                    Width = 34,
                    Height = 34,
                    Margin = new Thickness(0, 0, 7, 7),
                    CornerRadius = new CornerRadius(9),
                    Background = selected ? (Brush)FindResource("Brush.AccentSoft") : (Brush)FindResource("Brush.SurfaceAlt"),
                    BorderBrush = selected ? (Brush)FindResource("Brush.AccentBorder") : Brushes.Transparent,
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    ToolTip = label
                };
                button.Child = new TextBlock
                {
                    Text = glyph,
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = 14,
                    Foreground = selected ? (Brush)FindResource("Brush.Accent") : (Brush)FindResource("Brush.Muted"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                button.MouseLeftButtonUp += (_, _) =>
                {
                    _icon = glyph;
                    BuildIcons();
                    UpdatePreview();
                };
                IconPanel.Children.Add(button);
            }
        }

        private void UpdatePreview()
        {
            var color = Parse(_color);
            PreviewBadge.Background = new SolidColorBrush(color) { Opacity = 0.16 };
            PreviewIcon.Text = _icon;
            PreviewIcon.Foreground = new SolidColorBrush(color);
            PreviewName.Text = string.IsNullOrWhiteSpace(NameBox.Text) ? "Naamloos project" : NameBox.Text.Trim();
        }

        private static Color Parse(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return Color.FromRgb(227, 38, 54); }
        }

        private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ErrorText.Visibility = Visibility.Collapsed;
            UpdatePreview();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                ErrorText.Text = "Geef je project een naam.";
                ErrorText.Visibility = Visibility.Visible;
                NameBox.Focus();
                return;
            }
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) DialogResult = false;
            else if (e.Key == Key.Enter && NameBox.IsKeyboardFocusWithin)
            {
                Save_Click(sender, e);
                e.Handled = true;
            }
        }
    }
}
