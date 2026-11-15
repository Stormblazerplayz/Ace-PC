using System;
using System.Collections.Generic;
using System.Linq;

namespace Nimbus.AceV4.Brain
{
    public sealed record IntentGuess(string Intent, double Confidence);

    /// <summary>
    /// A tiny neural net (one softmax layer) on top of the MiniLM sentence vectors. It is trained right here,
    /// in under a second when Ace starts, from the example sentences below, and guesses which skill a request
    /// is for even when the words don't match a rule ("pull up the tube" → open a site).
    /// Without the MiniLM model it falls back to the closest example by words.
    /// </summary>
    public sealed class IntentClassifier
    {
        public static readonly Dictionary<string, string[]> Examples = new()
        {
            ["calendar.add"] = new[] { "add dentist to my calendar tomorrow at 3", "put soccer practice on friday at 5", "schedule a meeting next monday", "make an event called movie night saturday", "add a calendar event", "book time for homework tomorrow 4 to 6", "new event on the 20th", "i have a doctor appointment thursday at 10 add it" },
            ["calendar.edit"] = new[] { "move the dentist to friday", "change soccer practice to 6pm", "reschedule my meeting to tomorrow", "rename movie night to game night", "push homework back an hour", "edit my calendar event", "make the party start at 7 instead" },
            ["calendar.delete"] = new[] { "delete the dentist appointment", "remove soccer practice from my calendar", "cancel movie night", "clear my event tomorrow", "get rid of the meeting on friday" },
            ["calendar.list"] = new[] { "what's on my calendar", "what do i have today", "any events tomorrow", "show my schedule this week", "what's next on my calendar", "am i busy on friday", "what's my day look like" },
            ["power.shutdown"] = new[] { "shut down the pc", "turn off my computer", "power off", "shutdown now", "turn the computer off" },
            ["power.restart"] = new[] { "restart the pc", "reboot my computer", "restart now", "turn it off and on again" },
            ["power.sleep"] = new[] { "put the pc to sleep", "sleep mode", "make the computer sleep" },
            ["power.lock"] = new[] { "lock my pc", "lock the screen", "lock the computer" },
            ["app.open"] = new[] { "open spotify", "launch discord", "start minecraft", "run notepad", "open file explorer", "bring up blender", "fire up steam", "can you open calculator" },
            ["app.close"] = new[] { "close spotify", "quit discord", "exit chrome", "close notepad", "kill steam", "close that app" },
            ["app.minimize"] = new[] { "minimize discord", "hide spotify", "minimize everything", "show the desktop", "minimize this window" },
            ["app.maximize"] = new[] { "maximize chrome", "make discord full screen", "maximize this window", "restore spotify" },
            ["app.resize"] = new[] { "resize discord to 800 by 600", "snap chrome to the left half", "put spotify on the right side", "make notepad smaller", "make discord bigger", "move blender to the left" },
            ["app.list"] = new[] { "what apps are open", "what's running", "which programs are open", "list my open windows" },
            ["browser.open"] = new[] { "open youtube", "go to google", "open reddit in a new tab", "pull up github", "open roblox website", "take me to wikipedia", "open a new tab", "open amazon" },
            ["browser.search"] = new[] { "search google for cats", "look up minecraft seeds in the browser", "search youtube for mrbeast", "google how to tie a tie", "search the web for blender tutorials" },
            ["browser.tab"] = new[] { "close this tab", "next tab", "go to the previous tab", "reopen the closed tab", "refresh the page", "go back", "new tab", "switch tabs" },
            ["file.search"] = new[] { "find my homework doc", "where is the file called budget", "search my files for farside", "find pdfs from this week", "look for my resume", "find the blender file i saved yesterday", "find videos in downloads" },
            ["screenshot.search"] = new[] { "find the screenshot with the error", "show my latest screenshot", "find screenshots from yesterday", "where's the screenshot of the minecraft build", "screenshots that say discord" },
            ["web.search"] = new[] { "who won the world cup", "what's the latest news on spacex", "search the internet for the best gaming mouse", "look up how tall mount everest is", "find out when the next minecraft update comes out" },
            ["recipe"] = new[] { "how do i make pancakes", "recipe for chocolate chip cookies", "show me a mac and cheese recipe", "how to cook spaghetti", "what can i make with eggs and cheese", "give me a smoothie recipe" },
            ["weather"] = new[] { "what's the weather", "will it rain tomorrow", "how cold is it outside", "weather this weekend", "is it going to snow", "temperature right now" },
            ["define"] = new[] { "define photosynthesis", "what does ambiguous mean", "meaning of the word ubiquitous", "definition of algorithm" },
            ["system.status"] = new[] { "how's my pc doing", "check my cpu", "how much ram am i using", "is my computer running hot", "gpu usage", "system status", "what's my cpu temp" },
            ["storage"] = new[] { "how much space do i have", "check my storage", "what's taking up space", "find big files", "is my disk full", "free up space" },
            ["notifications"] = new[] { "any notifications", "did i miss anything", "what notifications do i have", "check my notifications", "anything i need to see" },
            ["sleep.bedtime"] = new[] { "when should i go to bed", "what time should i sleep", "how much am i sleeping", "my sleep schedule", "when do i usually go to bed", "bedtime" },
            ["timer"] = new[] { "set a timer for 10 minutes", "timer 5 min", "start a 30 second timer", "count down 2 minutes" },
            ["reminder"] = new[] { "remind me in 20 minutes to check the oven", "remind me tomorrow at 8 to take out the trash", "remind me to call grandma", "set a reminder for 5pm" },
            ["calc"] = new[] { "what's 25 times 4", "calculate 18 percent of 60", "what is 2 to the power of 10", "square root of 144", "add 15 and 27", "how much is 120 divided by 8" },
            ["convert"] = new[] { "convert 5 miles to km", "how many cups in a liter", "100 fahrenheit in celsius", "10 pounds to kilograms", "how many mb in a gb" },
            ["time"] = new[] { "what time is it", "time please", "what time is it in tokyo", "what's the date today", "what day is it", "how many days until christmas" },
            ["media"] = new[] { "pause the music", "play", "next song", "skip this track", "previous song", "resume music" },
            ["volume"] = new[] { "turn the volume up", "mute", "volume down", "unmute", "make it louder", "quieter please", "set volume to 50" },
            ["screenshot.take"] = new[] { "take a screenshot", "screenshot this", "capture my screen", "snip the screen" },
            ["remember"] = new[] { "remember that my favorite color is blue", "remember my wifi is on the router sticker", "note that the farside build is on drive d", "learn that my dog's name is max", "don't forget that i have a test friday" },
            ["forget"] = new[] { "forget that", "forget what i told you about my dog", "delete that memory", "that's wrong forget it" },
            ["check.now"] = new[] { "check my pc now", "do a check", "wake up and check everything", "run your checks", "scan my computer" },
            ["help"] = new[] { "what can you do", "help", "what are your skills", "how do i use you", "list your commands" },
            ["chat"] = new[] { "hi", "how are you", "tell me a joke", "what's the capital of france", "why is the sky blue", "who are you", "thanks", "good night", "what is a black hole", "i'm bored" },
        };

