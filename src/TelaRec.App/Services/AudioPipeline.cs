using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TelaRec.Core;

namespace TelaRec.Services;

/// <summary>Fila circular de amostras float (thread-safe), usada como ponte entre a captura e o mixer.</summary>
internal sealed class RingSampleProvider : ISampleProvider
{
    private readonly float[] _data;
    private int _read, _count;
    private readonly object _lock = new();
    public WaveFormat WaveFormat { get; }

    public RingSampleProvider(WaveFormat fmt, int seconds)
    {
        WaveFormat = fmt;
        _data = new float[fmt.SampleRate * fmt.Channels * seconds];
    }

    public void Write(float[] src, int n)
    {
        lock (_lock)
        {
            if (n >= _data.Length)
            {
                Array.Copy(src, n - _data.Length, _data, 0, _data.Length);
                _read = 0; _count = _data.Length; return;
            }
            int free = _data.Length - _count;
            if (n > free) { int drop = n - free; _read = (_read + drop) % _data.Length; _count -= drop; }
            int w = (_read + _count) % _data.Length;
            int first = Math.Min(n, _data.Length - w);
            Array.Copy(src, 0, _data, w, first);
            if (first < n) Array.Copy(src, first, _data, 0, n - first);
            _count += n;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (_lock)
        {
            int n = Math.Min(count, _count);
            int first = Math.Min(n, _data.Length - _read);
            Array.Copy(_data, _read, buffer, offset, first);
            if (first < n) Array.Copy(_data, 0, buffer, offset + first, n - first);
            _read = (_read + n) % _data.Length;
            _count -= n;
            return n;
        }
    }

    public void TrimTo(double seconds)
    {
        lock (_lock)
        {
            int keep = (int)(seconds * WaveFormat.SampleRate * WaveFormat.Channels);
            keep -= keep % WaveFormat.Channels;
            if (_count > keep) { int drop = _count - keep; _read = (_read + drop) % _data.Length; _count = keep; }
        }
    }

    public void Clear() { lock (_lock) { _read = 0; _count = 0; } }
}

/// <summary>
/// Envolve uma captura WASAPI (microfone ou loopback do som do PC) e entrega sempre PCM float estéreo 48 kHz,
/// qualquer que seja o formato nativo do dispositivo.
/// </summary>
internal sealed class CaptureSource : IDisposable
{
    private readonly IWaveIn _cap;
    private readonly RingSampleProvider _ring;
    private readonly ISampleProvider _chain;
    private readonly WaveFormat _fmt;
    private readonly int _bits;
    private readonly bool _isFloat;
    private float[] _tmp = Array.Empty<float>();

    public CaptureSource(IWaveIn capture)
    {
        _cap = capture;
        _fmt = capture.WaveFormat;
        _bits = _fmt.BitsPerSample;
        _isFloat = _fmt.Encoding == WaveFormatEncoding.IeeeFloat ||
                   (_bits == 32 && _fmt.Encoding != WaveFormatEncoding.Pcm);
        _ring = new RingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(_fmt.SampleRate, _fmt.Channels), 3);

        ISampleProvider sp = _ring;
        if (_fmt.Channels == 1) sp = new MonoToStereoSampleProvider(sp);
        else if (_fmt.Channels > 2) sp = new MultiplexingSampleProvider(new[] { sp }, 2);
        if (_fmt.SampleRate != 48000) sp = new WdlResamplingSampleProvider(sp, 48000);
        _chain = sp;

        _cap.DataAvailable += OnData;
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        int bytesPer = _bits / 8;
        if (bytesPer == 0) return;
        int frames = e.BytesRecorded / (bytesPer * _fmt.Channels);
        int n = frames * _fmt.Channels;
        if (n <= 0) return;
        if (_tmp.Length < n) _tmp = new float[n];

        int p = 0;
        for (int i = 0; i < n; i++, p += bytesPer)
        {
            float v;
            switch (_bits)
            {
                case 16: v = BitConverter.ToInt16(e.Buffer, p) / 32768f; break;
                case 24:
                    int s24 = e.Buffer[p] | (e.Buffer[p + 1] << 8) | (e.Buffer[p + 2] << 16);
                    if ((s24 & 0x800000) != 0) s24 |= unchecked((int)0xFF000000);
                    v = s24 / 8388608f; break;
                case 32:
                    v = _isFloat ? BitConverter.ToSingle(e.Buffer, p) : BitConverter.ToInt32(e.Buffer, p) / 2147483648f;
                    break;
                default: v = 0; break;
            }
            _tmp[i] = v;
        }
        _ring.Write(_tmp, n);
    }

