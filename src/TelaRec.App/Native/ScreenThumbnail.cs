using System.Windows.Media;
using System.Windows.Media.Imaging;
using TelaRec.Core;

namespace TelaRec.Native;

/// <summary>Miniatura barata de um monitor (usada para o usuário reconhecer a tela correta, ao vivo).</summary>
internal static class ScreenThumbnail
{
    public static BitmapSource? Capture(MonitorInfo m, int maxWidth = 480)
    {
        IntPtr screen = IntPtr.Zero, mem = IntPtr.Zero, bmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            if (m.Width <= 0 || m.Height <= 0) return null;
            int w = maxWidth;
            int h = Math.Max(1, (int)((double)m.Height * w / m.Width));
            screen = NativeMethods.GetDC(IntPtr.Zero);
            mem = NativeMethods.CreateCompatibleDC(screen);
            bmp = NativeMethods.CreateCompatibleBitmap(screen, w, h);
            old = NativeMethods.SelectObject(mem, bmp);
            NativeMethods.SetStretchBltMode(mem, 4); // HALFTONE
            NativeMethods.StretchBlt(mem, 0, 0, w, h, screen, m.X, m.Y, m.Width, m.Height, 0x00CC0020); // SRCCOPY
            NativeMethods.SelectObject(mem, old); old = IntPtr.Zero; // GetDIBits exige bitmap fora do DC

            // Lê os pixels crus (BGRA, canal alfa ignorado) — evita a miniatura "transparente/preta".
            var bi = new BITMAPINFOHEADER
            {
                biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32, biCompression = 0,
            };
            var pixels = new byte[w * h * 4];
            int lines = NativeMethods.GetDIBits(screen, bmp, 0, (uint)h, pixels, ref bi, 0);
            if (lines == 0) return null;

            var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, pixels, w * 4);
            src.Freeze();
            return src;
        }
        catch { return null; }
        finally
        {
            if (old != IntPtr.Zero) NativeMethods.SelectObject(mem, old);
            if (bmp != IntPtr.Zero) NativeMethods.DeleteObject(bmp);
            if (mem != IntPtr.Zero) NativeMethods.DeleteDC(mem);
            if (screen != IntPtr.Zero) NativeMethods.ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