        private readonly Embedder _embedder;
        private string[] _labels = Array.Empty<string>();
        private float[,]? _w;        // [classes, 384]
        private float[]? _b;
        private List<(string Label, List<string> Words)> _lexExamples = new();
        private readonly Lexical _lex = new();

        public bool UsesNeuralNet => _w != null;

        public IntentClassifier(Embedder embedder) => _embedder = embedder;

        /// <summary>Trains the softmax layer. Takes well under a second on a CPU.</summary>
        public void Train()
        {
            _labels = Examples.Keys.ToArray();
            var texts = new List<string>();
            var ys = new List<int>();
            for (int c = 0; c < _labels.Length; c++)
                foreach (var s in Examples[_labels[c]]) { texts.Add(s); ys.Add(c); }

            _lexExamples = texts.Select((t, i) => (_labels[ys[i]], Lexical.Tokens(t))).ToList();
            _lex.Build(_lexExamples.Select(e => (IReadOnlyCollection<string>)e.Words));

            var x = _embedder.EmbedMany(texts);
            if (x == null) return;

            int k = _labels.Length, d = Embedder.Dim, n = x.Length;
            var w = new float[k, d];
            var b = new float[k];
            var rng = new Random(7);
            const float rate = 0.5f, l2 = 0.0005f;
            var probs = new float[k];
            for (int epoch = 0; epoch < 200; epoch++)
            {
                foreach (int i in Enumerable.Range(0, n).OrderBy(_ => rng.Next()))
                {
                    Forward(w, b, x[i], probs);
                    for (int c = 0; c < k; c++)
                    {
                        float g = probs[c] - (c == ys[i] ? 1f : 0f);
                        b[c] -= rate * g;
                        for (int j = 0; j < d; j++) w[c, j] -= rate * (g * x[i][j] + l2 * w[c, j]);
                    }
                }
            }
            _w = w;
            _b = b;
        }

        private static void Forward(float[,] w, float[] b, float[] x, float[] probs)
        {
            int k = b.Length, d = x.Length;
            float max = float.MinValue;
            for (int c = 0; c < k; c++)
            {
                // Vectors are unit length; scale logits so the softmax can get confident.
                float z = b[c];
                for (int j = 0; j < d; j++) z += w[c, j] * x[j];
                z *= 4f;
                probs[c] = z;
                if (z > max) max = z;
            }
            float sum = 0;
            for (int c = 0; c < k; c++) { probs[c] = MathF.Exp(probs[c] - max); sum += probs[c]; }
            for (int c = 0; c < k; c++) probs[c] /= sum;
        }

        /// <summary>The top guesses, best first.</summary>
        public List<IntentGuess> Guess(string text, int take = 3)
        {
            if (_labels.Length == 0) Train();
            if (_w != null && _b != null)
            {
                var v = _embedder.Embed(text);
                if (v != null)
                {
                    var probs = new float[_labels.Length];
                    Forward(_w, _b, v, probs);
                    return probs.Select((p, i) => new IntentGuess(_labels[i], p)).OrderByDescending(g => g.Confidence).Take(take).ToList();
                }
            }
            var words = Lexical.Tokens(text);
            return _lexExamples
                .GroupBy(e => e.Label)
                .Select(g => new IntentGuess(g.Key, g.Max(e => _lex.Match(words, e.Words))))
                .OrderByDescending(g => g.Confidence)
                .Take(take)
                .ToList();
        }
    }
}
