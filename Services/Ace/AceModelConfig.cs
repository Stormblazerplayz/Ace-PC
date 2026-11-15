using System;
using System.IO;
using System.Text.Json;

namespace Nimbus.Services.Ace
{
    /// <summary>
    /// Ace's model settings. Saved to %LOCALAPPDATA%\Nimbus\ace-model.json so the GPU/CPU pick sticks.
    /// Every path has a default that matches what Setup-LFM3Omni.ps1 downloads, so the file is optional.
    /// </summary>
    public sealed class AceModelConfig
    {
        public static string Folder { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nimbus");

        public static string ConfigPath => Path.Combine(Folder, "ace-model.json");

        // ---- LFM3-Omni-2B through llama.cpp ----
        /// <summary>Folder that holds llama-server.exe (or llama.exe). Searched one level deep too.</summary>
        public string LlamaFolder { get; set; } = Path.Combine(Folder, "llama");
        public string ModelPath { get; set; } = Path.Combine(Folder, "models", "LFM3-Omni-2B-Q4_K_M.gguf");
        public string MmprojPath { get; set; } = Path.Combine(Folder, "models", "mmproj-LFM3-Omni-2B-Q8_0.gguf");
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 8090;
        /// <summary>The model can do 32K. 8K keeps memory low and is plenty for chat.</summary>
        public int ContextSize { get; set; } = 8192;
        /// <summary>True = every layer on the GPU. False = CPU and RAM only.</summary>
        public bool UseGpu { get; set; } = true;
        public int MaxReplyTokens { get; set; } = 1024;
        /// <summary>Unload Ollama models when Omni starts on the GPU, so the two don't fight over 6 GB of VRAM.</summary>
        public bool FreeOllamaVramOnStart { get; set; } = true;
        /// <summary>Seconds to wait for llama.cpp to load the model.</summary>
        public int StartTimeoutSeconds { get; set; } = 120;

        // ---- Fallback: Qwen3.5 4B through Ollama ----
        public string OllamaUrl { get; set; } = "http://127.0.0.1:11434";
        public string FallbackModel { get; set; } = "qwen3.5:4b";

        // ---- Personality ----
        public string SystemPrompt { get; set; } =
            "You are Ace, the assistant inside Nimbus, a desktop app on Storm's Windows PC. " +
            "Be friendly, quick and clear. Keep answers short unless asked for more.";
        /// <summary>female or male. Used for spoken replies.</summary>
        public string Voice { get; set; } = "female";

        public string BaseUrl => $"http://{Host}:{Port}";

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static AceModelConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                    return JsonSerializer.Deserialize<AceModelConfig>(File.ReadAllText(ConfigPath), JsonOptions) ?? new AceModelConfig();
            }
            catch { /* a broken file just means defaults */ }
            return new AceModelConfig();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOptions));
            }
            catch { /* saving is nice to have, never fatal */ }
        }
    }
}
