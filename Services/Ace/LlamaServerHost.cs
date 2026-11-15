using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nimbus.Services.Ace
{
    /// <summary>
    /// Starts and stops llama.cpp's server with LFM3-Omni-2B, so Storm never has to open a terminal.
    /// If a server is already answering on the port (started by hand), Nimbus just uses that one.
    /// </summary>
    public sealed class LlamaServerHost : IDisposable
    {
        private readonly AceModelConfig _config;
        private readonly OmniClient _probe;
        private readonly object _logLock = new();
        private Process? _process;
        private StreamWriter? _log;

        public LlamaServerHost(AceModelConfig config, OmniClient probe)
        {
            _config = config;
            _probe = probe;
        }

        public static string LogPath => Path.Combine(AceModelConfig.Folder, "llama-server.log");

        /// <summary>True when Nimbus started the server (so it can restart or stop it).</summary>
        public bool OwnsServer => _process is { HasExited: false };

        /// <summary>Why Omni can't start, or null if everything it needs is on disk.</summary>
        public string? MissingPiece()
        {
            if (FindServer() == null) return "llama.cpp not found, run Setup-LFM3Omni.ps1";
            if (!File.Exists(_config.ModelPath)) return "model file missing, run Setup-LFM3Omni.ps1";
            if (!File.Exists(_config.MmprojPath)) return "audio/vision file missing, run Setup-LFM3Omni.ps1";
            return null;
        }

        /// <summary>
        /// Finds llama-server.exe, or the newer all-in-one llama.exe that takes "serve".
        /// Returns the exe and the first argument to pass (empty for llama-server).
        /// </summary>
        public (string Exe, string Verb)? FindServer()
        {
            if (!Directory.Exists(_config.LlamaFolder)) return null;
            var folders = new List<string> { _config.LlamaFolder };
            try { folders.AddRange(Directory.GetDirectories(_config.LlamaFolder)); } catch { }

            foreach (string dir in folders)
            {
                string server = Path.Combine(dir, "llama-server.exe");
                if (File.Exists(server)) return (server, "");
            }
            foreach (string dir in folders)
            {
                string all = Path.Combine(dir, "llama.exe");
                if (File.Exists(all)) return (all, "serve");
            }
            return null;
        }

        /// <summary>Makes sure the server is up. Returns null on success, or a reason it couldn't start.</summary>
        public async Task<string?> EnsureRunningAsync(CancellationToken ct = default)
        {
            if (await _probe.IsReadyAsync(ct).ConfigureAwait(false)) return null;   // already up (ours or Storm's)

            if (!OwnsServer)
            {
                string? missing = MissingPiece();
                if (missing != null) return missing;
                var found = FindServer()!.Value;
                StartProcess(found.Exe, found.Verb);
            }

            var deadline = DateTime.UtcNow.AddSeconds(_config.StartTimeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                if (_process is { HasExited: true })
                    return $"llama.cpp stopped while loading (exit code {_process.ExitCode}). See {LogPath}";
                if (await _probe.IsReadyAsync(ct).ConfigureAwait(false)) return null;
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
            return $"LFM3-Omni took longer than {_config.StartTimeoutSeconds} s to load. See {LogPath}";
        }

        /// <summary>The command line, kept public so the setup script and README can show the same thing.</summary>
        public string BuildArguments(string verb)
        {
            var args = new List<string>();
            if (verb.Length > 0) args.Add(verb);
            args.Add("-m"); args.Add(Quote(_config.ModelPath));
            args.Add("--mmproj"); args.Add(Quote(_config.MmprojPath));
            args.Add("--host"); args.Add(_config.Host);
            args.Add("--port"); args.Add(_config.Port.ToString());
            args.Add("-c"); args.Add(_config.ContextSize.ToString());
            args.Add("--reasoning"); args.Add("off");           // LFM3-Omni is a non-reasoning model
            args.Add("--temp"); args.Add("0");                  // greedy text, as Liquid recommends
            args.Add("-ngl"); args.Add(_config.UseGpu ? "all" : "0");
            if (!_config.UseGpu) args.Add("--no-mmproj-offload");   // keep the audio/vision part off the GPU too
            return string.Join(" ", args);
        }

        private void StartProcess(string exe, string verb)
        {
            Directory.CreateDirectory(AceModelConfig.Folder);
            lock (_logLock)
            {
                _log?.Dispose();
                _log = new StreamWriter(LogPath, append: false) { AutoFlush = true };
                _log.WriteLine($"[{DateTime.Now:T}] {exe} {BuildArguments(verb)}");
            }

            var psi = new ProcessStartInfo(exe, BuildArguments(verb))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(exe)!,   // so it finds its CUDA dlls
            };
            _process?.Dispose();   // an earlier run that already exited
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (_, e) => WriteLog(e.Data);
            p.ErrorDataReceived += (_, e) => WriteLog(e.Data);   // llama.cpp logs to stderr; must be drained or it stalls
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            _process = p;
        }

        private void WriteLog(string? line)
        {
            if (line == null) return;
            lock (_logLock) { try { _log?.WriteLine(line); } catch { } }
        }

        /// <summary>Restarts with the current settings (used by the GPU / CPU+RAM switch). False if the server isn't ours.</summary>
        public async Task<string?> RestartAsync(CancellationToken ct = default)
        {
            if (!OwnsServer && await _probe.IsReadyAsync(ct).ConfigureAwait(false))
                return "a llama.cpp server Ace didn't start is using the port, so the switch takes effect after you close it";
            Stop();
            await Task.Delay(300, ct).ConfigureAwait(false);   // let Windows free the port
            return await EnsureRunningAsync(ct).ConfigureAwait(false);
        }

        public void Stop()
        {
            var p = _process;
            _process = null;
            if (p == null) return;
            try { if (!p.HasExited) { p.Kill(entireProcessTree: true); p.WaitForExit(3000); } } catch { }
            p.Dispose();
        }

        /// <summary>The last lines of the server log, for showing in an error bubble.</summary>
        public static string LogTail(int lines = 6)
        {
            try
            {
                using var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                return string.Join(Environment.NewLine, sr.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).TakeLast(lines));
            }
            catch { return ""; }
        }

        private static string Quote(string path) => "\"" + path + "\"";

        public void Dispose()
        {
            Stop();
            lock (_logLock) { _log?.Dispose(); _log = null; }
        }
    }
}
