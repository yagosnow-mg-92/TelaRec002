using System.IO;
using System.Media;
using System.Text;

namespace TelaRec.Services;

/// <summary>Dois "cliques" sonoros diferentes, gerados em memória (sem arquivos de áudio).</summary>
internal static class ClickSounds
{
    private static SoundPlayer? _left, _right;

    public static void Play(bool left)
    {
        try
        {
            var p = left ? (_left ??= Make(1500, 45)) : (_right ??= Make(750, 80));
            p.Play();
        }
        catch { /* sem dispositivo de áudio: ignora */ }
    }

    private static SoundPlayer Make(double hz, int ms)
    {
        var p = new SoundPlayer(new MemoryStream(BuildWav(hz, ms)));
        p.Load();
        return p;
    }

    private static byte[] BuildWav(double hz, int ms)
    {
        const int rate = 44100;
        int n = rate * ms / 1000;
        int dataLen = n * 2;
        using var m = new MemoryStream();
        using var w = new BinaryWriter(m);
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataLen);
        w.Write(Encoding.ASCII.GetBytes("WAVE")); w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2);
        w.Write((short)2); w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataLen);
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / rate;
            double attack = Math.Min(1.0, i / (rate * 0.002));   // 2 ms para evitar estalo
            double env = attack * Math.Exp(-t * (hz > 1000 ? 70 : 40));
            double v = Math.Sin(2 * Math.PI * hz * t) * 0.6 + Math.Sin(2 * Math.PI * hz * 2 * t) * 0.15;
            w.Write((short)(v * env * 0.6 * short.MaxValue));
        }
        w.Flush();
        return m.ToArray();
    }
}
