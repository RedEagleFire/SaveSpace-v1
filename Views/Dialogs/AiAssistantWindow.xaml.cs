using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SaveSpace.Services;

namespace SaveSpace.Views.Dialogs
{
    public class ChatMessage
    {
        public string RoleLabel { get; set; } = "";
        public string Text { get; set; } = "";
        public Brush BubbleBrush { get; set; } = Brushes.Transparent;
        public Brush TextBrush { get; set; } = Brushes.White;
        public Brush RoleBrush { get; set; } = Brushes.Gray;
        public HorizontalAlignment Alignment { get; set; } = HorizontalAlignment.Left;
        public Thickness Margin { get; set; } = new(0, 0, 0, 8);
    }

    public partial class AiAssistantWindow : Window
    {
        private readonly ObservableCollection<ChatMessage> _messages = new();
        private readonly List<(bool FromUser, string Text)> _history = new();
        private CancellationTokenSource? _cts;
        private bool _busy;

        public AiAssistantWindow()
        {
            InitializeComponent();
            MessagesList.ItemsSource = _messages;
            ModelText.Text = $"{AppServices.Ai.ProviderLabel} · {AppServices.Settings.AiModel}";
            AddMessage(false, "Hallo. Ik kan helpen met schrijven, samenvatten, structureren en vragen over wat je hier typt. Waar wil je aan werken?");
            PromptBox.Focus();
        }

        private void AddMessage(bool fromUser, string text)
        {
            _messages.Add(new ChatMessage
            {
                RoleLabel = fromUser ? "JIJ" : "NOTEAGENT",
                Text = text,
                BubbleBrush = (Brush)FindResource(fromUser ? "Brush.Accent" : "Brush.Surface"),
                TextBrush = (Brush)FindResource(fromUser ? "Brush.OnAccent" : "Brush.Ink"),
                RoleBrush = (Brush)FindResource(fromUser ? "Brush.OnAccent" : "Brush.Accent"),
                Alignment = fromUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = fromUser ? new Thickness(70, 0, 0, 8) : new Thickness(0, 0, 70, 8)
            });
            Dispatcher.BeginInvoke(new Action(() => ChatScroll.ScrollToEnd()));
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }

        private void PromptBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SendButton != null)
                SendButton.IsEnabled = !_busy && !string.IsNullOrWhiteSpace(PromptBox.Text);
        }

        private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();

        private async Task SendAsync()
        {
            if (_busy) return;
            var prompt = PromptBox.Text.Trim();
            if (prompt.Length == 0) return;

            AddMessage(true, prompt);
            _history.Add((true, prompt));
            PromptBox.Clear();
            SetBusy(true);
            _cts = new CancellationTokenSource();
            try
            {
                var context = new StringBuilder();
                foreach (var (fromUser, text) in _history)
                    context.AppendLine((fromUser ? "Gebruiker: " : "Assistent: ") + text);
                var result = await AppServices.Ai.ChatAsync(
                    "Je bent de ingebouwde assistent van SaveSpace, een Nederlandse notitie- en documentapplicatie. " +
                    "Antwoord helder, concreet en in de taal van de gebruiker. Verzin niets; zeg het als je iets niet weet.",
                    context.ToString(), _cts.Token);
                AddMessage(false, string.IsNullOrWhiteSpace(result) ? "Ik kreeg een leeg antwoord terug." : result.Trim());
                _history.Add((false, result));
            }
            catch (OperationCanceledException)
            {
                AddMessage(false, "Gestopt.");
            }
            catch (Exception ex)
            {
                AddMessage(false, "Er ging iets mis: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
                _cts?.Dispose();
                _cts = null;
                PromptBox.Focus();
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            SendButton.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
            StopButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = busy ? "Bezig met antwoorden…" : "";
            PromptBox.IsEnabled = !busy;
            if (!busy) SendButton.IsEnabled = !string.IsNullOrWhiteSpace(PromptBox.Text);
        }

        private void Stop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && PromptBox.IsKeyboardFocusWithin)
            {
                _ = SendAsync();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && !_busy)
            {
                Close();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
