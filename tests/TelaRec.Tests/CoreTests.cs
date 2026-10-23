using TelaRec.Core;

namespace TelaRec.Tests;

public class FfmpegArgsTests
{
    private static MonitorInfo Mon(int x, int y, int w, int h, int a = 0, int o = 0) =>
        new(2, @"\\.\DISPLAY2", x, y, w, h, false, 100, a, o);

    [Fact]
    public void Dxgi_UsesAdapterAndOutputIndex()
    {
        var args = FfmpegArgs.BuildCapture(Mon(-1920, 0, 1920, 1080, 1, 2), CaptureMode.Dxgi, 30,
            VideoQuality.Balanced, VideoEncoder.Cpu, @"C:\v\a.mp4");
        Assert.Contains("d3d11va=hw:1", args);
        Assert.Contains("output_idx=2", args);
        Assert.Contains("framerate=30", args);
        Assert.Contains("libx264", args);
    }

    [Fact]
    public void Dxgi_WithoutMapping_Throws() =>
        Assert.Throws<InvalidOperationException>(() => FfmpegArgs.BuildCapture(
            Mon(0, 0, 1920, 1080, -1, -1), CaptureMode.Dxgi, 30, VideoQuality.Balanced, VideoEncoder.Cpu, "a.mp4"));

    [Fact]
    public void Gdi_SupportsNegativeCoordinates()
    {
        var args = FfmpegArgs.BuildCapture(Mon(-2560, -300, 2560, 1440), CaptureMode.Gdi, 60,
            VideoQuality.High, VideoEncoder.Cpu, "a.mp4");
        Assert.Contains("-offset_x -2560", args);
        Assert.Contains("-offset_y -300", args);
        Assert.Contains("-video_size 2560x1440", args);
    }

    [Fact]
    public void Output_NeverScalesResolution()
    {
        var args = FfmpegArgs.BuildCapture(Mon(0, 0, 1921, 1081), CaptureMode.Gdi, 30,
            VideoQuality.Balanced, VideoEncoder.Cpu, "a.mp4");
        Assert.Contains("crop=trunc(iw/2)*2:trunc(ih/2)*2", args); // só ajusta para número par
        Assert.DoesNotContain("scale=w=", args);
        Assert.DoesNotContain("scale=1", args);
    }

    [Theory]
    [InlineData(VideoEncoder.Nvenc, "h264_nvenc")]
    [InlineData(VideoEncoder.Qsv, "h264_qsv")]
    [InlineData(VideoEncoder.Amf, "h264_amf")]
    [InlineData(VideoEncoder.Cpu, "libx264")]
    public void EncoderArgs_SelectsCodec(VideoEncoder e, string expected) =>
        Assert.Contains(expected, FfmpegArgs.EncoderArgs(e, VideoQuality.Balanced));

    [Fact]
    public void HigherQuality_MeansLowerQuantizer()
    {
        Assert.True(FfmpegArgs.QualityValue(VideoQuality.High, VideoEncoder.Cpu) <
                    FfmpegArgs.QualityValue(VideoQuality.Economy, VideoEncoder.Cpu));
    }

    [Fact]
    public void ConcatLine_EscapesBackslashAndQuote() =>
        Assert.Equal("file 'C:/a/it'\\''s/seg001.mp4'", FfmpegArgs.ConcatLine(@"C:\a\it's\seg001.mp4"));

    [Fact]
    public void Capture_IsFragmentedMp4() =>
        Assert.Contains("frag_keyframe+empty_moov", FfmpegArgs.BuildCapture(
            Mon(0, 0, 1920, 1080), CaptureMode.Gdi, 30, VideoQuality.Balanced, VideoEncoder.Cpu, "a.mp4"));
}

