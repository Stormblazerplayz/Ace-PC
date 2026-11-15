using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Nimbus.AceV4.Brain;
using Windows.Graphics.Imaging;
using Windows.Media.Control;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

namespace Nimbus.AceV4.Pc
{
    // ============================================================================ What's playing

    public sealed class SongInfo
    {
        public string Title = "";
        public string Artist = "";
        public string Album = "";
        /// <summary>The app playing it ("Spotify", "Chrome").</summary>
        public string App = "";
        public bool Playing;
        public byte[]? Art;
        public TimeSpan Position;
        public TimeSpan Length;
        public bool CanSkip;
    }

    /// <summary>
    /// What's playing right now in any app that shows up in Windows' media controls (Spotify, YouTube in a browser,
    /// Apple Music, games...), with play/pause and skip. Uses Windows' own media session list.
    /// </summary>
    public static class NowPlaying
    {
        private static GlobalSystemMediaTransportControlsSessionManager? _manager;
        /// <summary>Raised when the song or play state changes (on a background thread).</summary>
        public static event Action? Changed;
        private static GlobalSystemMediaTransportControlsSession? _watched;

        private static async Task<GlobalSystemMediaTransportControlsSession?> SessionAsync()
        {
            try
            {
                if (_manager == null)
                {
                    _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                    _manager.CurrentSessionChanged += (_, _) => { Watch(); Changed?.Invoke(); };
                    _manager.SessionsChanged += (_, _) => Changed?.Invoke();
                }
                // The one Windows thinks is current, else any that's playing.
                var current = _manager.GetCurrentSession();
                if (current == null || current.GetPlaybackInfo()?.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    var playing = _manager.GetSessions().FirstOrDefault(s => s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
                    if (playing != null) current = playing;
                }
                return current;
            }
            catch { return null; }
        }

        private static void Watch()
        {
            try
            {
                var s = _manager?.GetCurrentSession();
                if (s == null || ReferenceEquals(s, _watched)) return;
                _watched = s;
                s.MediaPropertiesChanged += (_, _) => Changed?.Invoke();
                s.PlaybackInfoChanged += (_, _) => Changed?.Invoke();
            }
            catch { }
        }

        /// <summary>What's playing, or null if nothing is (or Windows won't say).</summary>
        public static async Task<SongInfo?> GetAsync(bool withArt = true)
        {
            var s = await SessionAsync();
            if (s == null) return null;
            Watch();
            try
            {
                var props = await s.TryGetMediaPropertiesAsync();
                var info = s.GetPlaybackInfo();
                var timeline = s.GetTimelineProperties();
                var song = new SongInfo
                {
                    Title = props?.Title ?? "",
                    Artist = props?.Artist ?? props?.AlbumArtist ?? "",
                    Album = props?.AlbumTitle ?? "",
                    App = AppOf(s.SourceAppUserModelId ?? ""),
                    Playing = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                    CanSkip = info?.Controls?.IsNextEnabled ?? false,
                    Position = timeline?.Position ?? TimeSpan.Zero,
                    Length = timeline != null ? timeline.EndTime - timeline.StartTime : TimeSpan.Zero,
                };
                if (song.Title.Length == 0) return null;
                if (withArt && props?.Thumbnail != null)
                {
                    try
                    {
                        using var stream = await props.Thumbnail.OpenReadAsync();
                        if (stream.Size > 0 && stream.Size < 8_000_000)
                        {
                            using var reader = new DataReader(stream.GetInputStreamAt(0));
                            uint n = await reader.LoadAsync((uint)stream.Size);
                            var bytes = new byte[n];
                            reader.ReadBytes(bytes);
                            song.Art = bytes;
                        }
                    }
                    catch { }
                }
                return song;
            }
            catch { return null; }
        }

        /// <summary>"Spotify.exe" → "Spotify", "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify" → "Spotify", "MSEdge" → "Edge".</summary>
        public static string AppOf(string id)
        {
            string s = id;
            if (s.Contains('!')) s = s[(s.LastIndexOf('!') + 1)..];
            s = Regex.Replace(s, @"\.exe$", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"_[a-z0-9]{10,}$", "");
            if (s.Contains('.')) s = s[(s.LastIndexOf('.') + 1)..];
            return s.ToLowerInvariant() switch
            {
                "msedge" or "edge" => "Edge",
                "chrome" => "Chrome",
                "firefox" => "Firefox",
                "spotify" or "spotifymusic" => "Spotify",
                "zunemusic" or "music" => "Media Player",
                "applemusic" or "applemusicwin" => "Apple Music",
                "" => "an app",
                _ => WindowControl.AppName(s),
            };
        }

        public static async Task<bool> PlayPauseAsync()
        {
            var s = await SessionAsync();
            if (s == null) { Media.PlayPause(); return false; }
            try { return await s.TryTogglePlayPauseAsync(); } catch { Media.PlayPause(); return false; }
        }

        public static async Task<bool> NextAsync()
        {
            var s = await SessionAsync();
            if (s == null) { Media.Next(); return false; }
            try { return await s.TrySkipNextAsync(); } catch { Media.Next(); return false; }
        }

        public static async Task<bool> PreviousAsync()
        {
            var s = await SessionAsync();
            if (s == null) { Media.Previous(); return false; }
            try { return await s.TrySkipPreviousAsync(); } catch { Media.Previous(); return false; }
        }
    }

