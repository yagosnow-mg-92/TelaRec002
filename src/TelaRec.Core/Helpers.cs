using System.Text.Json;

namespace TelaRec.Core;

public static class FileNamer
{
    public static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var s = name.Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c.ToString(), "");
        if (s.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        return s.Trim().TrimEnd('.');
    }

    public static string DefaultName(DateTime now) => $"TelaRec_{now:yyyy-MM-dd_HH-mm-ss}";

    /// <summary>Nunca devolve um caminho de arquivo já existente (não sobrescreve gravações).</summary>
    public static string UniquePath(string folder, string baseName)
    {
        var p = Path.Combine(folder, baseName + ".mp4");
        int i = 2;
        while (File.Exists(p)) p = Path.Combine(folder, $"{baseName} ({i++}).mp4");
        return p;
    }
}

public static class TimeFormat
{
    public static string Format(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";

    public static string Bytes(long b)
    {
        double v = b; string[] u = { "B", "KB", "MB", "GB", "TB" }; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {u[i]}";
    }
}

public static class DiskSpace
{
    public static long? FreeBytes(string folder)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(folder));
            if (string.IsNullOrEmpty(root)) return null;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch { return null; }
    }
}

public sealed class AppSettings
{
    public string OutputFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "TelaRec");
    public string FileName { get; set; } = "";
    public int Fps { get; set; } = 30;
    public VideoQuality Quality { get; set; } = VideoQuality.Balanced;
    public bool UseHardware { get; set; } = true;
    public bool HideWindowFromCapture { get; set; } = true;
    public bool ClickEffect { get; set; } = false;
    public bool ClickSound { get; set; } = true;
    public bool RecordSystemAudio { get; set; } = false;
    public bool RecordMic { get; set; } = false;
    public string? MicDeviceId { get; set; }
    public int SystemGainPct { get; set; } = 100;
    public int MicGainPct { get; set; } = 100;
    public bool Denoise { get; set; } = false;
    public int DenoiseLevel { get; set; } = 1;
    /// <summary>Ajuste fino da sincronia (ms). Positivo = áudio mais tarde; negativo = áudio mais cedo.</summary>
    public int AudioSyncAdjustMs { get; set; } = 0;
    public bool DriveEnabled { get; set; } = false;
    public bool DriveAuto { get; set; } = false;
    public string? DriveFolderLink { get; set; }
    public string? DriveScriptUrl { get; set; }
    /// <summary>Chave secreta do script, protegida com DPAPI (somente neste usuário do Windows).</summary>
    public string? DriveKeyEnc { get; set; }

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
                if (s != null)
                {
                    if (s.Fps != 60) s.Fps = 30;
                    if (string.IsNullOrWhiteSpace(s.OutputFolder)) s.OutputFolder = new AppSettings().OutputFolder;
                    return s;
                }
            }
        }
        catch { /* configuração corrompida: usa padrões */ }
        return new AppSettings();
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* não é crítico */ }
    }
}