public class HelpersTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "telarec-test-" + Guid.NewGuid().ToString("N"));
    public HelpersTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void UniquePath_DoesNotOverwrite()
    {
        File.WriteAllText(Path.Combine(_dir, "video.mp4"), "x");
        var p = FileNamer.UniquePath(_dir, "video");
        Assert.EndsWith("video (2).mp4", p);
        File.WriteAllText(p, "x");
        Assert.EndsWith("video (3).mp4", FileNamer.UniquePath(_dir, "video"));
    }

    [Theory]
    [InlineData("meu:video?.mp4", "meuvideo")]
    [InlineData("  aula 1  ", "aula 1")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Sanitize_RemovesInvalidChars(string? input, string expected) =>
        Assert.Equal(expected, FileNamer.Sanitize(input));

    [Fact]
    public void TimeFormat_HandlesLongRecordings() =>
        Assert.Equal("100:00:05", TimeFormat.Format(TimeSpan.FromSeconds(360005)));

    [Fact]
    public void Settings_RoundTrip()
    {
        var path = Path.Combine(_dir, "s.json");
        new AppSettings { Fps = 60, Quality = VideoQuality.High, FileName = "x", ClickEffect = true, ClickSound = false }.Save(path);
        var s = AppSettings.Load(path);
        Assert.Equal(60, s.Fps);
        Assert.Equal(VideoQuality.High, s.Quality);
        Assert.Equal("x", s.FileName);
        Assert.True(s.ClickEffect);
        Assert.False(s.ClickSound);
    }

    [Fact]
    public void Settings_CorruptedFile_FallsBackToDefaults()
    {
        var path = Path.Combine(_dir, "bad.json");
        File.WriteAllText(path, "{{{ nope");
        Assert.Equal(30, AppSettings.Load(path).Fps);
    }

    [Fact]
    public void MonitorInfo_GeometryComparison()
    {
        var a = new MonitorInfo(1, "D1", -1920, 0, 1920, 1080, false, 100);
        Assert.True(a.SameGeometryAs(a with { AdapterIndex = 3 }));
        Assert.False(a.SameGeometryAs(a with { Width = 1280 }));
    }
}

public class AudioTests
{
    private static MonitorInfo Mon(int a = 0, int o = 0) => new(1, "D1", 0, 0, 1920, 1080, true, 100, a, o);

    [Fact]
    public void Args_WithoutAudio_HaveNoAudioInput()
    {
        var a = FfmpegArgs.BuildCapture(Mon(), CaptureMode.Dxgi, 30, VideoQuality.Balanced, VideoEncoder.Cpu, "a.mp4");
        Assert.DoesNotContain("pipe:0", a);
        Assert.DoesNotContain("aac", a);
        Assert.DoesNotContain("showinfo", a);
    }

    [Theory]
    [InlineData(CaptureMode.Dxgi)]
    [InlineData(CaptureMode.Gdi)]
    public void Args_WithAudio_ReadPcmFromPipeAndEncodeAac(CaptureMode mode)
    {
        var a = FfmpegArgs.BuildCapture(Mon(), mode, 30, VideoQuality.Balanced, VideoEncoder.Cpu, "a.mp4", audio: true);
        Assert.Contains("-f s16le -ar 48000 -ac 2", a);
        Assert.Contains("-i pipe:0", a);
        Assert.Contains("-c:a aac", a);
        Assert.Contains("-shortest", a);
        Assert.Contains("-map", a);
        Assert.Contains("-probesize 32", a);   // evita ~1 s de atraso do áudio
        Assert.Contains("showinfo", a);        // sinaliza o 1º quadro de vídeo
    }

    [Fact]
    public void ToPcm16_ClampsAndIsLittleEndian()
    {
        var dst = new byte[6];
        AudioMath.ToPcm16(new[] { 0f, 5f, -5f }, 3, dst);
        Assert.Equal(0, BitConverter.ToInt16(dst, 0));
        Assert.InRange(BitConverter.ToInt16(dst, 2), 32000, 32767);
        Assert.InRange(BitConverter.ToInt16(dst, 4), -32767, -32000);
    }

    [Fact]
    public void Mix_SumsSources()
    {
        var d = new[] { 0.1f, 0.2f }; AudioMath.MixInto(d, new[] { 0.3f, 0.1f }, 2);
        Assert.Equal(0.4f, d[0], 3); Assert.Equal(0.3f, d[1], 3);
    }

    private static float Rms(float[] b) { double s = 0; foreach (var v in b) s += v * v; return (float)Math.Sqrt(s / b.Length); }

    [Fact]
    public void Gate_AttenuatesLowNoise()
    {
        var gate = new MicProcessor(1f, true, 1);
        var rnd = new Random(1);
        float inRms = 0, outRms = 0;
        for (int k = 0; k < 100; k++)
        {
            var b = new float[1920];
            for (int i = 0; i < b.Length; i++) b[i] = (float)(rnd.NextDouble() * 2 - 1) * 0.003f;
            inRms = Rms(b); gate.Process(b, b.Length); outRms = Rms(b);
        }
        Assert.True(outRms < inRms * 0.5f);
    }

