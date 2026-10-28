using System.Diagnostics;
using System.IO;
using System.Text;
using TelaRec.Core;

namespace TelaRec.Services;

public enum RecState { Ready, Starting, Recording, Paused, Finalizing, Done, Error }

public sealed class RecordingResult
{
    public bool Success { get; set; }
    public bool Ignored { get; set; }
    public string? FilePath { get; set; }
    public string? KeptFolder { get; set; }
    public string? LogPath { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>
/// Controla o FFmpeg. Cada trecho (início → pausa) vira um MP4 fragmentado em pasta temporária;
/// ao finalizar, os trechos são unidos sem recodificar (-c copy) e o arquivo final recebe "faststart".
/// Tempo em pausa nunca é gravado, pois nenhum processo de captura fica ativo durante a pausa.
/// </summary>
public sealed class Recorder : IDisposable
{
    private readonly string _ffmpeg;
    private readonly List<string> _segments = new();
    private readonly Stopwatch _sw = new();
    private TimeSpan _accum;
    private Process? _proc;
    private StringBuilder _log = new();
    private MonitorInfo? _monitor;
    private int _fps;
    private VideoQuality _quality;
    private CaptureMode _mode;
    private VideoEncoder _enc;
    private string _finalPath = "";
    private string _tempDir = "";
    private AudioOptions _audioOpt = AudioOptions.None;
    private readonly StringBuilder _diag = new();

    /// <summary>Relatório técnico da última gravação (comandos, linha do tempo e saída do FFmpeg).</summary>
    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TelaRec", "logs", "ultima-gravacao.log");

    private void Note(string s) { lock (_diag) _diag.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] {s}"); }

    private void WriteLog(string finalMessage)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            var sb = new StringBuilder();
            sb.AppendLine("TelaRec - relatório da última gravação");
            sb.AppendLine("Data: " + DateTime.Now);
            sb.AppendLine("Resultado: " + finalMessage);
            sb.AppendLine();
            lock (_diag) sb.Append(_diag);
            sb.AppendLine();
            sb.AppendLine("--- saída do FFmpeg (sem as linhas por quadro) ---");
            string t; lock (_log) t = _log.ToString();
            sb.AppendLine(t);
            File.WriteAllText(LogPath, sb.ToString(), Encoding.UTF8);
        }
        catch { /* o relatório é só um auxílio */ }
    }
    private AudioPipeline? _audio;
    private volatile bool _startingUp;
    private volatile bool _stoppingExpected;

    public Recorder(string ffmpegPath) { _ffmpeg = ffmpegPath; }

    public RecState State { get; private set; } = RecState.Ready;
    public TimeSpan Elapsed => _accum + (_sw.IsRunning ? _sw.Elapsed : TimeSpan.Zero);
    public string OutputFolder => Path.GetDirectoryName(_finalPath) ?? "";

    public event Action<RecState>? StateChanged;
    public event Action<string>? Warning;
    /// <summary>Disparado quando a gravação termina sozinha (ex.: falha inesperada do codificador).</summary>
    public event Action<RecordingResult>? Interrupted;

    private void SetState(RecState s) { State = s; StateChanged?.Invoke(s); }

    // ---------------------------------------------------------------- início
    public async Task<string?> StartAsync(MonitorInfo monitor, bool allowGdi, int fps, VideoQuality quality,
        VideoEncoder requested, string finalPath, AudioOptions? audio = null)
    {
        if (State is RecState.Starting or RecState.Recording or RecState.Paused or RecState.Finalizing)
            return "Já existe uma gravação em andamento.";
        SetState(RecState.Starting);      // a interface mostra "Iniciando…" na hora
        var err = await StartCoreAsync(monitor, allowGdi, fps, quality, requested, finalPath, audio);
        if (err != null) State = RecState.Ready;   // sem evento: a interface exibe o erro
        return err;
    }

