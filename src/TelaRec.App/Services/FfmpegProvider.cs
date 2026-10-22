using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace TelaRec.Services;

/// <summary>Extrai o FFmpeg embutido no .exe para %LOCALAPPDATA%\TelaRec\bin (uma única vez, sem admin).</summary>
internal static class FfmpegProvider
{
    public static (string? Path, string? Error) Ensure()
    {
        try
        {
            // Atalho para desenvolvimento/portabilidade: ffmpeg.exe ao lado do TelaRec.exe tem prioridade.
            var side = System.IO.Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
            if (File.Exists(side)) return (side, null);

            using var rs = Assembly.GetExecutingAssembly().GetManifestResourceStream("ffmpeg.exe.gz");
            if (rs == null)
                return (null, "O motor de vídeo (FFmpeg) não está embutido nesta compilação. Use o executável gerado pelo GitHub Actions.");

            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelaRec", "bin");
            Directory.CreateDirectory(dir);
            var target = System.IO.Path.Combine(dir, $"ffmpeg-{rs.Length}.exe");

            if (File.Exists(target) && new FileInfo(target).Length > 1_000_000) return (target, null);

            var tmp = target + ".tmp";
            using (var gz = new GZipStream(rs, CompressionMode.Decompress))
            using (var fs = File.Create(tmp))
                gz.CopyTo(fs);
            if (File.Exists(target)) File.Delete(target);
            File.Move(tmp, target);

            foreach (var old in Directory.GetFiles(dir, "ffmpeg-*.exe"))
                if (!string.Equals(old, target, StringComparison.OrdinalIgnoreCase))
                    try { File.Delete(old); } catch { }

            return (target, null);
        }
        catch (Exception ex)
        {
            return (null, "Não foi possível preparar o motor de vídeo: " + ex.Message);
        }
    }
}
