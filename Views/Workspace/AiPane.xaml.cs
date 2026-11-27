using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SaveSpace.Services;
using SaveSpace.Views.Dialogs;

namespace SaveSpace.Views.Workspace
{
    public class AiChatEntry
    {
        public bool IsUser { get; set; }
        public string Text { get; set; } = "";
        public DateTime At { get; set; } = DateTime.Now;
    }

    public partial class AiPane : UserControl
    {
        private readonly MainWindow _host;
        private readonly Func<string> _getNoteText;
        private readonly Func<string?> _getNoteTitle;
        private readonly Action<string> _insertText;
        private readonly Action<string> _replaceText;
        private readonly Action<string> _applyTitle;
        private readonly List<AiChatEntry> _entries = new();
        private CancellationTokenSource? _cts;
        private long? _noteId;
        private bool _busy;

        public AiPane(MainWindow host, Func<string> getNoteText, Func<string?> getNoteTitle,
            Action<string> insertText, Action<string> replaceText, Action<string> applyTitle)
        {
            InitializeComponent();
            _host = host;
            _getNoteText = getNoteText;
            _getNoteTitle = getNoteTitle;
            _insertText = insertText;
            _replaceText = replaceText;
            _applyTitle = applyTitle;
            UpdateModelLabel();
            Unloaded += (_, _) => Stop();
        }

        public void SetNote(long? noteId)
        {
            if (_noteId == noteId) { UpdateModelLabel(); return; }
            Stop();
            _noteId = noteId;
            _entries.Clear();
            MessagesPanel.Children.Clear();
            var raw = LoadHistory();
            if (raw.Count > 0)
            {
                _entries.AddRange(raw);
                foreach (var entry in raw) AddBubble(entry.Text, entry.IsUser, entry.At, false);
            }
            UpdateModelLabel();
            UpdateEmptyState();
            ScrollToEnd();
        }

        public void UpdateModelLabel()
        {
            ModelText.Text = AppServices.Ai.IsConfigured
                ? $"{AppServices.Ai.ProviderLabel} · {AppServices.Settings.AiModel}"
                : "AI nog niet ingesteld";
            ModelText.Foreground = AppServices.Ai.IsConfigured
                ? (Brush)FindResource("Brush.Ink")
                : (Brush)FindResource("Brush.Muted");
            UpdateEmptyState();
        }

        private void UpdateEmptyState()
        {
            var configured = AppServices.Ai.IsConfigured;
            EmptyTitle.Text = configured ? "Vraag over deze notitie" : "AI is optioneel";
            EmptyText.Text = configured
                ? "De assistent leest alleen de notitie die je nu open hebt. Je ziet eerst elk voorstel voordat er iets verandert."
                : "Er is nog geen provider ingesteld. Kies Ollama voor lokaal gebruik of OpenAI, Anthropic of een compatibel endpoint bij de instellingen.";
            EmptyPanel.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            InputBox.IsEnabled = configured;
            SendButton.IsEnabled = configured && !_busy;
        }

        private string HistoryKey => "ai.chat." + (_noteId?.ToString() ?? "draft");

        private List<AiChatEntry> LoadHistory()
        {
            try
            {
                var raw = AppServices.Library.GetSetting(HistoryKey);
                if (string.IsNullOrWhiteSpace(raw)) return new List<AiChatEntry>();
                return JsonSerializer.Deserialize<List<AiChatEntry>>(raw) ?? new List<AiChatEntry>();
            }
            catch { return new List<AiChatEntry>(); }
        }

        private void SaveHistory()
        {
            try
            {
                AppServices.Library.SetSetting(HistoryKey, JsonSerializer.Serialize(_entries.TakeLast(80).ToList()));
            }
            catch (Exception ex)
            {
                AppPaths.Log("AI-geschiedenis opslaan mislukt: " + ex.Message);
            }
        }

        private void Send_Click(object sender, RoutedEventArgs e) => _ = SendAsync();

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
            e.Handled = true;
            _ = SendAsync();
        }