    private async Task<string?> StartCoreAsync(MonitorInfo monitor, bool allowGdi, int fps, VideoQuality quality,
        VideoEncoder requested, string finalPath, AudioOptions? audio = null)
    {

        _monitor = monitor; _fps = fps; _quality = quality; _finalPath = finalPath;
        _audioOpt = audio ?? AudioOptions.None;
        lock (_diag) _diag.Clear();
        Note($"Início da gravação: monitor {monitor.Name} ({monitor.Resolution}), {fps} FPS, qualidade {quality}, codificador pedido {requested}.");

        try
        {
            var folder = Path.GetDirectoryName(finalPath)!;
            Directory.CreateDirectory(folder);
            _tempDir = Path.Combine(folder, ".telarec-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_tempDir);
            File.SetAttributes(_tempDir, FileAttributes.Hidden);
        }
        catch (UnauthorizedAccessException) { return "Sem permissão para salvar nesta pasta. Escolha outra pasta."; }
        catch (Exception ex) { return "Não foi possível preparar a pasta de destino: " + ex.Message; }

        _segments.Clear(); _accum = TimeSpan.Zero; _sw.Reset();

        var attempts = new List<(CaptureMode, VideoEncoder)>();
        if (monitor.AdapterIndex >= 0 && monitor.OutputIndex >= 0)
        {
            attempts.Add((CaptureMode.Dxgi, requested));
            if (requested != VideoEncoder.Cpu) attempts.Add((CaptureMode.Dxgi, VideoEncoder.Cpu));
        }
        if (allowGdi) attempts.Add((CaptureMode.Gdi, VideoEncoder.Cpu));
        if (attempts.Count == 0)
        {
            CleanupTemp();
            return "Não foi possível mapear este monitor para uma captura segura. Atualize a lista de monitores e tente novamente.";
        }

        string? err = null;
        foreach (var (mode, enc) in attempts)
        {
            err = await LaunchSegmentAsync(mode, enc);
            if (err == null)
            {
                _mode = mode; _enc = enc;
                var notes = new List<string>();
                if (mode == CaptureMode.Gdi) notes.Add("captura acelerada (DXGI) indisponível; usando o modo compatível (GDI)");
                if (enc != requested) notes.Add("codificação por hardware indisponível; usando a CPU");
                if (notes.Count > 0) Warning?.Invoke("Aviso: " + string.Join("; ", notes) + ".");
                break;
            }
        }

        // Última chance: se nenhuma tentativa COM áudio funcionou, grava SEM áudio (melhor um vídeo mudo que nenhum vídeo).
        if (err != null && _audioOpt.Any)
        {
            Note("Todas as tentativas com áudio falharam (" + err + "). Tentando sem áudio.");
            var saved = _audioOpt;
            _audioOpt = AudioOptions.None;
            foreach (var (mode, enc) in attempts)
            {
                err = await LaunchSegmentAsync(mode, enc);
                if (err == null)
                {
                    _mode = mode; _enc = enc;
                    Warning?.Invoke("Aviso: não foi possível iniciar a gravação COM áudio; este vídeo está sendo gravado SEM ÁUDIO. " +
                                    "Use o botão \"Abrir relatório\" ao terminar e envie o conteúdo ao suporte.");
                    break;
                }
            }
            if (err != null) _audioOpt = saved;
        }

        if (err != null)
        {
            Note("Falha final ao iniciar: " + err);
            WriteLog("Falha ao iniciar a captura: " + err);
            CleanupTemp();
            return "Falha ao iniciar a captura: " + err;
        }

        SetState(RecState.Recording);
        return null;
    }

    private async Task<string?> LaunchSegmentAsync(CaptureMode mode, VideoEncoder enc)
    {
        var seg = Path.Combine(_tempDir, $"seg{_segments.Count + 1:000}.mp4");
        var log = new StringBuilder();
        _log = log;
        var firstFrame = new ManualResetEventSlim(false);
        _startingUp = true;
        Process p;
        AudioPipeline? audio;
        try
        {
            var args = FfmpegArgs.BuildCapture(_monitor!, mode, _fps, _quality, enc, seg, _audioOpt.Any);
            Note($"Iniciando segmento: captura={mode}, codificador={enc}, áudio=" +
                 (_audioOpt.Any ? $"sim (sistema={_audioOpt.RecordSystem}, microfone={_audioOpt.RecordMic}, ajuste={_audioOpt.SyncAdjustMs} ms)" : "não"));
            Note("ffmpeg " + args);
            // Iniciar o processo e preparar o áudio (enumerar dispositivos) é lento: fora da thread da interface.
            (p, audio) = await Task.Run(() =>
            {
                var proc = Launch(args, log, firstFrame);
                AudioPipeline? ap = null;
                if (_audioOpt.Any)
                {
                    ap = new AudioPipeline(_audioOpt);
                    ap.Start(proc.StandardInput.BaseStream, firstFrame);
                }
                return (proc, ap);
            });
        }
        catch (Exception ex) { _startingUp = false; return "não foi possível iniciar o motor de vídeo (" + ex.Message + ")"; }

        _proc = p; _audio = audio;
        if (audio != null) foreach (var w in audio.Warnings) Warning?.Invoke(w);

        // Espera o 1º quadro de vídeo real (em vez de uma pausa fixa). O cronômetro só começa nesse instante.
        var wait = Stopwatch.StartNew();
        bool Started() => firstFrame.IsSet || (File.Exists(seg) && new FileInfo(seg).Length > 1000);
        while (wait.ElapsedMilliseconds < 8000 && !p.HasExited && !Started()) await Task.Delay(15);
        bool started = Started();
        Note(started ? $"Vídeo iniciou em {wait.ElapsedMilliseconds} ms (1º quadro detectado: {firstFrame.IsSet})."
                     : "O vídeo NÃO iniciou dentro do prazo (8 s).");

        if (!started && !p.HasExited)
        {
            _proc = null; _startingUp = false;      // o evento de saída será ignorado
            try { p.Kill(true); } catch { }
            try { audio?.Dispose(); } catch { }
            if (audio != null) lock (_diag) foreach (var n in audio.GetNotes()) _diag.AppendLine("  áudio: " + n);
            _audio = null;
            var t = LastLines(log);
            try { p.Dispose(); } catch { }
            try { if (File.Exists(seg)) File.Delete(seg); } catch { }
            return (_audioOpt.Any ? "o vídeo não iniciou junto com o áudio" : "o vídeo não iniciou (tempo esgotado)") +
                   (string.IsNullOrWhiteSpace(t) ? "." : " (" + t + ").");
        }
        _startingUp = false;

        if (p.HasExited)
        {
            _proc = null;
            try { audio?.Dispose(); } catch { }
            if (audio != null) lock (_diag) foreach (var n in audio.GetNotes()) _diag.AppendLine("  áudio: " + n);
            _audio = null;
            var tail = LastLines(log);
            Note("O FFmpeg encerrou logo após iniciar: " + tail);
            try { p.Dispose(); } catch { }
            try { if (File.Exists(seg)) File.Delete(seg); } catch { }
            return string.IsNullOrWhiteSpace(tail) ? "o codificador encerrou imediatamente." : tail;
        }

        _sw.Restart();            // a partir daqui o tempo gravado conta
        _segments.Add(seg);
        return null;
    }

    private Process Launch(string args, StringBuilder log, ManualResetEventSlim? firstFrame = null)
    {
        var psi = new ProcessStartInfo(_ffmpeg, args)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            // O filtro showinfo escreve uma linha por quadro: o 1º marca o início real do vídeo (sincronia do áudio).
            if (firstFrame != null && e.Data.Contains("Parsed_showinfo"))
            {
                if (!firstFrame.IsSet) firstFrame.Set();
                return;
            }
            lock (log) { if (log.Length < 20000) log.AppendLine(e.Data); }
        };
        p.Exited += (_, _) => OnExited(p);
        p.Start();
        p.BeginErrorReadLine();
        return p;
    }

