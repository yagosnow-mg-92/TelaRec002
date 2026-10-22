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
/// O relógio é o do Windows: quando não há som (o loopback não envia nada em silêncio) o mixer injeta silêncio,
/// mantendo o áudio sempre do mesmo comprimento do vídeo.
/// </summary>
internal sealed class AudioPipeline : IDisposable
{
    private const int Frames = 960;            // 20 ms
    private const int Samples = Frames * 2;    // estéreo

    private readonly AudioOptions _o;
    private CaptureSource? _sys, _mic;
    private MicProcessor? _micProc;
    private Stream? _out;
    private Thread? _thread;
    private volatile bool _stop;

    public List<string> Warnings { get; } = new();

    public AudioPipeline(AudioOptions o)
    {
        _o = o;

        if (o.RecordSystem)
        {
            try { _sys = new CaptureSource(new WasapiLoopbackCapture()); }
            catch (Exception ex) { Warnings.Add("Aviso: não foi possível capturar o som do computador (" + ex.Message + ")."); }
        }

        if (o.RecordMic)
        {
            try
            {
                using var en = new MMDeviceEnumerator();
                var dev = o.MicDeviceId != null ? en.GetDevice(o.MicDeviceId) : en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
                _mic = new CaptureSource(new WasapiCapture(dev));
                _micProc = new MicProcessor(o.MicGain, o.Denoise, o.DenoiseLevel);
            }
            catch (Exception ex)
            {
                Warnings.Add("Aviso: não foi possível usar o microfone (" + ex.Message +
                             "). Verifique Configurações do Windows → Privacidade → Microfone.");
            }
        }
    }

    private ManualResetEventSlim _videoStarted = new(true);

    public void Start(Stream output, ManualResetEventSlim videoStarted)
    {
        _out = output;
        _videoStarted = videoStarted;
        try { _sys?.Start(); }
        catch (Exception ex) { Warnings.Add("Aviso: falha ao iniciar a captura do som do computador (" + ex.Message + ")."); _sys?.Dispose(); _sys = null; }
        try { _mic?.Start(); }
        catch (Exception ex)
        {
            Warnings.Add("Aviso: falha ao iniciar o microfone (" + ex.Message + "). Verifique a permissão de microfone do Windows.");
            _mic?.Dispose(); _mic = null;
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "TelaRec-audio", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    private void Run()
    {
        var sysBuf = new float[Samples];
        var micBuf = new float[Samples];
        var mix = new float[Samples];
        var pcm = new byte[Samples * 2];

        // 1) "Escorva": um bloco de silêncio logo de início libera a leitura do FFmpeg
        //    (ele só começa o vídeo depois de abrir a entrada de áudio).
        if (!WriteSilence(pcm, 1)) return;

        // 2) Espera o 1º quadro de vídeo existir. O áudio é PCM puro (sem relógio próprio), então o que
        //    escrevermos a partir de agora cai exatamente no início da linha do tempo do vídeo.
        var wait = Stopwatch.StartNew();
        while (!_stop && !_videoStarted.Wait(10) && wait.ElapsedMilliseconds < 6000) { }
        if (_stop) return;
        _sys?.Clear(); _mic?.Clear();

        // 3) Ajuste fino do usuário: positivo = áudio mais tarde (silêncio extra); negativo = áudio mais cedo.
        int adj = _o.SyncAdjustMs;
        if (adj > 0 && !WriteSilence(pcm, adj / 20)) return;
        long skip = adj < 0 ? (-adj) / 20 : 0;

        var clock = Stopwatch.StartNew();
        long blocks = 0;
        while (!_stop)
        {
            if (blocks > clock.ElapsedMilliseconds / 20) { Thread.Sleep(1); continue; }

            Array.Clear(mix, 0, Samples);
            if (_sys != null)
            {
                int n = _sys.Read(sysBuf, Samples);
                if (n < Samples) Array.Clear(sysBuf, n, Samples - n);
                AudioMath.ApplyGain(sysBuf, Samples, _o.SystemGain);
                AudioMath.MixInto(mix, sysBuf, Samples);
                _sys.Trim(0.25);
            }
            if (_mic != null && _micProc != null)
            {
                int n = _mic.Read(micBuf, Samples);
                if (n < Samples) Array.Clear(micBuf, n, Samples - n);
                _micProc.Process(micBuf, Samples);
                AudioMath.MixInto(mix, micBuf, Samples);
                _mic.Trim(0.25);
            }

            if (blocks >= skip)
            {
                AudioMath.ToPcm16(mix, Samples, pcm);
                try { _out!.Write(pcm, 0, pcm.Length); _out.Flush(); }
                catch { break; } // FFmpeg encerrou: o Recorder trata o restante
            }
            blocks++;
        }
    }

    private bool WriteSilence(byte[] pcm, int blocks)
    {
        Array.Clear(pcm, 0, pcm.Length);
        try
        {
            for (int i = 0; i < blocks && !_stop; i++) { _out!.Write(pcm, 0, pcm.Length); }
            _out!.Flush();
            return true;
        }
        catch { return false; }
    }

    /// <summary>Para o mixer e as capturas. O chamador fecha o stdin do FFmpeg em seguida.</summary>
    public void Stop()
    {
        _stop = true;
        try { _thread?.Join(3000); } catch { }
        _sys?.Stop(); _mic?.Stop();
        _sys?.Dispose(); _mic?.Dispose();
        _sys = null; _mic = null;
    }

    public void Dispose() => Stop();
}
