using System;
using System.IO;

namespace Nimbus.Services.Ace
{
    /// <summary>One message in Ace's chat. Images and audio ride along only on the newest user turn.</summary>
    public sealed class ChatTurn
    {
        public string Role { get; init; } = "user";   // "user" or "assistant"
        public string Text { get; set; } = "";

        /// <summary>Raw image bytes (png, jpg, webp, gif, bmp).</summary>
        public byte[]? Image { get; set; }
        public string ImageMime { get; set; } = "image/png";

        /// <summary>Raw audio bytes (wav or mp3). The model hears it at 16 kHz.</summary>
        public byte[]? Audio { get; set; }
        public string AudioFormat { get; set; } = "wav";

        public bool HasMedia => Image != null || Audio != null;

        public static ChatTurn User(string text) => new() { Role = "user", Text = text };
        public static ChatTurn Assistant(string text) => new() { Role = "assistant", Text = text };

        /// <summary>A text-only copy for history, so old pictures and clips don't eat the context window.</summary>
        public ChatTurn WithoutMedia()
        {
            string note = Image != null ? "[sent a picture] " : "";
            if (Audio != null) note += "[sent an audio clip] ";
            return new ChatTurn { Role = Role, Text = (note + Text).Trim() };
        }

        /// <summary>Attach a file by extension. Returns false for types Ace can't read.</summary>
        public bool TryAttach(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string? imageMime = ext switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                _ => null,
            };
            if (imageMime != null)
            {
                Image = File.ReadAllBytes(path);
                ImageMime = imageMime;
                return true;
            }
            if (ext is ".wav" or ".mp3")
            {
                Audio = File.ReadAllBytes(path);
                AudioFormat = ext.TrimStart('.');
                return true;
            }
            return false;
        }
    }

    /// <summary>What came back, and which brain answered.</summary>
    public sealed record AceReply(string Text, string Engine, bool UsedFallback);
}