    [Fact]
    public void Gate_PassesLoudSpeechLikeSignal()
    {
        var gate = new MicProcessor(1f, true, 2);
        float inRms = 0, outRms = 0;
        for (int k = 0; k < 30; k++)
        {
            var b = new float[1920];
            for (int f = 0; f < 960; f++)
            {
                float v = 0.3f * MathF.Sin(2 * MathF.PI * 440 * (k * 960 + f) / 48000f);
                b[2 * f] = v; b[2 * f + 1] = v;
            }
            inRms = Rms(b); gate.Process(b, b.Length); outRms = Rms(b);
        }
        Assert.True(outRms > inRms * 0.9f);
    }

    [Fact]
    public void Gain_IsApplied_WithoutDenoise()
    {
        var p = new MicProcessor(2f, false, 1);
        var b = new[] { 0.1f, 0.1f };
        p.Process(b, 2);
        Assert.Equal(0.2f, b[0], 3);
    }

    [Fact]
    public void Settings_AudioRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "telarec-audio-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            new AppSettings { RecordSystemAudio = true, RecordMic = true, MicDeviceId = "id1", MicGainPct = 150, Denoise = true, DenoiseLevel = 2, AudioSyncAdjustMs = -150 }.Save(path);
            var s = AppSettings.Load(path);
            Assert.True(s.RecordSystemAudio); Assert.True(s.RecordMic);
            Assert.Equal("id1", s.MicDeviceId); Assert.Equal(150, s.MicGainPct);
            Assert.True(s.Denoise); Assert.Equal(2, s.DenoiseLevel); Assert.Equal(-150, s.AudioSyncAdjustMs);
        }
        finally { try { File.Delete(path); } catch { } }
    }
}

public class DriveTests
{
    [Theory]
    [InlineData("https://drive.google.com/drive/folders/1AbCdEfGhIjKlMnOp?usp=sharing", "1AbCdEfGhIjKlMnOp")]
    [InlineData("https://drive.google.com/drive/u/0/folders/1AbCdEfGhIjKlMnOp", "1AbCdEfGhIjKlMnOp")]
    [InlineData("https://drive.google.com/drive/folders/1AbCdEfGhIjKlMnOp?resourcekey=0-xyz", "1AbCdEfGhIjKlMnOp")]
    [InlineData("https://drive.google.com/open?id=1AbCdEfGhIjKlMnOp", "1AbCdEfGhIjKlMnOp")]
    [InlineData("  1AbCdEfGhIjKlMnOp  ", "1AbCdEfGhIjKlMnOp")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("https://example.com/qualquer", null)]
    [InlineData("curto", null)]
    public void ParseFolderId_Works(string? input, string? expected) =>
        Assert.Equal(expected, DriveLink.ParseFolderId(input));

    [Theory]
    [InlineData("https://script.google.com/macros/s/AKfycbx123/exec", true)]
    [InlineData("https://script.google.com/a/macros/empresa.com/s/AKfycbx123/exec", true)]
    [InlineData("https://script.google.com/macros/s/AKfycbx123/dev", false)]
    [InlineData("https://evil.example.com/macros/s/x/exec", false)]
    [InlineData("", false)]
    public void IsScriptUrl_Works(string url, bool expected) =>
        Assert.Equal(expected, DriveLink.IsScriptUrl(url));

    [Fact]
    public void BridgeScript_HasRequiredPieces()
    {
        var c = DriveBridgeScript.Code;
        Assert.Contains("function doPost", c);
        Assert.Contains("uploadType=resumable", c);
        Assert.Contains("supportsAllDrives=true", c);
        Assert.Contains("X-Upload-Content-Length", c);
        Assert.Contains("TROQUE-POR-UMA-CHAVE-LONGA", c);   // o usuário precisa trocar a chave
        Assert.Contains("req.key !== CHAVE", c);             // validação da chave
        Assert.Contains("function doGet", c);                // teste pelo navegador
        Assert.Contains("function autorizar", c);            // concede as permissões
    }

    [Fact]
    public void Settings_DriveRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "telarec-drive-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            new AppSettings { DriveEnabled = true, DriveAuto = true, DriveFolderLink = "f", DriveScriptUrl = "u", DriveKeyEnc = "k" }.Save(path);
            var s = AppSettings.Load(path);
            Assert.True(s.DriveEnabled); Assert.True(s.DriveAuto);
            Assert.Equal("f", s.DriveFolderLink); Assert.Equal("u", s.DriveScriptUrl); Assert.Equal("k", s.DriveKeyEnc);
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