    private static string LastLines(StringBuilder log)
    {
        string text; lock (log) text = log.ToString();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string[] keys = { "error", "fail", "invalid", "cannot", "could not", "unable", "no such", "denied" };
        var errs = lines.Where(l => keys.Any(k => l.Contains(k, StringComparison.OrdinalIgnoreCase))).ToList();
        var s = string.Join(" | ", (errs.Count > 0 ? errs : lines.ToList()).TakeLast(3));
        return s.Length > 300 ? s[..300] : s;
    }

    // ---------------------------------------------------------------- pausa / retomada
    private Task? _stopTask;

    /// <summary>
    /// Pausa na hora: o cronômetro congela e a interface muda imediatamente; a finalização do trecho
    /// (encerrar o FFmpeg e fechar o MP4) acontece em segundo plano.
    /// </summary>
    public Task PauseAsync()
    {
        if (State != RecState.Recording) return Task.CompletedTask;
        var seg = _sw.Elapsed;
        _accum += seg; _sw.Reset();
        SetState(RecState.Paused);
        _stopTask = StopSegmentAsync(seg);
        return Task.CompletedTask;
    }

    public async Task<string?> ResumeAsync()
    {
        if (State != RecState.Paused) return null;
        SetState(RecState.Starting);
        try { if (_stopTask != null) await _stopTask; } catch { }   // normalmente já terminou
        var err = await LaunchSegmentAsync(_mode, _enc);
        if (err != null)
        {
            SetState(RecState.Paused);
            return "Não foi possível retomar a gravação: " + err;
        }
        SetState(RecState.Recording);
        return null;
    }