    public void Start() => _cap.StartRecording();
    public void Stop() { try { _cap.StopRecording(); } catch { } }
    public void Clear() => _ring.Clear();
    public double BufferedSeconds => _ring.BufferedSeconds;
    public void Trim(double seconds) => _ring.TrimTo(seconds);

    /// <summary>Lê até <paramref name="count"/> amostras (estéreo 48 kHz); devolve quantas existiam.</summary>
    public int Read(float[] dst, int count)
    {
        int total = 0;
        while (total < count)
        {
            int r = _chain.Read(dst, total, count - total);
            if (r <= 0) break;
            total += r;
        }
        return total;
    }

    public void Dispose() { try { _cap.DataAvailable -= OnData; _cap.Dispose(); } catch { } }
}

/// <summary>
/// Mixa som do computador + microfone em tempo real e entrega ao FFmpeg (stdin) como PCM s16le 48 kHz estéreo.
/// O mixer NUNCA espera o FFmpeg: os blocos de 20 ms vão para uma fila e uma thread de escrita os entrega ao pipe
/// conforme o FFmpeg consegue ler. Assim, se o FFmpeg demorar para ler (ex.: codificador de vídeo ocupado),
/// nenhum áudio capturado é descartado. O relógio é o do Windows: quando não há som o mixer injeta silêncio, e ao
/// parar completa com silêncio até o tamanho do vídeo para que o "-shortest" nunca corte o vídeo.
/// </summary>
internal sealed class AudioPipeline : IDisposable
{
    private const int Frames = 960;            // 20 ms
    private const int Samples = Frames * 2;    // estéreo
    private const int PrimeBlocks = 2;         // 40 ms de silêncio (> 4096 bytes, o tamanho do 1º pacote do demuxer PCM)
    private const int MaxQueueBlocks = 30000;  // 10 min de áudio: limite de segurança da fila

    private readonly AudioOptions _o;
    private CaptureSource? _sys, _mic;
    private MicProcessor? _micProc;
    private Stream? _out;
    private Thread? _thread, _writer;
    private BlockingCollection<byte[]>? _q;
    private volatile bool _stop;
    private long _written;                      // blocos de 20 ms enfileirados (comprimento lógico do áudio)
    private readonly Stopwatch _t0 = Stopwatch.StartNew();
    private readonly List<string> _notes = new();

    // estatísticas para o relatório técnico
    private long _blocksMixed, _micStarved, _maxQueue;
    private double _maxMicBufMs, _maxWriteMs;
    private bool _queueFullNoted;

    public List<string> Warnings { get; } = new();

    private void Note(string s) { lock (_notes) _notes.Add($"{_t0.ElapsedMilliseconds,6} ms  {s}"); }
    public List<string> GetNotes() { lock (_notes) return new List<string>(_notes); }

    public AudioPipeline(AudioOptions o)
    {
        _o = o;

        if (o.RecordSystem)
        {
            try { _sys = new CaptureSource(new WasapiLoopbackCapture()); Note("Som do computador: captura criada."); }
            catch (Exception ex) { Warnings.Add("Aviso: não foi possível capturar o som do computador (" + ex.Message + ")."); Note("Som do computador: FALHOU - " + ex.Message); }
        }

        if (o.RecordMic)
        {
            try
            {
                using var en = new MMDeviceEnumerator();
                var dev = o.MicDeviceId != null ? en.GetDevice(o.MicDeviceId) : en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
                var cap = new WasapiCapture(dev);
                _mic = new CaptureSource(cap);
                _micProc = new MicProcessor(o.MicGain, o.Denoise, o.DenoiseLevel);
                Note($"Microfone: captura criada ({dev.FriendlyName}; formato {cap.WaveFormat.SampleRate} Hz, {cap.WaveFormat.Channels} canal(is), {cap.WaveFormat.BitsPerSample} bits).");
            }
            catch (Exception ex)
            {
                Warnings.Add("Aviso: não foi possível usar o microfone (" + ex.Message +
                             "). Verifique Configurações do Windows → Privacidade → Microfone.");
                Note("Microfone: FALHOU - " + ex.Message);
            }
        }
    }

