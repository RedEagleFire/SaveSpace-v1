using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using SaveSpace.Services;

namespace SaveSpace.Views.Workspace
{
    public class BrowserEntry
    {
        public string Url { get; set; } = "";
        public string Title { get; set; } = "";
        public DateTime At { get; set; } = DateTime.Now;
    }

    public partial class BrowserPane : UserControl
    {
        private bool _initializing;
        private bool _initialized;
        private bool _failed;
        private string _pendingUrl = "";
        private string _title = "Nieuw tabblad";
        private bool _updatingAddress;

        public event Action<BrowserPane, string>? TitleChanged;
        public event Action<BrowserPane, string>? NewTabRequested;

        public string CurrentUrl => Web.CoreWebView2?.Source ?? _pendingUrl;
        public string PageTitle => string.IsNullOrWhiteSpace(_title) ? "Nieuw tabblad" : _title;
        public bool IsWebViewReady => _initialized;

        public BrowserPane(string? url = null)
        {
            InitializeComponent();
            _pendingUrl = string.IsNullOrWhiteSpace(url) ? AppServices.Settings.BrowserHome : url;
            Loaded += async (_, _) => await InitializeAsync();
            Unloaded += (_, _) => { };
        }

        public async Task InitializeAsync()
        {
            if (_initialized || _initializing) return;
            _initializing = true;
            _failed = false;
            FallbackPanel.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Visible;
            try
            {
                var env = await WebRuntime.GetAsync();
                await Web.EnsureCoreWebView2Async(env);
                var core = Web.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = true;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = true;
                core.Settings.IsZoomControlEnabled = true;

                core.SourceChanged += (_, _) => UpdateAddress(core.Source);
                core.NavigationStarting += (_, e) =>
                {
                    UpdateAddress(e.Uri);
                    LoadingText.Text = "Pagina laden…";
                    LoadingPanel.Visibility = Visibility.Visible;
                };
                core.NavigationCompleted += (_, e) =>
                {
                    LoadingPanel.Visibility = Visibility.Collapsed;
                    if (!e.IsSuccess)
                    {
                        LoadingPanel.Visibility = Visibility.Collapsed;
                    }
                    UpdateNav();
                    AddHistory(core.Source, SafeTitle(core));
                    _title = SafeTitle(core);
                    TitleChanged?.Invoke(this, _title);
                    UpdateFavorite();
                };
                core.DocumentTitleChanged += (_, _) =>
                {
                    _title = SafeTitle(core);
                    TitleChanged?.Invoke(this, _title);
                };
                core.HistoryChanged += (_, _) => UpdateNav();
                core.NewWindowRequested += (_, e) =>
                {
                    e.Handled = true;
                    if (NewTabRequested != null) NewTabRequested(this, e.Uri);
                    else try { FileActions.OpenUrl(e.Uri); } catch { }
                };
                core.DownloadStarting += OnDownloadStarting;

                Web.Visibility = Visibility.Visible;
                _initialized = true;
                Web.ZoomFactor = 1;
                Web.CoreWebView2.Navigate(_pendingUrl);
                UpdateNav();
                UpdateFavorite();
            }
            catch (Exception ex)
            {
                _failed = true;
                LoadingPanel.Visibility = Visibility.Collapsed;
                Web.Visibility = Visibility.Collapsed;
                FallbackPanel.Visibility = Visibility.Visible;
                FallbackTitle.Text = "Ingebouwde browser start niet";
                FallbackText.Text = "De WebView2-runtime van Microsoft Edge ontbreekt of kon niet starten. " +
                                    "Installeer de WebView2 Runtime en probeer opnieuw, of open de pagina extern.\n\n" + ex.Message;
                AppPaths.Log("WebView2 starten mislukt: " + ex.Message);
            }
            finally
            {
                _initializing = false;
            }
        }

        private static string SafeTitle(CoreWebView2 core)
        {
            try { return string.IsNullOrWhiteSpace(core.DocumentTitle) ? core.Source : core.DocumentTitle; }
            catch { return "Nieuw tabblad"; }
        }

        private void UpdateAddress(string? url)
        {
            if (url == null) return;
            _pendingUrl = url;
            if (AddressBox.IsKeyboardFocusWithin) return;
            _updatingAddress = true;
            AddressBox.Text = url;
            _updatingAddress = false;
            SchemeIcon.Text = url.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "\uE72E" : "\uE774";
        }

