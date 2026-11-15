using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using Nimbus.Motion;
using Nimbus.Services.Ace;

namespace Nimbus.Pages
{
    public partial class AceChatPage : UserControl, INimbusPage
    {
        private static readonly string[] Suggestions =
        {
            "Remind me in 20 minutes to…", "What's open right now?", "Find a file called…", "Find the screenshot where…", "/help",
        };

        private const string HelpText =
            "Type to chat, tap + to add a picture or sound clip, or tap the mic, talk, and tap it again to hear me answer. " +
            "I run on LFM3 Omni 2B on your PC, with Qwen3.5 4B as backup.";

        private const long MaxAttachBytes = 20L * 1024 * 1024;

        private readonly AceBrain _brain = AceBrain.Shared;
        private bool _busy;
        private bool _ready;              // false while the constructor sets up the Run On chips
        private ChatTurn? _attachment;    // picture or clip waiting to go with the next message

        // Voice chat state
        private AceVoice? _voice;
        private bool _voiceBusy;
        private TextBlock? _youSaidText, _aceSaysText;
        private readonly StringBuilder _aceSays = new();
        private string _heard = "";

        public AceChatPage()
        {
            InitializeComponent();
            ResetChat();
            var spin = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(1.6))) { RepeatBehavior = RepeatBehavior.Forever };
            SpinTurn.BeginAnimation(RotateTransform.AngleProperty, spin);

            if (_brain.Config.UseGpu) RunGpu.IsChecked = true; else RunCpu.IsChecked = true;
            _ready = true;
            ShowStatus();

