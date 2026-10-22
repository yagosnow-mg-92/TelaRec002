namespace TelaRec.Core;

/// <summary>Opções de áudio de uma gravação. Ganhos são multiplicadores (1.0 = 100%).</summary>
public sealed record AudioOptions(
    bool RecordSystem,
    bool RecordMic,
    string? MicDeviceId,
    float SystemGain,
    float MicGain,
    bool Denoise,
    int DenoiseLevel,
    int SyncAdjustMs)
{
    public static AudioOptions None { get; } = new(false, false, null, 1f, 1f, false, 1, 0);
    public bool Any => RecordSystem || RecordMic;
}

/// <summary>Operações simples de áudio (PCM float entrelaçado, estéreo 48 kHz). Lógica pura e testável.</summary>
public static class AudioMath
{
    public static float Peak(float[] b, int n)
    {
        float p = 0;
        for (int i = 0; i < n; i++) { var a = Math.Abs(b[i]); if (a > p) p = a; }
        return p;
    }

    public static void ApplyGain(float[] b, int n, float gain)
    {
        if (gain == 1f) return;
        for (int i = 0; i < n; i++) b[i] *= gain;
    }

    public static void MixInto(float[] dst, float[] src, int n)
    {
        for (int i = 0; i < n; i++) dst[i] += src[i];
    }

    /// <summary>Limitador suave: evita estalos quando a soma das fontes passa de 0 dBFS.</summary>
    public static float SoftLimit(float x)
    {
        float a = Math.Abs(x);
        if (a <= 0.9f) return x;
        float o = 0.9f + 0.1f * MathF.Tanh((a - 0.9f) / 0.1f);
        return x < 0 ? -o : o;
    }

    public static void ToPcm16(float[] src, int n, byte[] dst)
    {
        for (int i = 0; i < n; i++)
        {
            short s = (short)Math.Round(SoftLimit(src[i]) * 32767f);
            dst[2 * i] = (byte)(s & 0xFF);
            dst[2 * i + 1] = (byte)((s >> 8) & 0xFF);
        }
    }
}

/// <summary>
/// Processamento do microfone: ganho + (opcional) filtro de graves e "porta de ruído" adaptativa.
/// A porta mede o ruído de fundo (menor nível recente) e atenua os trechos em que só há ruído.
/// Não remove ruído enquanto a pessoa fala. Opera em blocos de 20 ms (estéreo 48 kHz entrelaçado).
/// </summary>
public sealed class MicProcessor
{
    private readonly float _userGain;
    private readonly bool _denoise;
    private readonly float _closedGain;
    private readonly float _openFactor;
    private const float HpA = 0.98829f; // passa-altas ~90 Hz @ 48 kHz
    private float _pxL, _pyL, _pxR, _pyR;
    private float _floor = 0.005f;
    private float _gate = 1f;
    private int _hold;

    /// <param name="level">0 = leve, 1 = média, 2 = forte</param>
    public MicProcessor(float gain, bool denoise, int level)
    {
        _userGain = gain;
        _denoise = denoise;
        switch (level)
        {
            case 0: _closedGain = 0.35f; _openFactor = 2.5f; break;
            case 2: _closedGain = 0.04f; _openFactor = 4.0f; break;
            default: _closedGain = 0.15f; _openFactor = 3.0f; break;
        }
    }

    public void Process(float[] buf, int count)
    {
        AudioMath.ApplyGain(buf, count, _userGain);
        if (!_denoise || count < 2) return;

        double sum = 0;
        for (int i = 0; i + 1 < count; i += 2)
        {
            float l = buf[i], r = buf[i + 1];
            float yl = HpA * (_pyL + l - _pxL); _pxL = l; _pyL = yl;
            float yr = HpA * (_pyR + r - _pxR); _pxR = r; _pyR = yr;
            buf[i] = yl; buf[i + 1] = yr;
            sum += yl * yl + yr * yr;
        }
        float rms = (float)Math.Sqrt(sum / count);

        // Piso de ruído: desce na hora, sobe devagar (mais rápido com a porta fechada).
        if (rms < _floor) _floor = Math.Max(rms, 0.0003f);
        else _floor += (rms - _floor) * (_hold > 0 ? 0.0001f : 0.01f);

        float open = Math.Max(_floor * _openFactor, 0.006f);
        if (rms > open) _hold = 10;          // mantém aberta por ~200 ms após a fala
        else if (_hold > 0) _hold--;

        float target = _hold > 0 ? 1f : _closedGain;
        float g0 = _gate;
        float g1 = g0 + (target - g0) * (target > g0 ? 0.6f : 0.25f);
        _gate = g1;

        int frames = count / 2;
        for (int f = 0; f < frames; f++)
        {
            float g = g0 + (g1 - g0) * (f + 1) / frames; // rampa linear dentro do bloco (sem "cliques")
            buf[2 * f] *= g; buf[2 * f + 1] *= g;
        }
    }
}