        private void UpdateNav()
        {
            try
            {
                BackButton.IsEnabled = Web.CoreWebView2?.CanGoBack == true;
                ForwardButton.IsEnabled = Web.CoreWebView2?.CanGoForward == true;
            }
            catch { }
            BackButton.Opacity = BackButton.IsEnabled ? 1 : 0.35;
            ForwardButton.Opacity = ForwardButton.IsEnabled ? 1 : 0.35;
        }

        public void Navigate(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return;
            var url = BuildUrl(input.Trim());
            _pendingUrl = url;
            if (_failed)
            {
                _ = InitializeAsync();
                return;
            }
            if (!_initialized)
            {
                _ = InitializeAsync();
                return;
            }
            try { Web.CoreWebView2.Navigate(url); }
            catch (Exception ex)
            {
                FileActions.ShowError(Window.GetWindow(this), ex.Message, "Browser");
            }
        }

        public static string BuildUrl(string input)
        {
            if (input.Contains("://", StringComparison.Ordinal))
                return input;
            if (!input.Contains(' ') && (input.Contains('.') || input.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)))
                return "https://" + input;
            var engine = AppServices.Settings.SearchEngine;
            var query = Uri.EscapeDataString(input);
            return engine switch
            {
                "google" => "https://www.google.com/search?q=" + query,
                "bing" => "https://www.bing.com/search?q=" + query,
                _ => "https://duckduckgo.com/?q=" + query
            };
        }

        private void GoHome()
        {
            var home = AppServices.Settings.BrowserHome;
            Navigate(home);
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            try { if (Web.CoreWebView2?.CanGoBack == true) Web.CoreWebView2.GoBack(); } catch { }
        }

