using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using TelaRec.Core;

namespace TelaRec.Native;

/// <summary>Detecta todos os monitores ativos via Win32 (sem limite de quantidade).</summary>
internal static class MonitorEnumerator
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT lprc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc cb, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    public static List<MonitorInfo> GetAll()
    {
        var raw = new List<(string Dev, RECT R, bool Primary, int Dpi)>();

        MonitorEnumProc cb = (IntPtr h, IntPtr hdc, ref RECT rc, IntPtr l) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(h, ref mi))
            {
                int dpi = 100;
                try
                {
                    if (GetDpiForMonitor(h, 0, out uint dx, out _) == 0 && dx > 0)
                        dpi = (int)Math.Round(dx * 100.0 / 96.0);
                }
                catch { /* mantém 100% */ }
                raw.Add((mi.szDevice, mi.rcMonitor, (mi.dwFlags & 1) != 0, dpi));
            }
            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
        GC.KeepAlive(cb);

        // Numeração igual à das configurações de vídeo do Windows (\\.\DISPLAYn).
        var withNumbers = new List<(int Num, string Dev, RECT R, bool Primary, int Dpi)>();
        var used = new HashSet<int>();
        int fallback = 1;
        foreach (var r in raw)
        {
            var match = Regex.Match(r.Dev, @"DISPLAY(\d+)", RegexOptions.IgnoreCase);
            int n = match.Success ? int.Parse(match.Groups[1].Value) : 0;
            if (n <= 0 || !used.Add(n))
            {
                while (!used.Add(fallback)) fallback++;
                n = fallback;
            }
            withNumbers.Add((n, r.Dev, r.R, r.Primary, r.Dpi));
        }

        return withNumbers
            .OrderBy(x => x.Num)
            .Select(x => new MonitorInfo(x.Num, x.Dev, x.R.Left, x.R.Top,
                x.R.Right - x.R.Left, x.R.Bottom - x.R.Top, x.Primary, x.Dpi))
            .ToList();
    }
}
