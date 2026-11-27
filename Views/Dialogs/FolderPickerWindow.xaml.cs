using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SaveSpace.Models;

namespace SaveSpace.Views.Dialogs
{
    public class FolderChoice
    {
        public long? Id { get; set; }
        public string Name { get; set; } = "";
        public string CountLabel { get; set; } = "";
        public string Color { get; set; } = "#94897D";
        public Brush ColorBrush
        {
            get
            {
                try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(Color)); }
                catch { return Brushes.Gray; }
            }
        }
    }

    public partial class FolderPickerWindow : Window
    {
        public long? SelectedFolderId { get; private set; }

        public FolderPickerWindow(IEnumerable<Folder> folders, long? currentFolderId)
        {
            InitializeComponent();
            var choices = new List<FolderChoice>
            {
                new() { Id = null, Name = "Geen map", CountLabel = "Het item staat los in je bibliotheek", Color = "#94897D" }
            };
            choices.AddRange(folders.Select(f => new FolderChoice
            {
                Id = f.Id,
                Name = f.Name,
                CountLabel = f.CountLabel,
                Color = f.Color
            }));
            FolderList.ItemsSource = choices;
            FolderList.SelectedItem = choices.FirstOrDefault(c => c.Id == currentFolderId) ?? choices[0];
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }

        private void Choose_Click(object sender, RoutedEventArgs e)
        {
            if (FolderList.SelectedItem is FolderChoice choice)
            {
                SelectedFolderId = choice.Id;
                DialogResult = true;
            }
        }

        private void FolderList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Choose_Click(sender, e);

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