        private void Forward_Click(object sender, RoutedEventArgs e)
        {
            try { if (Web.CoreWebView2?.CanGoForward == true) Web.CoreWebView2.GoForward(); } catch { }
        }

        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            try { if (Web.CoreWebView2 != null) Web.CoreWebView2.Reload(); } catch { }
        }

        private void Address_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Navigate(AddressBox.Text);
            Web.Focus();
            Keyboard.ClearFocus();
        }

        private void Address_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            AddressBox.SelectAll();
        }

        private void Address_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _ = _updatingAddress;
            if (!string.IsNullOrWhiteSpace(CurrentUrl)) UpdateAddress(CurrentUrl);
        }

        private void Favorite_Click(object sender, RoutedEventArgs e)
        {
            var url = CurrentUrl;
            if (string.IsNullOrWhiteSpace(url)) return;
            var favorites = LoadEntries("browser.favorites");
            var existing = favorites.FirstOrDefault(f => string.Equals(f.Url, url, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                favorites.Remove(existing);
                AppServices.Notifications.Add("Uit favorieten", existing.Title, "\uE734", "Brush.Muted");
            }
            else
            {
                favorites.Insert(0, new BrowserEntry { Url = url, Title = PageTitle, At = DateTime.Now });
                AppServices.Notifications.Add("Favoriet bewaard", PageTitle, "\uE735", "Brush.DarkYellow");
            }
            SaveEntries("browser.favorites", favorites.Take(120));
            UpdateFavorite();
        }

        private void UpdateFavorite()
        {
            var url = CurrentUrl;
            var isFav = !string.IsNullOrWhiteSpace(url) && LoadEntries("browser.favorites")
                .Any(f => string.Equals(f.Url, url, StringComparison.OrdinalIgnoreCase));
            FavoriteButton.Content = isFav ? "\uE735" : "\uE734";
        }

        private void History_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            var favorites = LoadEntries("browser.favorites");
            var history = LoadEntries("browser.history");
            if (favorites.Count > 0)
            {
                menu.Items.Add(new MenuItem { Header = "FAVORIETEN", IsEnabled = false, FontSize = 9.5 });
                foreach (var entry in favorites.Take(10))
                    menu.Items.Add(MakeEntryItem(entry, "\uE735"));
                menu.Items.Add(new Separator());
            }
            menu.Items.Add(new MenuItem { Header = "RECENT", IsEnabled = false, FontSize = 9.5 });
            if (history.Count == 0)
                menu.Items.Add(new MenuItem { Header = "Nog geen geschiedenis", IsEnabled = false });
            foreach (var entry in history.Take(14))
                menu.Items.Add(MakeEntryItem(entry, "\uE81C"));

            menu.PlacementTarget = HistoryButton;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private MenuItem MakeEntryItem(BrowserEntry entry, string icon)
        {
            var item = new MenuItem
            {
                Header = string.IsNullOrWhiteSpace(entry.Title) ? entry.Url : entry.Title,
                ToolTip = entry.Url
            };
            item.Click += (_, _) => Navigate(entry.Url);
            return item;
        }

        private void Menu_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            var home = new MenuItem { Header = "Startpagina" };
            home.Click += (_, _) => GoHome();
            menu.Items.Add(home);

            var external = new MenuItem { Header = "Openen in standaardbrowser" };
            external.Click += (_, _) => OpenExternal();
            menu.Items.Add(external);

            var copy = new MenuItem { Header = "Adres kopiëren" };
            copy.Click += (_, _) =>
            {
                try { Clipboard.SetText(CurrentUrl); } catch { }
            };
            menu.Items.Add(copy);

            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "ZOOM", IsEnabled = false, FontSize = 9.5 });
            foreach (var zoom in new[] { 0.8, 1.0, 1.25, 1.5, 2.0 })
            {
                var item = new MenuItem { Header = $"{zoom * 100:0}%", IsCheckable = true, IsChecked = Math.Abs(Web.ZoomFactor - zoom) < 0.01 };
                var value = zoom;
                item.Click += (_, _) => { try { Web.ZoomFactor = value; } catch { } };
                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator());
            var clear = new MenuItem { Header = "Scherm verversen" };
            clear.Click += (_, _) => Reload_Click(sender, e);
            menu.Items.Add(clear);

            menu.PlacementTarget = MenuButton;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void OpenExternal_Click(object sender, RoutedEventArgs e) => OpenExternal();

        private void OpenExternal()
        {
            var url = CurrentUrl;
            if (string.IsNullOrWhiteSpace(url)) return;
            try { FileActions.OpenUrl(url); }
            catch (Exception ex) { FileActions.ShowError(Window.GetWindow(this), ex.Message, "Browser"); }
        }

        private void Retry_Click(object sender, RoutedEventArgs e) => _ = InitializeAsync();

        private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
        {
            try
            {
                var folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "SaveSpace");
                Directory.CreateDirectory(folder);
                var suggested = Path.GetFileName(e.ResultFilePath);
                if (string.IsNullOrWhiteSpace(suggested)) suggested = "download";
                var target = Path.Combine(folder, suggested);
                var counter = 2;
                var stem = Path.GetFileNameWithoutExtension(suggested);
                var ext = Path.GetExtension(suggested);
                while (File.Exists(target))
                    target = Path.Combine(folder, $"{stem} ({counter++}){ext}");
                e.ResultFilePath = target;
                e.Handled = false;

                var op = e.DownloadOperation;
                var name = Path.GetFileName(target);
                op.StateChanged += (_, _) =>
                {
                    if (op.State == CoreWebView2DownloadState.Completed)
                        AppServices.Notifications.Add("Download klaar", name, "\uE896", "Brush.Sage");
                    else if (op.State == CoreWebView2DownloadState.Interrupted)
                        AppServices.Notifications.Add("Download mislukt", name, "\uE7BA", "Brush.Danger");
                };
                AppServices.Notifications.Add("Download gestart", name, "\uE896", "Brush.Teal");
            }
            catch (Exception ex)
            {
                AppPaths.Log("Download mislukt: " + ex.Message);
            }
        }

        public void Dispose()
        {
            try
            {
                if (_initialized)
                {
                    Web.Dispose();
                }
            }
            catch { }
            _initialized = false;
        }

        // ---------- Geschiedenis en favorieten ----------

        private static List<BrowserEntry> LoadEntries(string key)
        {
            try
            {
                var raw = AppServices.Library.GetSetting(key);
                if (string.IsNullOrWhiteSpace(raw)) return new List<BrowserEntry>();
                return JsonSerializer.Deserialize<List<BrowserEntry>>(raw) ?? new List<BrowserEntry>();
            }
            catch { return new List<BrowserEntry>(); }
        }

        private static void SaveEntries(string key, IEnumerable<BrowserEntry> entries)
        {
            try
            {
                AppServices.Library.SetSetting(key, JsonSerializer.Serialize(entries.ToList()));
            }
            catch (Exception ex)
            {
                AppPaths.Log("Browsegeschiedenis opslaan mislukt: " + ex.Message);
            }
        }

        private static void AddHistory(string url, string title)
        {
            if (string.IsNullOrWhiteSpace(url) || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;
            var history = LoadEntries("browser.history");
            history.RemoveAll(h => string.Equals(h.Url, url, StringComparison.OrdinalIgnoreCase));
            history.Insert(0, new BrowserEntry { Url = url, Title = title, At = DateTime.Now });
            SaveEntries("browser.history", history.Take(80));
        }
    }
}