    // ============================================================================ What's on my screen

    public sealed class ScreenText
    {
        public List<string> Lines = new();
        public string Text => string.Join("\n", Lines);
        public int Words;
        /// <summary>The app that was in front.</summary>
        public string App = "";
        public string Language = "";
    }

    /// <summary>
    /// Reads the text on the main screen with Windows' built-in OCR (no internet). Takes a picture of the
    /// screen in memory only; nothing is saved.
    /// </summary>
    public static class ScreenReader
    {
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, byte[] bits, ref BITMAPINFO info, uint usage);

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
            public int bmiColors;
        }

        private const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;

        /// <summary>The main screen as BGRA pixels (top row first).</summary>
        public static (byte[] Pixels, int Width, int Height) Capture()
        {
            int w = GetSystemMetrics(0), h = GetSystemMetrics(1);
            if (w <= 0 || h <= 0) throw new InvalidOperationException("Couldn't find the screen.");
            IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), bmp = CreateCompatibleBitmap(screen, w, h);
            IntPtr old = SelectObject(mem, bmp);
            try
            {
                if (!BitBlt(mem, 0, 0, w, h, screen, 0, 0, SRCCOPY | CAPTUREBLT)) throw new InvalidOperationException("Windows didn't let me see the screen.");
                SelectObject(mem, old);
                var info = new BITMAPINFO { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32, biCompression = 0 };
                var pixels = new byte[w * h * 4];
                if (GetDIBits(mem, bmp, 0, (uint)h, pixels, ref info, 0) == 0) throw new InvalidOperationException("Couldn't read the screen picture.");
                for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
                return (pixels, w, h);
            }
            finally
            {
                DeleteObject(bmp);
                DeleteDC(mem);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }

        /// <summary>Halves a picture (averages each 2×2 block).</summary>
        private static (byte[], int, int) Half(byte[] p, int w, int h)
        {
            int nw = w / 2, nh = h / 2;
            var o = new byte[nw * nh * 4];
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                    for (int c = 0; c < 4; c++)
                    {
                        int a = ((2 * y) * w + 2 * x) * 4 + c, b = a + 4, d = a + w * 4, e = d + 4;
                        o[(y * nw + x) * 4 + c] = (byte)((p[a] + p[b] + p[d] + p[e]) / 4);
                    }
            return (o, nw, nh);
        }

        /// <summary>Is Windows' OCR available (it needs a language pack with OCR, which English Windows has)?</summary>
        public static bool Available
        {
            get { try { return Engine() != null; } catch { return false; } }
        }

        private static OcrEngine? Engine() =>
            OcrEngine.TryCreateFromUserProfileLanguages() ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));

        /// <summary>Reads the text on the screen right now.</summary>
        public static async Task<ScreenText> ReadAsync()
        {
            var engine = Engine() ?? throw new InvalidOperationException("Windows' text reader (OCR) isn't installed. Add the English language pack's OCR in Settings › Time & language.");
            string app = ScreenTime.ForegroundApp() ?? "";
            var (pixels, w, h) = Capture();
            while (Math.Max(w, h) > OcrEngine.MaxImageDimension) (pixels, w, h) = Half(pixels, w, h);
            IBuffer buffer = CryptographicBuffer.CreateFromByteArray(pixels);
            using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(buffer, BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Premultiplied);
            var result = await engine.RecognizeAsync(bitmap);
            var st = new ScreenText { App = app, Language = engine.RecognizerLanguage?.DisplayName ?? "" };
            foreach (var line in result.Lines)
            {
                string text = line.Text.Trim();
                if (text.Length == 0) continue;
                st.Lines.Add(text);
                st.Words += line.Words.Count;
            }
            return st;
        }

        /// <summary>A short summary of what's on screen: the app, how much text, and the first lines that look like sentences.</summary>
        public static string Summary(ScreenText s)
        {
            if (s.Lines.Count == 0) return $"I can't see any text on your screen{(s.App.Length > 0 ? " (" + s.App + " is in front)" : "")}.";
            var sentences = s.Lines.Where(l => l.Split(' ').Length >= 4 && Regex.IsMatch(l, @"[a-z]")).Take(2).ToList();
            string intro = $"I can read {Composer.Plural(s.Words, "word", "words")} on your screen{(s.App.Length > 0 ? ", in " + s.App : "")}.";
            if (sentences.Count == 0) return intro + " It's mostly short labels, like " + Composer.List(s.Lines.Take(3).Select(l => "\"" + AceV4Brain.Short(l, 30) + "\"").ToList()) + ".";
            return intro + " It starts with: \"" + AceV4Brain.Short(string.Join(" ", sentences), 160) + "\"";
        }
    }
}
