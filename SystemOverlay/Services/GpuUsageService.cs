using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace SystemOverlay.Services;

/// <summary>
/// Queries real per-process GPU usage % via D3DKMT kernel API.
/// Same method as Windows Task Manager — universal for AMD/NVIDIA/Intel.
/// Requires Windows 10 1803+ with WDDM 2.0+ drivers.
/// </summary>
public static class GpuUsageService
{
    #region P/Invoke

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTEnumAdapters2(ref ENUM_ADAPTERS2 pEnum);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTQueryStatistics(IntPtr pQuery);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    #endregion

    #region Native Structs

    [StructLayout(LayoutKind.Sequential)]
    private struct ENUM_ADAPTERS2
    {
        public uint NumAdapters;
        public IntPtr pAdapters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ADAPTER_INFO
    {
        public uint hAdapter;
        public uint LuidLow;
        public int LuidHigh;
        public uint NumOfSources;
        public int bPresentMoveRegionsPreferred;
    }

    #endregion

    #region D3DKMT_QUERYSTATISTICS Offsets (x64, WDDM 2.0+)
    //
    // struct D3DKMT_QUERYSTATISTICS {
    //   [0]  UINT32 Type
    //   [4]  LUID   { LowPart(4), HighPart(4) }
    //   [12] padding(4) for 8-byte alignment
    //   [16] HANDLE hProcess (8 bytes via D3DKMT_PTR)
    //   [24] QueryResult union (also contains input fields)
    //        ADAPTER:      [24] NbSegments, [28] NodeCount
    //        PROCESS_NODE: [24] NodeId(input), [28] padding, [32] RunningTime(output)
    // }

    private const int BUF = 1024;
    private const int O_TYPE = 0;
    private const int O_LUID_LO = 4;
    private const int O_LUID_HI = 8;
    private const int O_HPROC = 16;
    private const int O_INPUT = 24;
    private const int O_NODE_CNT = 28;
    private const int O_RUN_TIME = 32;

    private const int T_ADAPTER = 0;
    private const int T_PROCESS_NODE = 5;

    #endregion

    #region State

    private record struct GpuAdapter(uint LuidLow, int LuidHigh, uint NodeCount);

    private static GpuAdapter[] _adapters = [];
    private static IntPtr _buf = IntPtr.Zero;
    private static readonly byte[] _zero = new byte[BUF];
    private static Dictionary<int, long> _prev = new();
    private static long _prevTicks;
    private static bool _ok;
    private static bool _failed;

    #endregion

    public static bool IsAvailable => _ok;

    public static bool Initialize()
    {
        if (_ok) return true;
        if (_failed) return false;

        try
        {
            _buf = Marshal.AllocHGlobal(BUF);

            // Pass 1: query how many adapters exist
            var e = new ENUM_ADAPTERS2 { NumAdapters = 0, pAdapters = IntPtr.Zero };
            int hr = D3DKMTEnumAdapters2(ref e);

            // STATUS_BUFFER_TOO_SMALL (0xC0000023) is expected — it fills NumAdapters
            if (e.NumAdapters == 0)
            {
                Debug.WriteLine($"[D3DKMT] EnumAdapters2 pass1: hr=0x{hr:X8} count=0 — no adapters");
                _failed = true;
                return false;
            }

            Debug.WriteLine($"[D3DKMT] EnumAdapters2 pass1: hr=0x{hr:X8} count={e.NumAdapters}");

            // Pass 2: allocate buffer and fetch adapter info
            int sz = Marshal.SizeOf<ADAPTER_INFO>();
            e.pAdapters = Marshal.AllocHGlobal((int)(e.NumAdapters * sz));
            try
            {
                hr = D3DKMTEnumAdapters2(ref e);
                Debug.WriteLine($"[D3DKMT] EnumAdapters2 pass2: hr=0x{hr:X8} count={e.NumAdapters}");
                if (hr != 0) { _failed = true; return false; }

                var list = new List<GpuAdapter>();
                for (int i = 0; i < e.NumAdapters; i++)
                {
                    var info = Marshal.PtrToStructure<ADAPTER_INFO>(e.pAdapters + i * sz);
                    uint nodes = QueryNodeCount(info.LuidLow, info.LuidHigh);
                    Debug.WriteLine($"[D3DKMT] Adapter[{i}] LUID=0x{info.LuidLow:X}:{info.LuidHigh} nodes={nodes}");
                    if (nodes > 0 && nodes <= 64)
                    {
                        list.Add(new GpuAdapter(info.LuidLow, info.LuidHigh, nodes));
                    }
                }

                _adapters = list.ToArray();
                _ok = _adapters.Length > 0;
                _prevTicks = Stopwatch.GetTimestamp();
                if (!_ok) _failed = true;

                Debug.WriteLine($"[D3DKMT] Init complete: {_adapters.Length} valid adapters, ready={_ok}");
                return _ok;
            }
            finally
            {
                Marshal.FreeHGlobal(e.pAdapters);
            }
        }
        catch (DllNotFoundException)
        {
            Debug.WriteLine("[D3DKMT] gdi32 D3DKMT functions not found");
            _failed = true; return false;
        }
        catch (EntryPointNotFoundException)
        {
            Debug.WriteLine("[D3DKMT] D3DKMTEnumAdapters2 not available (old Windows?)");
            _failed = true; return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[D3DKMT] Init error: {ex.Message}");
            _failed = true; return false;
        }
    }

