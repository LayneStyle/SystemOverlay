using System.Diagnostics;

namespace SystemOverlay.Services;

public static class ProcessMonitorService
{
public record ProcessUsage(string Name, float Value, string Unit = "MB")
    {
        public string DisplayValue => Unit == "%" ? $"{Value:F1} %" : $"{Value:F0} MB";
    }

    // System processes to exclude from GPU process list (not user-facing apps)
    private static readonly HashSet<string> _systemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "svchost", "System", "Idle", "csrss", "smss", "wininit", "services",
        "lsass", "winlogon", "fontdrvhost", "dwm", "conhost", "RuntimeBroker",
        "SearchHost", "StartMenuExperienceHost", "ShellExperienceHost",
        "SystemOverlay", "TextInputHost", "ctfmon", "dllhost", "sihost",
        "taskhostw", "explorer", "SecurityHealthSystray", "spoolsv",
        "WmiPrvSE", "audiodg", "CompPkgSrv", "MsMpEng"
    };

    // CPU% calculation: requires two samples to compute delta
    private static Dictionary<int, (double CpuMs, DateTime Time)> _prevCpuSnapshot = new();
    private static readonly int _cpuCoreCount = Environment.ProcessorCount;

    public static List<ProcessUsage> GetTopCpuProcesses(int count = 3)
    {
        try
        {
            var snap = Process.GetProcesses();
            var now = DateTime.UtcNow;
            var currentSnapshot = new Dictionary<int, (string Name, double CpuMs)>();

            try
            {
                foreach (var p in snap)
                {
                    try
                    {
                        if (p.Id <= 4) continue;
                        currentSnapshot[p.Id] = (TrimName(p.ProcessName), p.TotalProcessorTime.TotalMilliseconds);
                    }
                    catch { /* access denied for some processes */ }
                }
            }
            finally
            {
                foreach (var p in snap) p.Dispose();
            }

            // First call — no previous data, just store and return empty
            if (_prevCpuSnapshot.Count == 0)
            {
                _prevCpuSnapshot = currentSnapshot.ToDictionary(
                    k => k.Key, v => (v.Value.CpuMs, now));
                return [];
            }

            // Calculate CPU% per process
            var results = new List<ProcessUsage>();
            var prevTime = _prevCpuSnapshot.Values.FirstOrDefault().Time;
            var elapsedMs = (now - prevTime).TotalMilliseconds;

            if (elapsedMs < 100) // Too short interval, skip
            {
                return [];
            }

            foreach (var (pid, (name, cpuMs)) in currentSnapshot)
            {
                if (_prevCpuSnapshot.TryGetValue(pid, out var prev))
                {
                    var deltaCpu = cpuMs - prev.CpuMs;
                    if (deltaCpu > 0)
                    {
                        // CPU% = (delta cpu time / elapsed wall time / cores) * 100
                        var cpuPercent = (float)(deltaCpu / elapsedMs / _cpuCoreCount * 100.0);
                        if (cpuPercent > 0.1f) // Filter noise
                            results.Add(new ProcessUsage(name, cpuPercent, "%"));
                    }
                }
            }

            // Store current as previous for next poll
            _prevCpuSnapshot = currentSnapshot.ToDictionary(
                k => k.Key, v => (v.Value.CpuMs, now));

            return results
                .OrderByDescending(p => p.Value)
                .Take(count)
                .ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemOverlay] GetTopCpuProcesses error: {ex.Message}");
            return [];
        }
    }

    public static List<ProcessUsage> GetTopRamProcesses(int count = 3)
    {
        try
        {
            var snap = Process.GetProcesses();
            try
            {
                return snap
                    .Where(p => p.Id != 0)
                    .Select(p =>
                    {
                        try
                        {
                            var mbUsed = p.WorkingSet64 / (1024f * 1024f);
                            return new ProcessUsage(TrimName(p.ProcessName), mbUsed);
                        }
                        catch { return null; }
                    })
                    .Where(p => p != null)
                    .OrderByDescending(p => p!.Value)
                    .Take(count)
                    .ToList()!;
            }
            finally
            {
                foreach (var p in snap) p.Dispose();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemOverlay] GetTopRamProcesses error: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Returns top processes by real GPU usage % via D3DKMT kernel API.
    /// Falls back to memory-based proxy if D3DKMT is unavailable.
    /// </summary>
    public static List<ProcessUsage> GetTopGpuProcesses(int count = 3)
    {
        try
        {
            if (!GpuUsageService.IsAvailable)
            {
                bool init = GpuUsageService.Initialize();
                Debug.WriteLine($"[GPU] D3DKMT Initialize() = {init}");
            }

            if (GpuUsageService.IsAvailable)
            {
                var gpuData = GpuUsageService.GetTopProcesses(count);
                Debug.WriteLine($"[GPU] D3DKMT returned {gpuData.Count} processes");
                return gpuData
                    .Select(g => new ProcessUsage(g.Name, g.Percent, "%"))
                    .ToList();
            }

            // D3DKMT not available (needs Admin) — don't fake it with RAM data
            Debug.WriteLine("[GPU] D3DKMT unavailable — no GPU process data");
            return [];
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemOverlay] GetTopGpuProcesses error: {ex.Message}");
            return [];
        }
    }

    private static string TrimName(string name)
    {
        return name.Length > 20 ? name[..20] + "…" : name;
    }
}