    private ManualResetEventSlim _videoStarted = new(true);

    public void Start(Stream output, ManualResetEventSlim videoStarted)
    {
        _out = output;
        _videoStarted = videoStarted;
        _q = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>());
        _writer = new Thread(WriterLoop) { IsBackground = true, Name = "TelaRec-audio-writer" };
        _writer.Start();

        try { _sys?.Start(); if (_sys != null) Note("Som do computador: capturando."); }
        catch (Exception ex) { Warnings.Add("Aviso: falha ao iniciar a captura do som do computador (" + ex.Message + ")."); Note("Som do computador: falha ao iniciar - " + ex.Message); _sys?.Dispose(); _sys = null; }
        try { _mic?.Start(); if (_mic != null) Note("Microfone: capturando."); }
        catch (Exception ex)
        {
            Warnings.Add("Aviso: falha ao iniciar o microfone (" + ex.Message + "). Verifique a permissão de microfone do Windows.");
            Note("Microfone: falha ao iniciar - " + ex.Message);
            _mic?.Dispose(); _mic = null;
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "TelaRec-audio", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>Entrega a fila ao FFmpeg. Só esta thread escreve no pipe (e só ela pode ficar bloqueada).</summary>
    private void WriterLoop()
    {
        try
        {
            foreach (var b in _q!.GetConsumingEnumerable())
            {
                long t = Stopwatch.GetTimestamp();
                _out!.Write(b, 0, b.Length);
                double ms = (Stopwatch.GetTimestamp() - t) * 1000.0 / Stopwatch.Frequency;
                if (ms > _maxWriteMs) _maxWriteMs = ms;
            }
        }
        catch (Exception ex)
        {
            Note("Erro ao escrever no FFmpeg: " + ex.Message);
            try { foreach (var _ in _q!.GetConsumingEnumerable()) { } } catch { }   // esvazia para não acumular memória
        }
    }

    private void Enqueue(byte[] block)
    {
        var q = _q;
        if (q == null || q.IsAddingCompleted) return;
        if (q.Count >= MaxQueueBlocks)
        {
            if (!_queueFullNoted) { _queueFullNoted = true; Note("Fila de áudio cheia (FFmpeg muito lento): descartando áudio novo."); }
            return;
        }
        try
        {
            q.Add(block);
            Interlocked.Increment(ref _written);
            if (q.Count > _maxQueue) _maxQueue = q.Count;
        }
        catch (InvalidOperationException) { /* fila já finalizada */ }
    }

    private void EnqueueSilence(int blocks)
    {
        for (int i = 0; i < blocks && !_stop; i++) Enqueue(new byte[Samples * 2]);
    }

    private void Run()
    {
        var sysBuf = new float[Samples];
        var micBuf = new float[Samples];
        var mix = new float[Samples];
        var pcm = new byte[Samples * 2];

        // 1) "Escorva": alguns blocos de silêncio logo de início liberam a leitura do FFmpeg.
        EnqueueSilence(PrimeBlocks);
        Note("Silêncio inicial enfileirado.");

        // 2) Espera o 1º quadro de vídeo existir (no máximo 3 s). O áudio é PCM puro (sem relógio próprio),
        //    então o que entregarmos a partir daqui cai no início da linha do tempo do vídeo.
        var wait = Stopwatch.StartNew();
        while (!_stop && !_videoStarted.Wait(10) && wait.ElapsedMilliseconds < 3000) { }
        if (_stop) { Note("Parado antes do 1º quadro de vídeo."); return; }
        Note(_videoStarted.IsSet ? $"1º quadro de vídeo detectado após {wait.ElapsedMilliseconds} ms." : "Tempo esgotado esperando o 1º quadro de vídeo; seguindo mesmo assim.");
        _sys?.Clear(); _mic?.Clear();

        // 3) Ajuste fino do usuário: positivo = áudio mais tarde (silêncio extra); negativo = áudio mais cedo.
        //    Os blocos iniciais de escorva são compensados descartando o mesmo tanto de áudio real.
        int adj = _o.SyncAdjustMs;
        if (adj > 0) EnqueueSilence(adj / 20);
        long skip = PrimeBlocks + (adj < 0 ? (-adj) / 20 : 0);

        var clock = Stopwatch.StartNew();
        long blocks = 0;
        int errors = 0;
        while (!_stop)
        {
            if (blocks > clock.ElapsedMilliseconds / 20) { Thread.Sleep(1); continue; }

            try
            {
                Array.Clear(mix, 0, Samples);
                if (_sys != null)
                {
                    int n = _sys.Read(sysBuf, Samples);
                    if (n < Samples) Array.Clear(sysBuf, n, Samples - n);
                    AudioMath.ApplyGain(sysBuf, Samples, _o.SystemGain);
                    AudioMath.MixInto(mix, sysBuf, Samples);
                    _sys.Trim(1.0);
                }
                if (_mic != null && _micProc != null)
                {
                    double bufMs = _mic.BufferedSeconds * 1000.0;
                    if (bufMs > _maxMicBufMs) _maxMicBufMs = bufMs;
                    int n = _mic.Read(micBuf, Samples);
                    if (n < Samples) { Array.Clear(micBuf, n, Samples - n); _micStarved++; }
                    _micProc.Process(micBuf, Samples);
                    AudioMath.MixInto(mix, micBuf, Samples);
                    _mic.Trim(1.0);
                }
            }
            catch (Exception ex)
            {
                // Falha ao ler/mixar: envia silêncio neste bloco e segue, para o áudio nunca ficar mais curto que o vídeo.
                Array.Clear(mix, 0, Samples);
                if (errors++ == 0) Note("Erro ao mixar o áudio (seguindo com silêncio): " + ex.Message);
            }

            if (blocks >= skip)
            {
                AudioMath.ToPcm16(mix, Samples, pcm);
                var copy = new byte[pcm.Length];
                Buffer.BlockCopy(pcm, 0, copy, 0, pcm.Length);
                Enqueue(copy);
            }
            blocks++;
            _blocksMixed = blocks;
        }
        Note($"Mixer encerrado ({blocks} blocos, {errors} erro(s)).");
    }

    /// <summary>
    /// Para o mixer e as capturas. Se <paramref name="videoElapsed"/> for informado, completa o áudio com silêncio até
    /// cobrir o tempo de vídeo (evita que o -shortest corte o vídeo) e espera a fila ser entregue ao FFmpeg.
    /// O chamador fecha o stdin do FFmpeg em seguida.
    /// </summary>
    public void Stop(TimeSpan? videoElapsed = null)
    {
        _stop = true;
        try { _thread?.Join(2000); } catch { }

        _sys?.Stop(); _mic?.Stop();
        _sys?.Dispose(); _mic?.Dispose();
        _sys = null; _mic = null;

        if (videoElapsed is TimeSpan ve && _q != null)
        {
            long target = (long)(ve.TotalMilliseconds / 20) + 25;          // + 0,5 s de folga
            long missing = Math.Min(target - Interlocked.Read(ref _written), 30000);
            if (missing > 0)
            {
                for (long i = 0; i < missing; i++) Enqueue(new byte[Samples * 2]);
                Note($"Áudio completado com {missing} bloco(s) de silêncio para cobrir o vídeo.");
            }
        }

        // Entrega o que ficou na fila (o FFmpeg pode estar atrasado); se travar, fecha o pipe para destravar.
        try
        {
            _q?.CompleteAdding();
            if (_writer != null && !_writer.Join(20000))
            {
                Note("A fila de áudio não foi entregue em 20 s; fechando o pipe.");
                try { _out?.Close(); } catch { }
                _writer.Join(2000);
            }
        }
        catch { }

        Note($"Resumo: {_blocksMixed} blocos mixados; microfone com buffer vazio em {_micStarved} bloco(s); " +
             $"maior buffer do microfone {_maxMicBufMs:0} ms; maior bloqueio de escrita no FFmpeg {_maxWriteMs:0} ms; fila máxima {_maxQueue} bloco(s).");
    }

    public void Dispose() => Stop();
}