            Loaded += (_, _) => { _brain.StatusChanged += OnBrainStatus; ShowStatus(); };
            Unloaded += (_, _) => _brain.StatusChanged -= OnBrainStatus;
        }

        public Panel PopRoot => Root;

        public void OnShown()
        {
            Input.Focus();
            _ = WarmUp();   // start loading LFM3-Omni as soon as the tab opens, so the first answer is quick
        }

        private async Task WarmUp()
        {
            try { await _brain.WarmUpAsync(); }
            catch { /* the status label already says what happened */ }
        }

        // ================= Model status =================

        private void OnBrainStatus() => Dispatcher.BeginInvoke(new Action(ShowStatus));

        private void ShowStatus()
        {
            ModelName.Text = _brain.ModelName;
            string detail = _brain.Detail;
            ModelDetail.Text = detail.Length > 140 ? detail.Substring(0, 140) + "…" : detail;
            ModelDetail.Foreground = (Brush)FindResource(_brain.UsingFallback ? "AmberBrush" : "MutedBrush");
        }

        private async void RunOn_Checked(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            bool gpu = ReferenceEquals(sender, RunGpu);
            await CloseVoiceAsync();   // the server restarts, so the voice link would drop anyway
            try
            {
                string? problem = await _brain.SetUseGpuAsync(gpu);
                if (problem != null) Add("Couldn't switch: " + problem, false);
            }
            catch (Exception ex) { Add("Couldn't switch: " + ex.Message, false); }
        }

        // ================= Chat =================

        private void ResetChat()
        {
            ChatList.Children.Clear();
            var greet = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            greet.Children.Add(new Border
            {
                Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Margin = new Thickness(0, 0, 10, 0),
                Background = (Brush)FindResource("AceBrush"),
                Child = new TextBlock { Text = "A", FontWeight = FontWeights.ExtraBold, Foreground = (Brush)FindResource("InkBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            var hello = Bubble("Good evening! I'm Ace.", false);
            hello.BorderBrush = new SolidColorBrush(Color.FromArgb(0x8C, 0xF5, 0xB8, 0x3D));
            hello.BorderThickness = new Thickness(1.5);
            hello.Margin = new Thickness(0);
            greet.Children.Add(hello);
            ChatList.Children.Add(greet);

            var chips = new WrapPanel();
            foreach (var s in Suggestions)
            {
                var b = new Button { Content = s, Style = (Style)FindResource("SmallPillButton"), Margin = new Thickness(0, 0, 7, 7) };
                b.Click += (_, _) => _ = Send(s);
                chips.Children.Add(b);
            }
            ChatList.Children.Add(chips);
            Spring.PopChildren(ChatList);
        }

        private Border Bubble(string text, bool mine)
        {
            var b = new Border
            {
                Padding = new Thickness(13, 9, 13, 9),
                Margin = new Thickness(mine ? 60 : 0, 0, mine ? 0 : 60, 8),
                MaxWidth = 440,
                HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                CornerRadius = mine ? new CornerRadius(16, 16, 6, 16) : new CornerRadius(16, 16, 16, 6),
                Background = mine ? (Brush)FindResource("AccentBrush") : new SolidColorBrush(Color.FromArgb(0x17, 0xFF, 0xFF, 0xFF)),
                Child = new TextBlock
                {
                    Text = text, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Bold, FontSize = 13.5,
                    Foreground = mine ? new SolidColorBrush(Color.FromRgb(0x12, 0x0C, 0x2E)) : (Brush)FindResource("FgBrush"),
                },
            };
            return b;
        }

        private Border Add(string text, bool mine)
        {
            var b = Bubble(text, mine);
            ChatList.Children.Add(b);
            Spring.PopIn(b);
            ChatScroll.ScrollToEnd();
            return b;
        }

        private static TextBlock TextOf(Border bubble) => (TextBlock)bubble.Child;

        private async Task Send(string raw)
        {
            string text = raw.Trim();
            ChatTurn? attachment = _attachment;
            if ((text.Length == 0 && attachment == null) || _busy) return;
            _busy = true;
            Input.Clear();
            ClearAttachment();

            string label = text;
            if (attachment?.Image != null) label = ("🖼 picture  " + label).Trim();
            if (attachment?.Audio != null) label = ("🔊 sound clip  " + label).Trim();
            Add(label, true);
            var reply = Add("• • •", false);

            try
            {
                if (text.Equals("/help", StringComparison.OrdinalIgnoreCase) && attachment == null)
                {
                    await Task.Delay(250);
                    TextOf(reply).Text = HelpText;
                    return;
                }

                var question = attachment ?? new ChatTurn();
                question.Text = text;

                // Pieces arrive on a background thread; show them as they come.
                var sofar = new StringBuilder();
                AceReply answer = await _brain.AskAsync(question, piece => Dispatcher.BeginInvoke(new Action(() =>
                {
                    sofar.Append(piece);
                    string shown = OmniClient.Clean(sofar.ToString());
                    if (shown.Length > 0) { TextOf(reply).Text = shown; ChatScroll.ScrollToEnd(); }
                })));

                // Set the final text after the queued pieces, so a late piece can't overwrite it.
                await Dispatcher.InvokeAsync(() =>
                    TextOf(reply).Text = answer.Text.Length > 0 ? answer.Text : "(Ace had nothing to say. Try asking another way.)",
                    System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                TextOf(reply).Text = "I couldn't answer: " + ex.Message;
            }
            finally
            {
                _busy = false;
                ChatScroll.ScrollToEnd();
            }
        }

        private void Send_Click(object sender, RoutedEventArgs e) => _ = Send(Input.Text);

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { e.Handled = true; _ = Send(Input.Text); }
        }

        private async void New_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            ResetChat();
            ClearAttachment();
            _brain.NewChat();
            await CloseVoiceAsync();   // a fresh voice link also starts a fresh spoken conversation
        }

        // ================= Attachments =================

        private void Attach_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Show or play something to Ace",
                Filter = "Pictures and sound|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp;*.wav;*.mp3|Pictures|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp|Sound|*.wav;*.mp3",
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                if (new FileInfo(dialog.FileName).Length > MaxAttachBytes)
                {
                    Add("That file is over 20 MB. Try a smaller picture or a shorter clip.", false);
                    return;
                }
                var turn = new ChatTurn();
                if (!turn.TryAttach(dialog.FileName))
                {
                    Add("I can read png, jpg, webp, gif and bmp pictures, and wav or mp3 sound.", false);
                    return;
                }
                _attachment = turn;
                AttachText.Text = (turn.Image != null ? "🖼 " : "🔊 ") + Path.GetFileName(dialog.FileName);
                AttachChip.Visibility = Visibility.Visible;
                Input.Focus();
            }
            catch (Exception ex) { Add("Couldn't open that file: " + ex.Message, false); }
        }

        private void ClearAttach_Click(object sender, RoutedEventArgs e) => ClearAttachment();

        private void ClearAttachment()
        {
            _attachment = null;
            AttachChip.Visibility = Visibility.Collapsed;
            AttachText.Text = "";
        }

        // ================= Voice =================

        private async void Mic_Click(object sender, RoutedEventArgs e)
        {
            if (_voiceBusy) return;
            _voiceBusy = true;
            MicButton.IsEnabled = false;
            try
            {
                if (_voice == null || !_voice.IsOpen)
                {
                    await CloseVoiceAsync();
                    ModelDetail.Text = "connecting voice…";
                    _voice = await _brain.StartVoiceAsync();
                    HookVoice(_voice);
                    ShowStatus();
                }

                if (!_voice.IsListening)
                {
                    _voice.StartListening();
                    MicButton.Background = (Brush)FindResource("AmberBrush");
                    MicIcon.Stroke = (Brush)FindResource("InkBrush");
                    _heard = "";
                    _youSaidText = TextOf(Add("🎙 Listening… tap the mic when you're done", true));
                    _aceSaysText = null;
                }
                else
                {
                    // Make Ace's bubble first, so the first words of the answer land in it.
                    MicOff();
                    if (_youSaidText != null) _youSaidText.Text = "🎙 Voice message";
                    _aceSays.Clear();
                    _aceSaysText = TextOf(Add("• • •", false));
                    await _voice.StopListeningAndAnswerAsync();
                }
            }
            catch (Exception ex)
            {
                MicOff();
                ShowStatus();
                Add("Voice isn't working: " + ex.Message, false);
                await CloseVoiceAsync();
            }
            finally
            {
                _voiceBusy = false;
                MicButton.IsEnabled = true;
            }
        }

        private void HookVoice(AceVoice voice)
        {
            voice.AceText += t => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!ReferenceEquals(voice, _voice)) return;
                _aceSays.Append(t);
                _aceSaysText ??= TextOf(Add("", false));
                string shown = OmniClient.Clean(_aceSays.ToString());
                if (shown.Length > 0) { _aceSaysText.Text = shown; ChatScroll.ScrollToEnd(); }
            }));
            voice.HeardYou += t => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!ReferenceEquals(voice, _voice) || string.IsNullOrWhiteSpace(t)) return;
                _heard = t.Trim();
                if (_youSaidText != null) _youSaidText.Text = "🎙 " + _heard;
            }));
            voice.AnswerDone += () => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!ReferenceEquals(voice, _voice)) return;
                string said = OmniClient.Clean(_aceSays.ToString()).Trim();
                if (_aceSaysText != null && said.Length == 0) _aceSaysText.Text = "🔊 (spoken answer)";
                _brain.AddVoiceExchange(_heard, said);
                _aceSays.Clear();
                _aceSaysText = null;
            }));
            voice.Problem += msg => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ReferenceEquals(voice, _voice)) Add("Voice: " + msg, false);
            }));
            voice.Disconnected += () => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!ReferenceEquals(voice, _voice)) return;
                _voice = null;
                MicOff();
                _ = voice.DisposeAsync().AsTask();
            }));
        }

        private void MicOff()
        {
            MicButton.ClearValue(BackgroundProperty);
            MicIcon.Stroke = (Brush)FindResource("FgBrush");
        }

        private async Task CloseVoiceAsync()
        {
            var voice = _voice;
            _voice = null;
            MicOff();
            if (voice != null)
            {
                try { await voice.DisposeAsync(); } catch { }
            }
        }

        // ================= Team =================

        private void Mate_Click(object sender, RoutedEventArgs e)
        {
            var face = (sender as FrameworkElement)?.Tag switch
            {
                "Slate" => SlateFace,
                "Scout" => ScoutFace,
                _ => ForgeFace,
            };
            Spring.Jump(face, 22);
        }
    }
}
