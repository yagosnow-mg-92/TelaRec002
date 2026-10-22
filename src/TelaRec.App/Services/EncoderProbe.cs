using System.Diagnostics;
using TelaRec.Core;

namespace TelaRec.Services;

/// <summary>Testa (com um mini-encode de 0,5 s) quais encoders de hardware realmente funcionam nesta máquina.</summary>
internal static class EncoderProbe
{
    public static async Task<VideoEncoder> DetectBestAsync(string ffmpeg)
    {
        foreach (var e in new[] { VideoEncoder.Nvenc, VideoEncoder.Qsv, VideoEncoder.Amf })
            if (await TestAsync(ffmpeg, e)) return e;
        return VideoEncoder.Cpu;
    }

    private static async Task<bool> TestAsync(string ffmpeg, VideoEncoder e)
    {
        try
        {
            var psi = new ProcessStartInfo(ffmpeg, FfmpegArgs.BuildEncoderProbe(e))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true, RedirectStandardOutput = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            _ = p.StandardError.ReadToEndAsync();
            _ = p.StandardOutput.ReadToEndAsync();
            bool done = await Task.Run(() => p.WaitForExit(12000));
            if (!done) { try { p.Kill(true); } catch { } return false; }
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    public static string Describe(VideoEncoder e) => e switch
    {
        VideoEncoder.Nvenc => "NVIDIA NVENC",
        VideoEncoder.Qsv => "Intel Quick Sync",
        VideoEncoder.Amf => "AMD AMF",
        _ => "CPU (x264)",
    };
}