        private async Task SendAsync()
        {
            if (_busy) return;
            var input = InputBox.Text.Trim();
            if (input.Length == 0) return;
            if (!AppServices.Ai.IsConfigured)
            {
                NavigateSettings();
                return;
            }
            InputBox.Clear();
            AddMessage(input, true);

            var text = _getNoteText();
            var title = _getNoteTitle();
            var system = "Je bent de schrijfassistent van SaveSpace. Je helpt met de notitie die de gebruiker nu open heeft. " +
                         "Antwoord in het Nederlands, bondig en concreet. Verzin niets dat niet in de notitie staat. " +
                         "Je mag voorstellen doen, maar voert nooit zelf wijzigingen door.";
            var truncated = text.Length > 14000 ? "\n(Let op: de notitie is ingekort tot de eerste 14.000 tekens.)" : "";
            var prompt = $"Notitie: {title}\n\n---\n{Truncate(text, 14000)}\n---{truncated}\n\nVraag van de gebruiker: {input}";
            await RunAsync("Denkt na…", ct => AppServices.Ai.ChatAsync(system, prompt, ct), addToChat: true);
        }

        private void Stop_Click(object sender, RoutedEventArgs e) => Stop();

        private void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            _cts = null;
            SetBusy(false, "");
        }

        private void Settings_Click(object sender, RoutedEventArgs e) => NavigateSettings();

        private void NavigateSettings()
        {
            _host.Navigate(PageKind.Settings);
        }

        private async Task RunAsync(string busyLabel, Func<CancellationToken, Task<string>> work, bool addToChat)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            SetBusy(true, busyLabel);
            try
            {
                var result = await work(token);
                if (token.IsCancellationRequested) return;
                if (addToChat) AddMessage(result.Trim(), false);
                StatusText.Text = "";
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Gestopt.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Mislukt";
                AddBubble("De assistent kon niet antwoorden: " + ex.Message, false, DateTime.Now, false, error: true);
            }
            finally
            {
                if (_cts != null && _cts.Token == token)
                {
                    _cts.Dispose();
                    _cts = null;
                }
                SetBusy(false, "");
            }
        }

        private void SetBusy(bool busy, string label)
        {
            _busy = busy;
            BusyText.Text = busy ? label : "";
            StopButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            SendButton.IsEnabled = !busy && AppServices.Ai.IsConfigured;
            SendButton.Opacity = SendButton.IsEnabled ? 1 : 0.45;
        }

        private async void Summarize_Click(object sender, RoutedEventArgs e)
        {
            var text = _getNoteText();
            if (!RequireText(text)) return;
            await RunAsync("Samenvatten…", ct => AppServices.Ai.ChatAsync(
                "Je bent een Nederlandstalige schrijfassistent. Antwoord alleen met de gevraagde tekst.",
                "Vat deze notitie samen in enkele heldere zinnen. Behoud de kern en laat details weg die niet nodig zijn.\n\n" + Truncate(text, 14000), ct),
                addToChat: true);
        }

        private async void Improve_Click(object sender, RoutedEventArgs e)
        {
            var text = _getNoteText();
            if (!RequireText(text)) return;
            var result = await RunSuggestionAsync("Herschrijven…", "Herschrijf deze notitie helderder en grammaticaal correct. Behoud de toon, betekenis en taal. Antwoord alleen met de herschreven tekst.", text);
            if (result == null) return;
            ShowSuggestion("Verbeterde tekst", result, insert: true, replace: true);
        }

        private async void Continue_Click(object sender, RoutedEventArgs e)
        {
            var text = _getNoteText();
            if (!RequireText(text)) return;
            var result = await RunSuggestionAsync("Verder schrijven…", "Schrijf een kort, passend vervolg op deze notitie in dezelfde toon en taal. Antwoord alleen met het vervolg.", text);
            if (result == null) return;
            ShowSuggestion("Vervolg", result, insert: true, replace: false);
        }

        private async void Title_Click(object sender, RoutedEventArgs e)
        {
            var text = _getNoteText();
            if (!RequireText(text)) return;
            var result = await RunSuggestionAsync("Titel bedenken…", "Stel één korte, pakkende titel voor deze notitie voor. Geef alleen de titel, zonder aanhalingstekens of uitleg.", text);
            if (result == null) return;
            var title = result.Trim().Trim('"', '\'', ' ');
            if (title.Length == 0) return;
            if (title.Length > 120) title = title[..120];
            if (!ConfirmDialog.Show(Window.GetWindow(this), "Titel toepassen?",
                    $"Voorgestelde titel:\n\n«{title}»\n\nWil je deze titel gebruiken?", "Titel toepassen", false, "\uE8AC"))
                return;
            _applyTitle(title);
            StatusText.Text = "Titel toegepast.";
        }

        private async Task<string?> RunSuggestionAsync(string busyLabel, string instruction, string text)
        {
            if (!AppServices.Ai.IsConfigured)
            {
                NavigateSettings();
                return null;
            }
            string? result = null;
            await RunAsync(busyLabel, async ct =>
            {
                result = await AppServices.Ai.ChatAsync(
                    "Je bent een behulpzame Nederlandstalige schrijfassistent. Antwoord bondig, zonder inleiding of meta-uitleg.",
                    instruction + "\n\n---\n" + Truncate(text, 14000), ct);
                return result;
            }, addToChat: false);
            return result;
        }

        private void ShowSuggestion(string label, string result, bool insert, bool replace)
        {
            var dialog = new AiResultWindow(label, result,
                insert ? _insertText : null,
                replace ? _replaceText : null)
            {
                Owner = Window.GetWindow(this)
            };
            dialog.ShowDialog();
        }

        private bool RequireText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                StatusText.Text = "Schrijf eerst tekst in deze notitie.";
                return false;
            }
            if (!AppServices.Ai.IsConfigured)
            {
                NavigateSettings();
                return false;
            }
            return true;
        }

        private static string Truncate(string text, int max) =>
            string.IsNullOrEmpty(text) || text.Length <= max ? text : text[..max] + "\n[…ingekort]";

        private void AddMessage(string text, bool isUser)
        {
            var entry = new AiChatEntry { Text = text, IsUser = isUser, At = DateTime.Now };
            _entries.Add(entry);
            AddBubble(text, isUser, entry.At, true);
            SaveHistory();
        }

        private void AddBubble(string text, bool isUser, DateTime at, bool animate, bool error = false)
        {
            EmptyPanel.Visibility = Visibility.Collapsed;
            var outer = new StackPanel
            {
                HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = new Thickness(10, 4, 10, 0),
                MaxWidth = 370
            };
            var bubble = new Border
            {
                Background = error
                    ? (Brush)FindResource("Brush.DangerSoft")
                    : isUser ? (Brush)FindResource("Brush.AccentSoft") : (Brush)FindResource("Brush.SurfaceAlt"),
                BorderBrush = error ? (Brush)FindResource("Brush.Danger") : (Brush)FindResource("Brush.Line"),
                BorderThickness = new Thickness(1),
                CornerRadius = (CornerRadius)FindResource("Radius.M"),
                Padding = new Thickness(10, 7, 10, 7)
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11.5,
                LineHeight = 17,
                Foreground = (Brush)FindResource("Brush.Ink")
            });
            stack.Children.Add(new TextBlock
            {
                Text = at.ToString("HH:mm"),
                FontSize = 8.5,
                Foreground = (Brush)FindResource("Brush.Faint"),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 3, 0, 0)
            });
            bubble.Child = stack;
            outer.Children.Add(bubble);
            MessagesPanel.Children.Add(outer);
            if (animate && ThemeManager.AnimationsEnabled)
            {
                outer.Opacity = 0;
                outer.BeginAnimation(OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            }
            ScrollToEnd();
        }

        private void ScrollToEnd()
        {
            Dispatcher.BeginInvoke(new Action(() => ChatScroll.ScrollToEnd()), System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
