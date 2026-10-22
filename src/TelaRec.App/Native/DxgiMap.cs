using System.Runtime.InteropServices;
using TelaRec.Core;

namespace TelaRec.Native;

/// <summary>
/// Descobre, para cada monitor do Windows, o par (adaptador, saída) DXGI correspondente.
/// É esse par que o capturador por Desktop Duplication usa para gravar SOMENTE aquele monitor.
/// </summary>
internal static class DxgiMap
{
    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_OUTPUT_DESC
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public RECT DesktopCoordinates;
        public int AttachedToDesktop;
        public int Rotation;
        public IntPtr Monitor;
    }

    // Os métodos _0.._n são apenas marcadores de posição na vtable (IDXGIObject etc.).
    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        [PreserveSig] int _0(); [PreserveSig] int _1(); [PreserveSig] int _2(); [PreserveSig] int _3();
        [PreserveSig] int _4(); [PreserveSig] int _5(); [PreserveSig] int _6(); [PreserveSig] int _7();
        [PreserveSig] int _8();
        [PreserveSig] int EnumAdapters1(uint index, out IntPtr adapter);
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        [PreserveSig] int _0(); [PreserveSig] int _1(); [PreserveSig] int _2(); [PreserveSig] int _3();
        [PreserveSig] int EnumOutputs(uint index, out IntPtr output);
    }

    [ComImport, Guid("ae02eedb-c735-4690-8d52-5a8dc20213aa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIOutput
    {
        [PreserveSig] int _0(); [PreserveSig] int _1(); [PreserveSig] int _2(); [PreserveSig] int _3();
        [PreserveSig] int GetDesc(out DXGI_OUTPUT_DESC desc);
    }

    private static Dictionary<string, (int Adapter, int Output)> Build()
    {
        var map = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);
        var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        if (CreateDXGIFactory1(ref iid, out var fp) < 0 || fp == IntPtr.Zero) return map;

        var factory = (IDXGIFactory1)Marshal.GetObjectForIUnknown(fp);
        Marshal.Release(fp);
        try
        {
            for (uint a = 0; ; a++)
            {
                if (factory.EnumAdapters1(a, out var ap) < 0 || ap == IntPtr.Zero) break;
                var adapter = (IDXGIAdapter1)Marshal.GetObjectForIUnknown(ap);
                Marshal.Release(ap);
                try
                {
                    for (uint o = 0; ; o++)
                    {
                        if (adapter.EnumOutputs(o, out var op) < 0 || op == IntPtr.Zero) break;
                        var output = (IDXGIOutput)Marshal.GetObjectForIUnknown(op);
                        Marshal.Release(op);
                        try
                        {
                            if (output.GetDesc(out var d) >= 0 && d.AttachedToDesktop != 0)
                                map[d.DeviceName] = ((int)a, (int)o);
                        }
                        finally { Marshal.ReleaseComObject(output); }
                    }
                }
                finally { Marshal.ReleaseComObject(adapter); }
            }
        }
        finally { Marshal.ReleaseComObject(factory); }
        return map;
    }

    public static List<MonitorInfo> Annotate(List<MonitorInfo> monitors)
    {
        Dictionary<string, (int Adapter, int Output)> map;
        try { map = Build(); } catch { map = new(); }
        return monitors
            .Select(m => map.TryGetValue(m.DeviceName, out var v)
                ? m with { AdapterIndex = v.Adapter, OutputIndex = v.Output }
                : m)
            .ToList();
    }
}