    // ---------------------------------------------------------------- parada inesperada
    private void OnExited(Process p)
    {
        if (_startingUp || _stoppingExpected || !ReferenceEquals(p, _proc) || State != RecState.Recording) return;
        _ = Task.Run(async () =>
        {
            var tail = LastLines(_log);
            var r = await FinishAsync("A captura foi interrompida inesperadamente" +
                                      (string.IsNullOrWhiteSpace(tail) ? "." : " (" + tail + ")."));
            if (!r.Ignored) Interrupted?.Invoke(r);
        });
    }

    // ---------------------------------------------------------------- finalização
    public async Task<RecordingResult> FinishAsync(string? interruptionReason = null)
    {
        if (State is not (RecState.Recording or RecState.Paused))
            return new RecordingResult { Ignored = true };

        SetState(RecState.Finalizing);
        var seg = _sw.IsRunning ? _sw.Elapsed : TimeSpan.Zero;
        if (_sw.IsRunning) { _accum += seg; _sw.Reset(); }
        try { if (_stopTask != null) await _stopTask; } catch { }   // trecho de uma pausa ainda sendo finalizado
        await StopSegmentAsync(seg);                                // sem efeito se estava pausado (sem processo)
        var result = await MergeAsync();
        if (interruptionReason != null)
            result.Message = interruptionReason + " " + result.Message;
        Note("Resultado: " + result.Message);
        WriteLog(result.Message);
        if (!result.Success) result.LogPath = LogPath;
        SetState(result.Success ? RecState.Done : RecState.Error);
        return result;
    }

    private async Task StopSegmentAsync(TimeSpan segmentElapsed)
    {
        var p = _proc;
        if (p == null) return;
        _stoppingExpected = true;
        try
        {
            if (_audio != null)
            {
                // Com áudio, o stdin carrega o PCM: ao parar o mixer e fechar o pipe, o FFmpeg encerra
                // (-shortest) e finaliza o MP4 normalmente.
                var a = _audio; _audio = null;
                await Task.Run(() => a.Stop(segmentElapsed));
                try { p.StandardInput.Close(); } catch { }
                lock (_diag) foreach (var n in a.GetNotes()) _diag.AppendLine("  áudio: " + n);
            }
            else
            {
                try
                {
                    if (!p.HasExited)
                    {
                        await p.StandardInput.WriteAsync("q");
                        await p.StandardInput.FlushAsync();
                        p.StandardInput.Close();
                    }
                }
                catch { /* processo já encerrou */ }
            }

            bool exited = await Task.Run(() => p.WaitForExit(30000));
            if (!exited)
            {
                try { p.Kill(true); } catch { }
                await Task.Run(() => p.WaitForExit(5000));
            }
        }
        finally
        {
            try { p.Dispose(); } catch { }
            _proc = null;
            _stoppingExpected = false;
        }
    }

