using System.Diagnostics;

namespace SystemOverlay.Services;

public static class ProcessMonitorService
{
    public record ProcessUsage(string Name, float Value);

    public static List<ProcessUsage> GetTopCpuProcesses(int count = 3)
    {
        try
        {
            var processes = Process.GetProcesses()
                .Where(p => p.Id != 0)
                .Select(p =>
                {
                    try
                    {
                        return new { p.ProcessName, Cpu = p.TotalProcessorTime.TotalMilliseconds };
                    }
                    catch { return null; }
                })
                .Where(p => p != null)
                .OrderByDescending(p => p!.Cpu)
                .Take(count)
                .Select(p => new ProcessUsage(TrimName(p!.ProcessName), 0))
                .ToList();

            return processes;
        }
        catch { return []; }
    }

    public static List<ProcessUsage> GetTopRamProcesses(int count = 3)
    {
        try
        {
            return Process.GetProcesses()
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
        catch { return []; }
    }

    private static Dictionary<string, PerformanceCounter> _gpuCounters = new();

    public static List<ProcessUsage> GetTopGpuProcesses(int count = 3)
    {
        try
        {
            var category = new PerformanceCounterCategory("GPU Engine");
            var instances = category.GetInstanceNames();
            var dict = new Dictionary<int, float>();
            var activeInstances = new HashSet<string>();

            foreach (var instance in instances)
            {
                if (instance.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var parts = instance.Split('_');
                        if (parts.Length > 1 && parts[0] == "pid" && int.TryParse(parts[1], out int pid))
                        {
                            activeInstances.Add(instance);

                            if (!_gpuCounters.TryGetValue(instance, out var counter))
                            {
                                counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance);
                                counter.NextValue(); // seed value
                                _gpuCounters[instance] = counter;
                                continue; // Skip first reading as it's just seeding
                            }
                            
                            float val = counter.NextValue();
                            
                            if (val > 0)
                            {
                                if (dict.ContainsKey(pid)) dict[pid] += val;
                                else dict[pid] = val;
                            }
                        }
                    }
                    catch { /* ignore instance errors */ }
                }
            }

            // Cleanup old instances
            var keysToRemove = _gpuCounters.Keys.Where(k => !activeInstances.Contains(k)).ToList();
            foreach (var k in keysToRemove)
            {
                _gpuCounters[k].Dispose();
                _gpuCounters.Remove(k);
            }

            var results = new List<ProcessUsage>();
            foreach (var kvp in dict.OrderByDescending(x => x.Value).Take(count))
            {
                try
                {
                    var p = Process.GetProcessById(kvp.Key);
                    results.Add(new ProcessUsage(TrimName(p.ProcessName), kvp.Value));
                }
                catch { }
            }
            return results;
        }
        catch 
        { 
            // Fallback if counters are not available or broken
            return []; 
        }
    }

    private static string TrimName(string name)
    {
        return name.Length > 20 ? name[..20] + "…" : name;
    }
}
