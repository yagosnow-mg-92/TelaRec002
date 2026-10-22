using System.Runtime.InteropServices;

namespace TelaRec.Native;

/// <summary>
/// Hook global de mouse (somente leitura: não bloqueia nem altera cliques). Fica instalado apenas enquanto
/// a gravação está ativa e o efeito de clique ligado. Posição em pixels físicos (o app é PerMonitorV2).
/// </summary>
internal sealed class MouseHook : IDisposable
{
    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    private const int WH_MOUSE_LL = 14, WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204;

    private readonly LowLevelMouseProc _proc;
    private IntPtr _hook = IntPtr.Zero;

    /// <summary>x, y (pixels físicos do desktop virtual), isLeft.</summary>
    public event Action<int, int, bool>? Click;

    public MouseHook() { _proc = Callback; }

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException("o Windows recusou o hook de mouse (erro " + Marshal.GetLastWin32Error() + ")");
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN)
            {
                var d = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                try { Click?.Invoke(d.pt.X, d.pt.Y, msg == WM_LBUTTONDOWN); } catch { }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();
}
