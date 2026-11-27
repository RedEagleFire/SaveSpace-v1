using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SaveSpace.Models;

namespace SaveSpace.Views.Dialogs
{
    public partial class ItemPickerWindow : Window
    {
        private readonly List<LibraryItem> _all;
        private List<LibraryItem> _shown = new();

        public LibraryItem? SelectedItem => ItemList.SelectedItem as LibraryItem;

        public ItemPickerWindow(IEnumerable<LibraryItem> items, string title)
        {
            InitializeComponent();
            _all = items.ToList();
            TitleText.Text = title;
            ApplyFilter();
            Loaded += (_, _) => SearchBox.Focus();
        }

        private void ApplyFilter()
        {
            var query = SearchBox?.Text?.Trim() ?? "";
            _shown = query.Length == 0
                ? _all
                : _all.Where(i => i.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            ItemList.ItemsSource = _shown;
            EmptyText.Visibility = _shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ChooseButton.IsEnabled = false;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            ChooseButton.IsEnabled = SelectedItem != null;

        private void Choose_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedItem != null) DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }
    }
}
