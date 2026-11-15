using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace Nimbus.Services
{
    /// <summary>
    /// Live numbers for the Glance and Memory tabs: CPU, RAM, committed memory, disk,
    /// network, plus real CPU/GPU temps, GPU load and fan speed from LibreHardwareMonitor.
    /// Sample() runs on a background thread; Hub raises PropertyChanged on the UI thread.
    /// </summary>
    public sealed class SystemStats : INotifyPropertyChanged, IDisposable
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        // ---------- CPU / memory ----------
        public double Cpu { get; private set; }
        public string CpuText => Cpu.ToString("0");

        public double MemTotalGb { get; private set; }
        public double MemFreeGb { get; private set; }
        public double MemUsedGb => MemTotalGb - MemFreeGb;
        public double MemPercent => MemTotalGb > 0 ? MemUsedGb / MemTotalGb * 100 : 0;
        public string MemText => MemPercent.ToString("0");
        public string RamLine => $"{MemUsedGb:0.0} GB of {MemTotalGb:0.0} GB in use, {MemFreeGb:0.0} GB free";

        /// <summary>Committed = what Windows has promised to apps (RAM + page file).</summary>
        public double CommitUsedGb { get; private set; }
        public double CommitLimitGb { get; private set; }
        public double CommitPercent => CommitLimitGb > 0 ? CommitUsedGb / CommitLimitGb * 100 : 0;
        public bool CommitHigh => CommitPercent >= 80;
        public string CommitLine => $"{CommitUsedGb:0.0} GB of {CommitLimitGb:0.0} GB · {CommitPercent:0}%";

        // ---------- sensors (null = not reported) ----------
        public double? Gpu { get; private set; }
        public double? CpuTemp { get; private set; }
        public double? GpuTemp { get; private set; }
        public double? FanRpm { get; private set; }
        public bool SensorsReady { get; private set; }

        public string GpuText => Gpu.HasValue ? Gpu.Value.ToString("0") : "–";
        public double GpuPercent => Gpu ?? 0;
        public string CpuTempText => CpuTemp.HasValue ? CpuTemp.Value.ToString("0") : "–";
        public double CpuTempPercent => CpuTemp ?? 0;
        public bool CpuHot => CpuTemp >= 80;
        public string GpuTempText => GpuTemp.HasValue ? GpuTemp.Value.ToString("0") : "–";
        public double GpuTempPercent => GpuTemp ?? 0;
        public string FanText => FanRpm.HasValue ? FanRpm.Value.ToString("N0") : "–";
        public double FanPercent => FanRpm.HasValue ? Math.Min(100, FanRpm.Value / 30) : 0;   // 3000 rpm = full bar
        /// <summary>Shown on the temp tiles when sensors can't be read (usually: run as admin).</summary>
        public string SensorFlag => SensorsReady && CpuTemp.HasValue ? "" : "Admin";

        // ---------- disk / network / uptime ----------
        public double DiskFreeGb { get; private set; }
        public double DiskTotalGb { get; private set; }
        public string DiskText => DiskFreeGb.ToString("0");
        public double DiskUsedPercent => DiskTotalGb > 0 ? (1 - DiskFreeGb / DiskTotalGb) * 100 : 0;
        public bool DiskLow => DiskTotalGb > 0 && DiskFreeGb < 50;
        public string DiskFlag => DiskLow ? "Low" : "";

        public double DownMBps { get; private set; }
        public double UpMBps { get; private set; }
        public string DownText => DownMBps.ToString(DownMBps < 10 ? "0.0" : "0");
        public string NetText => $"↓ {DownText} MB/s  ↑ {UpMBps:0.0}";
        public string Uptime { get; private set; } = "";

        /// <summary>Last 40 samples, newest last.</summary>
        public IReadOnlyList<double> DownHistory { get { lock (_down) return _down.ToArray(); } }
        public IReadOnlyList<double> FreeHistory { get { lock (_free) return _free.ToArray(); } }

        private readonly Queue<double> _down = new(), _free = new();
        private ulong _lastIdle, _lastKernel, _lastUser;
        private long _lastRx, _lastTx;
        private DateTime _lastNet = DateTime.MinValue;
        private Computer? _computer;
        private readonly object _gate = new();

        public SystemStats()
        {
            for (int i = 0; i < 40; i++) { _down.Enqueue(0); _free.Enqueue(0); }
        }

        /// <summary>Opens LibreHardwareMonitor. Slow (a second or two), so call it off the UI thread.</summary>
        public void OpenSensors()
        {
            try
            {
                var c = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMotherboardEnabled = true, IsControllerEnabled = true };
                c.Open();
                lock (_gate) _computer = c;
                SensorsReady = true;
            }
            catch
            {
                SensorsReady = false;   // no admin rights or no driver: tiles show "–" and an Admin flag
            }
        }

        /// <summary>Takes one reading. Call about once a second from a background thread.</summary>
        public void Sample()
        {
            SampleCpu();
            SampleMemory();
            SampleDisk();
            SampleNetwork();
            SampleSensors();
            var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
            Uptime = up.TotalDays >= 1 ? $"up {(int)up.TotalDays}d {up.Hours}h" : $"up {up.Hours}h {up.Minutes}m";
        }

        /// <summary>Tells every binding to re-read (call on the UI thread).</summary>
        public void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

        private void SampleCpu()
        {
            if (!GetSystemTimes(out var idleFt, out var kernelFt, out var userFt)) return;
            ulong idle = idleFt.Value, kernel = kernelFt.Value, user = userFt.Value;
            if (_lastKernel != 0)
            {
                ulong total = (kernel - _lastKernel) + (user - _lastUser);   // kernel time includes idle time
                ulong idleD = idle - _lastIdle;
                if (total > 0) Cpu = Math.Max(0, Math.Min(100, (1.0 - (double)idleD / total) * 100));
            }
            _lastIdle = idle; _lastKernel = kernel; _lastUser = user;
        }

        private void SampleMemory()
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref m)) return;
            const double gb = 1024.0 * 1024 * 1024;
            MemTotalGb = m.ullTotalPhys / gb;
            MemFreeGb = m.ullAvailPhys / gb;
            CommitLimitGb = m.ullTotalPageFile / gb;
            CommitUsedGb = (m.ullTotalPageFile - m.ullAvailPageFile) / gb;
            Push(_free, MemFreeGb);
        }

        private void SampleDisk()
        {
            try
            {
                var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
                var d = new DriveInfo(root);
                const double gb = 1000.0 * 1000 * 1000;   // drives are sold in "1000" gigabytes, like Explorer shows
                DiskFreeGb = d.AvailableFreeSpace / gb;
                DiskTotalGb = d.TotalSize / gb;
            }
            catch { /* drive not ready */ }
        }

        private void SampleNetwork()
        {
            long rx = 0, tx = 0;
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                    var s = ni.GetIPStatistics();
                    rx += s.BytesReceived;
                    tx += s.BytesSent;
                }
            }
            catch { return; }
            var now = DateTime.UtcNow;
            if (_lastNet != DateTime.MinValue)
            {
                double secs = Math.Max(0.2, (now - _lastNet).TotalSeconds);
                DownMBps = Math.Max(0, (rx - _lastRx) / secs / 1_000_000);
                UpMBps = Math.Max(0, (tx - _lastTx) / secs / 1_000_000);
            }
            _lastRx = rx; _lastTx = tx; _lastNet = now;
            Push(_down, DownMBps);
        }

        private void SampleSensors()
        {
            lock (_gate)
            {
                if (_computer == null) return;
                double? cpuT = null, gpuT = null, gpuL = null, fan = null;
                try
                {
                    foreach (var hw in _computer.Hardware)
                    {
                        hw.Update();
                        foreach (var sub in hw.SubHardware) sub.Update();
                        var sensors = hw.Sensors.Concat(hw.SubHardware.SelectMany(s => s.Sensors)).ToList();
                        bool isGpu = hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;

                        if (hw.HardwareType == HardwareType.Cpu)
                            cpuT ??= Pick(sensors, SensorType.Temperature, "Package", "Tctl", "Tdie", "Core Max", "Core Average", "");
                        if (isGpu)
                        {
                            gpuT ??= Pick(sensors, SensorType.Temperature, "GPU Core", "GPU Hot Spot", "");
                            gpuL ??= Pick(sensors, SensorType.Load, "GPU Core", "D3D 3D", "");
                        }
                        foreach (var s in sensors)
                            if (s.SensorType == SensorType.Fan && s.Value is float rpm && rpm > 0)
                                fan = Math.Max(fan ?? 0, rpm);
                    }
                }
                catch { /* a sensor went away; keep the last good values */ return; }
                CpuTemp = cpuT; GpuTemp = gpuT; Gpu = gpuL; FanRpm = fan;
            }
        }

        /// <summary>First sensor of a type whose name contains one of the hints, in order ("" = any).</summary>
        private static double? Pick(List<ISensor> sensors, SensorType type, params string[] hints)
        {
            foreach (var hint in hints)
                foreach (var s in sensors)
                    if (s.SensorType == type && s.Value.HasValue && s.Value.Value > 0 &&
                        s.Name.Contains(hint, StringComparison.OrdinalIgnoreCase))
                        return s.Value.Value;
            return null;
        }

        private static void Push(Queue<double> q, double v)
        {
            lock (q)
            {
                q.Enqueue(v);
                while (q.Count > 40) q.Dequeue();
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                try { _computer?.Close(); } catch { }
                _computer = null;
            }
        }

        // ---------- Windows API ----------
        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint Low, High;
            public ulong Value => ((ulong)High << 32) | Low;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
    }
}