    private static uint QueryNodeCount(uint lo, int hi)
    {
        Clear();
        Marshal.WriteInt32(_buf, O_TYPE, T_ADAPTER);
        Marshal.WriteInt32(_buf, O_LUID_LO, (int)lo);
        Marshal.WriteInt32(_buf, O_LUID_HI, hi);
        Marshal.WriteInt64(_buf, O_HPROC, 0);

        if (D3DKMTQueryStatistics(_buf) != 0) return 0;
        return (uint)Marshal.ReadInt32(_buf, O_NODE_CNT);
    }

    /// <summary>
    /// Returns top GPU-consuming processes with real GPU% (delta-based).
    /// First call returns empty (needs two samples to compute delta).
    /// </summary>
    public static List<(string Name, float Percent)> GetTopProcesses(int count = 3)
    {
        if (!_ok) return [];

        long now = Stopwatch.GetTimestamp();
        double elapsed = (double)(now - _prevTicks) / Stopwatch.Frequency;
        if (elapsed < 0.1) return [];

        // Collect GPU running time for all user processes
        var current = new Dictionary<int, (string Name, long Time)>();
        var procs = Process.GetProcesses();
        try
        {
            foreach (var p in procs)
            {
                try
                {
                    if (p.Id <= 4) continue;
                    long t = GetProcessGpuTime(p.Id);
                    if (t > 0) current[p.Id] = (p.ProcessName, t);
                }
                catch { }
            }
        }
        finally
        {
            foreach (var p in procs) p.Dispose();
        }

        // Calculate GPU% from delta
        var results = new List<(string Name, float Percent)>();
        foreach (var (pid, (name, gpuTime)) in current)
        {
            if (_prev.TryGetValue(pid, out var prevTime) && gpuTime > prevTime)
            {
                // RunningTime is in 100ns units → seconds = value / 10_000_000
                double deltaSec = (gpuTime - prevTime) / 10_000_000.0;
                float pct = (float)(deltaSec / elapsed * 100.0);
                if (pct > 0.01f) // Low threshold to catch even light GPU use
                {
                    string trimmed = name.Length > 20 ? name[..20] + "…" : name;
                    results.Add((trimmed, Math.Min(pct, 100f)));
                }
            }
        }

        _prev = current.ToDictionary(kv => kv.Key, kv => kv.Value.Time);
        _prevTicks = now;

        Debug.WriteLine($"[D3DKMT] GetTopProcesses: {current.Count} with GPU time, {results.Count} with delta > 0");
        return results.OrderByDescending(r => r.Percent).Take(count).ToList();
    }

    private static long GetProcessGpuTime(int pid)
    {
        IntPtr ph = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (ph == IntPtr.Zero) return 0;

        try
        {
            long total = 0;
            long hProc = ph.ToInt64();

            foreach (var a in _adapters)
            {
                for (uint n = 0; n < a.NodeCount; n++)
                {
                    Clear();
                    Marshal.WriteInt32(_buf, O_TYPE, T_PROCESS_NODE);
                    Marshal.WriteInt32(_buf, O_LUID_LO, (int)a.LuidLow);
                    Marshal.WriteInt32(_buf, O_LUID_HI, a.LuidHigh);
                    Marshal.WriteInt64(_buf, O_HPROC, hProc);
                    Marshal.WriteInt32(_buf, O_INPUT, (int)n);

                    if (D3DKMTQueryStatistics(_buf) == 0)
                        total += Marshal.ReadInt64(_buf, O_RUN_TIME);
                }
            }
            return total;
        }
        finally
        {
            CloseHandle(ph);
        }
    }

    private static void Clear() => Marshal.Copy(_zero, 0, _buf, BUF);

    public static void Cleanup()
    {
        if (_buf != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buf);
            _buf = IntPtr.Zero;
        }
    }
}