    private async Task<RecordingResult> MergeAsync()
    {
        var valid = _segments.Where(s => File.Exists(s) && new FileInfo(s).Length > 2048).ToList();
        if (valid.Count == 0)
        {
            var tail = LastLines(_log);
            Note("Nenhum segmento válido encontrado (arquivos ausentes ou menores que 2 KB).");
            CleanupTemp();
            return new RecordingResult
            {
                Success = false,
                Message = "Nenhum vídeo foi gravado." + (_audioOpt.Any ? " (a gravação estava com áudio ligado)" : "") +
                          (string.IsNullOrWhiteSpace(tail) ? "" : " Detalhe técnico: " + tail),
            };
        }

        var tmpOut = _finalPath + ".tmp";
        try
        {
            // Pouco espaço? Para um único trecho, o MP4 fragmentado já é válido: apenas move.
            if (valid.Count == 1)
            {
                var free = DiskSpace.FreeBytes(OutputFolder);
                long size = new FileInfo(valid[0]).Length;
                if (free != null && free < size + 100L * 1024 * 1024)
                {
                    var path = FinalizeMove(valid[0]);
                    CleanupTemp();
                    return new RecordingResult
                    {
                        Success = true, FilePath = path,
                        Message = "Vídeo salvo (sem otimização final por falta de espaço em disco): " + path,
                    };
                }
            }

            string args;
            if (valid.Count == 1) args = FfmpegArgs.BuildRemux(valid[0], tmpOut);
            else
            {
                var list = Path.Combine(_tempDir, "list.txt");
                File.WriteAllLines(list, valid.Select(FfmpegArgs.ConcatLine), new UTF8Encoding(false));
                args = FfmpegArgs.BuildConcat(list, tmpOut);
            }

            var log = new StringBuilder();
            int code = await RunToEndAsync(args, log, 3_600_000);

            if (code == 0 && File.Exists(tmpOut) && new FileInfo(tmpOut).Length > 2048)
            {
                var path = FinalizeMove(tmpOut);
                CleanupTemp();
                return new RecordingResult { Success = true, FilePath = path, Message = "Vídeo salvo em: " + path };
            }

            try { if (File.Exists(tmpOut)) File.Delete(tmpOut); } catch { }
            return new RecordingResult
            {
                Success = false, KeptFolder = _tempDir,
                Message = "Não foi possível finalizar o MP4 (" + LastLines(log) + "). Os trechos gravados foram mantidos em: " + _tempDir,
            };
        }
        catch (Exception ex)
        {
            return new RecordingResult
            {
                Success = false, KeptFolder = _tempDir,
                Message = "Erro ao salvar o vídeo: " + ex.Message + ". Os trechos gravados foram mantidos em: " + _tempDir,
            };
        }
    }

    /// <summary>Move para o destino final sem jamais sobrescrever um arquivo existente.</summary>
    private string FinalizeMove(string source)
    {
        var target = _finalPath;
        if (File.Exists(target))
            target = FileNamer.UniquePath(OutputFolder, Path.GetFileNameWithoutExtension(_finalPath));
        File.Move(source, target);
        return target;
    }

    private async Task<int> RunToEndAsync(string args, StringBuilder log, int timeoutMs)
    {
        var psi = new ProcessStartInfo(_ffmpeg, args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        using var p = new Process { StartInfo = psi };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (log) { if (log.Length < 20000) log.AppendLine(e.Data); } };
        p.Start();
        p.BeginErrorReadLine();
        bool done = await Task.Run(() => p.WaitForExit(timeoutMs));
        if (!done) { try { p.Kill(true); } catch { } return -1; }
        await Task.Run(() => p.WaitForExit()); // garante o fim da leitura do stderr
        return p.ExitCode;
    }

    private void CleanupTemp()
    {
        try { if (!string.IsNullOrEmpty(_tempDir) && Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { }
    }

    public void Dispose()
    {
        try { if (_proc is { HasExited: false }) _proc.Kill(true); } catch { }
    }
}
