using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using TelaRec.Core;
using TelaRec.Native;
using TelaRec.Services;

namespace TelaRec;

public partial class MainWindow : Window
{
    private readonly string _settingsPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TelaRec", "settings.json");

    private AppSettings _cfg;
    private List<MonitorInfo> _monitors = new();
    private MonitorInfo? _selected;
    private readonly Dictionary<string, BitmapSource?> _thumbs = new();
    private Recorder? _rec;
    private VideoEncoder _hw = VideoEncoder.Cpu;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private int _ticks;
    private bool _busy, _closing, _closeAfter, _warnedDisk;
    private string? _lastFile, _folderToOpen;
    private bool _affinityApplied;
    private readonly Dictionary<string, Border> _cards = new();
    private readonly MouseHook _hook = new();
    private ClickOverlay? _overlay;
    private MonitorInfo? _recTarget;
    private bool _fxEnabled, _fxSound;
    private CaptureSource? _testSrc;
    private MicProcessor? _testProc;
    private readonly DispatcherTimer _meterTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly float[] _meterBuf = new float[9600];
    private double _meterLevel;
    private int _silentTicks;
    private readonly DispatcherTimer _liveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };

    public MainWindow()
    {
        InitializeComponent();
        _cfg = AppSettings.Load(_settingsPath);

        FolderBox.Text = _cfg.OutputFolder;
        NameBox.Text = _cfg.FileName;
        (_cfg.Fps == 60 ? Fps60 : Fps30).IsChecked = true;
        switch (_cfg.Quality)
        {
            case VideoQuality.Economy: QEco.IsChecked = true; break;
            case VideoQuality.High: QHigh.IsChecked = true; break;
            default: QBal.IsChecked = true; break;
        }
        HwCheck.IsChecked = _cfg.UseHardware;
        HideCheck.IsChecked = _cfg.HideWindowFromCapture;
        ClickFxCheck.IsChecked = _cfg.ClickEffect;
        ClickSoundCheck.IsChecked = _cfg.ClickSound;
        SysAudioCheck.IsChecked = _cfg.RecordSystemAudio;
        MicCheck.IsChecked = _cfg.RecordMic;
        DenoiseCheck.IsChecked = _cfg.Denoise;
        SysGainSlider.Value = Math.Clamp(_cfg.SystemGainPct, 0, 200);
        MicGainSlider.Value = Math.Clamp(_cfg.MicGainPct, 0, 300);
        SysGainText.Text = (int)SysGainSlider.Value + "%";
        MicGainText.Text = (int)MicGainSlider.Value + "%";
        SyncBox.Text = Math.Clamp(_cfg.AudioSyncAdjustMs, -2000, 2000).ToString();
        switch (_cfg.DenoiseLevel) { case 0: DnLow.IsChecked = true; break; case 2: DnHigh.IsChecked = true; break; default: DnMed.IsChecked = true; break; }
        // Eventos dos sliders são ligados aqui (e não no XAML) para não dispararem durante a construção da janela.
        SysGainSlider.ValueChanged += (_, _) => SysGainText.Text = (int)SysGainSlider.Value + "%";
        MicGainSlider.ValueChanged += (_, _) =>
        {
            MicGainText.Text = (int)MicGainSlider.Value + "%";
            if (_testSrc != null) _testProc = NewMicProcessor();
        };
        _meterTimer.Tick += MeterTick;
        UpdateAudioUi();
        if (_cfg.RecordMic) LoadMics();
        InitDriveUi();

        _timer.Tick += Timer_Tick;
        _liveTimer.Tick += (_, _) => LiveTick();
        Loaded += async (_, _) => await InitAsync();
        Closing += Window_Closing;
        Closed += (_, _) => { _liveTimer.Stop(); StopMicTest(); StopClickFx(); _hook.Dispose(); };
        Closed += (_, _) => SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    // ------------------------------------------------------------------ inicialização
    private async Task InitAsync()
    {
        ShowInfo("Preparando o motor de vídeo (apenas na primeira execução)…");
        var (path, error) = await Task.Run(FfmpegProvider.Ensure);
        if (path == null) { ShowError(error ?? "Falha ao preparar o motor de vídeo."); return; }

        _rec = new Recorder(path);
        _rec.StateChanged += s => Dispatcher.InvokeAsync(() => ApplyState(s));
        _rec.Warning += m => Dispatcher.InvokeAsync(() => ShowInfo(m));
        _rec.Interrupted += r => Dispatcher.InvokeAsync(() => ShowResult(r));

        RefreshMonitors();
        _liveTimer.Start();
        ShowInfo("Verificando aceleração de hardware…");
        _hw = await EncoderProbe.DetectBestAsync(path);
        EncoderInfo.Text = _hw == VideoEncoder.Cpu
            ? "Codificador: CPU (x264). Nenhum codificador de hardware compatível foi encontrado."
            : $"Codificador de hardware disponível: {EncoderProbe.Describe(_hw)}.";
        HideMessage();
        RecButton.IsEnabled = _monitors.Count > 0;
    }

    // ------------------------------------------------------------------ monitores
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(async () => { await Task.Delay(700); RefreshMonitors(); });

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshMonitors();

    private void RefreshMonitors()
    {
        List<MonitorInfo> list;
        try { list = DxgiMap.Annotate(MonitorEnumerator.GetAll()); }
        catch (Exception ex) { ShowError("Não foi possível detectar os monitores: " + ex.Message); return; }

        _monitors = list;
        var rec = _rec;

        if (rec != null && rec.State is RecState.Recording or RecState.Paused)
        {
            // Durante a gravação o monitor escolhido é validado, nunca trocado silenciosamente.
            var still = list.FirstOrDefault(m => m.DeviceName == _selected?.DeviceName);
            if (still == null || !still.SameGeometryAs(_selected))
                _ = StopBecauseMonitorChangedAsync();
            DrawMonitors();
            return;
        }

        foreach (var m in list) _thumbs[m.DeviceName] = ScreenThumbnail.Capture(m);

        var previous = _selected;
        _selected = list.FirstOrDefault(m => previous != null && m.DeviceName == previous.DeviceName)
                    ?? list.FirstOrDefault(m => m.IsPrimary) ?? list.FirstOrDefault();
        if (previous != null && _selected != null && previous.DeviceName != _selected.DeviceName)
            ShowInfo("A configuração de monitores mudou. Confira qual tela está selecionada.");

        if (list.Count == 0) ShowError("Nenhum monitor foi detectado.");
        RecButton.IsEnabled = _rec != null && list.Count > 0 && !_busy;
        DrawMonitors();
        UpdateSelectedInfo();
    }

    private async Task StopBecauseMonitorChangedAsync()
    {
        if (_rec == null) return;
        var r = await _rec.FinishAsync(
            "O monitor selecionado foi desconectado ou teve a configuração alterada; a gravação foi finalizada com segurança.");
        ShowResult(r);
    }

    /// <summary>Atualiza as miniaturas ~1x por segundo para mostrar o conteúdo ao vivo de cada tela.</summary>
    private void LiveTick()
    {
        if (_rec == null || WindowState == WindowState.Minimized || !IsVisible) return;
        if (_rec.State is RecState.Recording or RecState.Paused or RecState.Finalizing) return;
        foreach (var m in _monitors)
        {
            if (!_cards.TryGetValue(m.DeviceName, out var card)) continue;
            var th = ScreenThumbnail.Capture(m);
            if (th == null) continue;
            _thumbs[m.DeviceName] = th;
            card.Background = new ImageBrush(th) { Stretch = Stretch.Fill };
        }
    }

    private void LayoutHost_SizeChanged(object sender, SizeChangedEventArgs e) => DrawMonitors();

    private void SelectMonitor(MonitorInfo m)
    {
        if (_rec != null && _rec.State is RecState.Recording or RecState.Paused or RecState.Finalizing) return;
        _selected = m;
        DrawMonitors();
        UpdateSelectedInfo();
    }

    private void UpdateSelectedInfo()
    {
        if (_selected == null) { SelectedInfo.Text = "Nenhum monitor selecionado"; SelectedInfo2.Text = ""; return; }
        var m = _selected;
        SelectedInfo.Text = $"{m.Name}  ·  {m.Resolution}" + (m.IsPrimary ? "  ·  Principal" : "");
        SelectedInfo2.Text = $"{m.Orientation}  ·  Escala do Windows {m.DpiPercent}%  ·  Posição ({m.X}, {m.Y})  ·  {_monitors.Count} monitor(es) detectado(s)";
    }

    private void DrawMonitors()
    {
        MonitorCanvas.Children.Clear();
        _cards.Clear();
        if (_monitors.Count == 0) return;
        double hostW = LayoutHost.ActualWidth, hostH = LayoutHost.ActualHeight;
        if (hostW < 60 || hostH < 60) return;

        int minX = _monitors.Min(m => m.X), minY = _monitors.Min(m => m.Y);
        int maxX = _monitors.Max(m => m.X + m.Width), maxY = _monitors.Max(m => m.Y + m.Height);
        double totW = maxX - minX, totH = maxY - minY;
        const double pad = 12;
        double s = Math.Min((hostW - 2 * pad) / totW, (hostH - 2 * pad) / totH);
        double ox = (hostW - totW * s) / 2, oy = (hostH - totH * s) / 2;

        var accent = (Brush)FindResource("Accent");
        var border = (Brush)FindResource("Border");
        var surface2 = (Brush)FindResource("Surface2");

        foreach (var m in _monitors)
        {
            bool sel = _selected?.DeviceName == m.DeviceName;
            var card = new Border
            {
                Width = Math.Max(24, m.Width * s - 4),
                Height = Math.Max(24, m.Height * s - 4),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(sel ? 3 : 1),
                BorderBrush = sel ? accent : border,
                Background = surface2,
                Opacity = sel ? 1.0 : 0.75,
                Cursor = Cursors.Hand,
                ToolTip = $"{m.Name} — {m.Resolution}",
            };
            if (_thumbs.TryGetValue(m.DeviceName, out var th) && th != null)
                card.Background = new ImageBrush(th) { Stretch = Stretch.Fill };

            card.Child = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(190, 15, 17, 21)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(6),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Child = new TextBlock
                {
                    Text = $"{m.Number}  ·  {m.Resolution}",
                    Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold,
                },
            };

            var captured = m;
            card.MouseLeftButtonUp += (_, _) => SelectMonitor(captured);
            Canvas.SetLeft(card, ox + (m.X - minX) * s + 2);
            Canvas.SetTop(card, oy + (m.Y - minY) * s + 2);
            MonitorCanvas.Children.Add(card);
            _cards[m.DeviceName] = card;
        }
    }

    // ------------------------------------------------------------------ gravação
    private AppSettings ReadSettings()
    {
        _cfg.OutputFolder = FolderBox.Text.Trim();
        _cfg.FileName = NameBox.Text.Trim();
        _cfg.Fps = Fps60.IsChecked == true ? 60 : 30;
        _cfg.Quality = QEco.IsChecked == true ? VideoQuality.Economy : QHigh.IsChecked == true ? VideoQuality.High : VideoQuality.Balanced;
        _cfg.UseHardware = HwCheck.IsChecked == true;
        _cfg.HideWindowFromCapture = HideCheck.IsChecked == true;
        _cfg.ClickEffect = ClickFxCheck.IsChecked == true;
        _cfg.ClickSound = ClickSoundCheck.IsChecked == true;
        _cfg.RecordSystemAudio = SysAudioCheck.IsChecked == true;
        _cfg.RecordMic = MicCheck.IsChecked == true;
        _cfg.Denoise = DenoiseCheck.IsChecked == true;
        _cfg.SystemGainPct = (int)SysGainSlider.Value;
        _cfg.MicGainPct = (int)MicGainSlider.Value;
        _cfg.DenoiseLevel = DenoiseLevelIndex();
        if (SelectedMic != null) _cfg.MicDeviceId = SelectedMic.Id;
        _cfg.AudioSyncAdjustMs = int.TryParse(SyncBox.Text.Trim(), out var sd) ? Math.Clamp(sd, -2000, 2000) : 0;
        ReadDriveSettings(_cfg);
        return _cfg;
    }

    private async void RecButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _rec == null || _selected == null) return;
        _busy = true;
        try
        {
            var cfg = ReadSettings();
            cfg.Save(_settingsPath);

            if (string.IsNullOrWhiteSpace(cfg.OutputFolder)) { ShowError("Escolha uma pasta para salvar os vídeos."); return; }

            // Revalida o monitor imediatamente antes de gravar (evita gravar a tela errada).
            var fresh = DxgiMap.Annotate(MonitorEnumerator.GetAll());
            _monitors = fresh;
            var target = fresh.FirstOrDefault(m => m.DeviceName == _selected.DeviceName);
            if (target == null)
            {
                RefreshMonitors();
                ShowError("O monitor selecionado não está mais disponível. Selecione outro e tente novamente.");
                return;
            }
            if (!target.SameGeometryAs(_selected)) _selected = target;

            var mic = SelectedMic;
            if (cfg.RecordMic && mic == null)
            {
                ShowError("Selecione um microfone na lista (use \"Detectar microfones\") ou desmarque \"Gravar o microfone\".");
                return;
            }
            StopMicTest();
            var audio = (cfg.RecordSystemAudio || cfg.RecordMic)
                ? new AudioOptions(cfg.RecordSystemAudio, cfg.RecordMic, mic?.Id, cfg.SystemGainPct / 100f,
                    cfg.MicGainPct / 100f, cfg.Denoise, cfg.DenoiseLevel, cfg.AudioSyncAdjustMs)
                : AudioOptions.None;

            long? free = DiskSpace.FreeBytes(cfg.OutputFolder);
            if (free != null && free < 500L * 1024 * 1024)
            {
                ShowError($"Espaço em disco insuficiente ({TimeFormat.Bytes(free.Value)} livres). Libere espaço ou escolha outra pasta.");
                return;
            }

            var name = FileNamer.Sanitize(cfg.FileName);
            if (name.Length == 0) name = FileNamer.DefaultName(DateTime.Now);
            string path;
            try { path = FileNamer.UniquePath(cfg.OutputFolder, name); }
            catch (Exception ex) { ShowError("Pasta de destino inválida: " + ex.Message); return; }

            bool allowGdi = fresh.All(m => m.DpiPercent == 100);
            var enc = cfg.UseHardware ? _hw : VideoEncoder.Cpu;

            RecButton.IsEnabled = false;
            ShowInfo("Iniciando a captura…");
            _warnedDisk = false; _ticks = 0;
            TimerText.Text = "00:00:00";

            _fxEnabled = cfg.ClickEffect; _fxSound = cfg.ClickSound; _recTarget = target;
            var err = await _rec.StartAsync(target, allowGdi, cfg.Fps, cfg.Quality, enc, path, audio);
            if (err != null)
            {
                ShowError(err);
                ApplyState(RecState.Error);
                return;
            }

            _lastFile = null;
            if (!string.IsNullOrEmpty(cfg.OutputFolder)) HideMessageIfNoWarning();
            SetCaptureExclusion(cfg.HideWindowFromCapture);
            _timer.Start();
        }
        finally { _busy = false; }
    }

    private async void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _rec == null) return;
        _busy = true;
        PauseButton.IsEnabled = false;
        try
        {
            if (_rec.State == RecState.Recording) await _rec.PauseAsync();
            else if (_rec.State == RecState.Paused)
            {
                var err = await _rec.ResumeAsync();
                if (err != null) ShowError(err);
            }
        }
        finally { _busy = false; PauseButton.IsEnabled = true; }
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _rec == null) return;
        _busy = true;
        try
        {
            ShowInfo("Finalizando e salvando o vídeo… não feche o programa.");
            var r = await _rec.FinishAsync();
            ShowResult(r);
        }
        finally { _busy = false; if (_closeAfter) { _closing = true; Close(); } }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_rec == null) return;
        TimerText.Text = TimeFormat.Format(_rec.Elapsed);
        if (_rec.State != RecState.Recording) return;
        if (++_ticks % 20 == 0) _ = CheckDiskAsync();
    }

    private async Task CheckDiskAsync()
    {
        if (_rec == null) return;
        var free = DiskSpace.FreeBytes(_rec.OutputFolder);
        if (free == null) return;
        if (free < 1024L * 1024 * 1024)
        {
            var r = await _rec.FinishAsync("O espaço em disco está acabando; a gravação foi finalizada para proteger o arquivo.");
            ShowResult(r);
        }
        else if (free < 3L * 1024 * 1024 * 1024 && !_warnedDisk)
        {
            _warnedDisk = true;
            ShowInfo($"Atenção: restam apenas {TimeFormat.Bytes(free.Value)} no disco de destino.");
        }
    }

    // ------------------------------------------------------------------ estado da interface
    private void ApplyState(RecState s)
    {
        var green = (Brush)FindResource("Green");
        var red = (Brush)FindResource("Red");
        var amber = (Brush)FindResource("Amber");
        var accent = (Brush)FindResource("Accent");

        StatusDot.BeginAnimation(OpacityProperty, null);
        StatusDot.Opacity = 1;
        bool active = s is RecState.Recording or RecState.Paused or RecState.Finalizing;

        SettingsPanel.IsEnabled = !active;
        AudioPanel.IsEnabled = !active;
        RefreshButton.IsEnabled = !active;
        RecButton.Visibility = (s is RecState.Recording or RecState.Paused or RecState.Finalizing) ? Visibility.Collapsed : Visibility.Visible;
        PauseButton.Visibility = (s is RecState.Recording or RecState.Paused) ? Visibility.Visible : Visibility.Collapsed;
        StopButton.Visibility = (s is RecState.Recording or RecState.Paused) ? Visibility.Visible : Visibility.Collapsed;

        switch (s)
        {
            case RecState.Ready:
                StatusDot.Fill = green; StatusText.Text = "Pronto para gravar"; Title = "TelaRec"; StopClickFx();
                RecButton.IsEnabled = _monitors.Count > 0; break;
            case RecState.Recording:
                StatusDot.Fill = red; StatusText.Text = "Gravando"; Title = "● Gravando — TelaRec"; StartClickFx();
                PauseButton.Content = "Pausar";
                StatusDot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(700))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                break;
            case RecState.Paused:
                StatusDot.Fill = amber; StatusText.Text = "Gravação pausada"; Title = "⏸ Pausado — TelaRec"; PauseClickFx();
                PauseButton.Content = "Retomar"; break;
            case RecState.Finalizing:
                StatusDot.Fill = accent; StatusText.Text = "Finalizando e salvando o vídeo…"; PauseClickFx(); break;
            case RecState.Done:
                _timer.Stop(); ClearCaptureExclusion(); StopClickFx(); TimerText.Text = "00:00:00";
                StatusDot.Fill = green; StatusText.Text = "Gravação concluída"; Title = "TelaRec";
                RecButton.IsEnabled = _monitors.Count > 0; break;
            case RecState.Error:
                _timer.Stop(); ClearCaptureExclusion(); StopClickFx(); TimerText.Text = "00:00:00";
                StatusDot.Fill = red; StatusText.Text = "Erro de gravação"; Title = "TelaRec";
                RecButton.IsEnabled = _monitors.Count > 0; break;
        }
    }

    private void SetCaptureExclusion(bool on)
    {
        if (!on) return;
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return;
        _affinityApplied = NativeMethods.SetWindowDisplayAffinity(h, 0x11); // WDA_EXCLUDEFROMCAPTURE
        if (!_affinityApplied) WindowState = WindowState.Minimized;
    }

    private void ClearCaptureExclusion()
    {
        if (!_affinityApplied) return;
        var h = new WindowInteropHelper(this).Handle;
        if (h != IntPtr.Zero) NativeMethods.SetWindowDisplayAffinity(h, 0);
        _affinityApplied = false;
    }

    // ------------------------------------------------------------------ áudio
    private MicDevice? SelectedMic => MicList.SelectedItem as MicDevice;

    private int DenoiseLevelIndex() => DnLow.IsChecked == true ? 0 : DnHigh.IsChecked == true ? 2 : 1;

    private MicProcessor NewMicProcessor() =>
        new((float)(MicGainSlider.Value / 100.0), DenoiseCheck.IsChecked == true, DenoiseLevelIndex());

    private void UpdateAudioUi()
    {
        bool sys = SysAudioCheck.IsChecked == true, mic = MicCheck.IsChecked == true;
        SysOptions.Visibility = sys ? Visibility.Visible : Visibility.Collapsed;
        MicOptions.Visibility = mic ? Visibility.Visible : Visibility.Collapsed;
        SyncPanel.Visibility = (sys || mic) ? Visibility.Visible : Visibility.Collapsed;
        DenoiseOptions.IsEnabled = DenoiseCheck.IsChecked == true;
    }

    private void AudioOption_Click(object sender, RoutedEventArgs e)
    {
        UpdateAudioUi();
        if (MicCheck.IsChecked == true) { if (MicList.Items.Count == 0) LoadMics(); }
        else StopMicTest();
        if (_testSrc != null) _testProc = NewMicProcessor();
    }

    private void DenoiseLevel_Click(object sender, RoutedEventArgs e)
    {
        if (_testSrc != null) _testProc = NewMicProcessor();
    }

    private void RefreshMicsButton_Click(object sender, RoutedEventArgs e) => LoadMics();

    private void MicList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_testSrc != null) StartMicTest(); // reinicia o teste com o novo microfone
    }

    private void LoadMics()
    {
        try
        {
            var keep = SelectedMic?.Id ?? _cfg.MicDeviceId;
            var list = AudioDevices.ListMics();
            MicList.ItemsSource = list;
            MicList.SelectedItem = list.FirstOrDefault(m => m.Id == keep) ?? list.FirstOrDefault(m => m.IsDefault) ?? list.FirstOrDefault();
            MicTestHint.Text = list.Count == 0
                ? "Nenhum microfone foi encontrado. Conecte um e clique em \"Detectar microfones\"."
                : $"{list.Count} microfone(s) detectado(s). Clique em \"Testar microfone\" e fale: a barra deve se mexer.";
        }
        catch (Exception ex)
        {
            MicTestHint.Text = "Não foi possível listar os microfones: " + ex.Message;
        }
    }

    private void MicTestButton_Click(object sender, RoutedEventArgs e)
    {
        if (_testSrc != null) StopMicTest(); else StartMicTest();
    }

    private void StartMicTest()
    {
        var mic = SelectedMic;
        if (mic == null) { MicTestHint.Text = "Selecione um microfone na lista."; return; }
        StopMicTest();
        try
        {
            using var en = new MMDeviceEnumerator();
            var cap = new WasapiCapture(en.GetDevice(mic.Id));
            _testSrc = new CaptureSource(cap);
            _testSrc.Start();
            _testProc = NewMicProcessor();
            _meterLevel = 0; _silentTicks = 0;
            _meterTimer.Start();
            MicTestButton.Content = "Parar teste";
            MicTestHint.Text = "Fale normalmente. Ajuste a captação até a barra chegar perto do fim nas falas mais altas, sem encostar no limite.";
        }
        catch (Exception ex)
        {
            StopMicTest();
            MicTestHint.Text = "Não foi possível abrir o microfone: " + ex.Message +
                " Verifique Configurações do Windows → Privacidade → Microfone.";
        }
    }

    private void StopMicTest()
    {
        _meterTimer.Stop();
        if (_testSrc != null) { _testSrc.Stop(); _testSrc.Dispose(); _testSrc = null; }
        _testProc = null;
        MicMeter.Value = 0;
        MicTestButton.Content = "Testar microfone";
    }

    private void MeterTick(object? sender, EventArgs e)
    {
        if (_testSrc == null || _testProc == null) return;
        int n = _testSrc.Read(_meterBuf, _meterBuf.Length) & ~1;
        float peak = 0;
        if (n > 0) { _testProc.Process(_meterBuf, n); peak = AudioMath.Peak(_meterBuf, n); }
        _silentTicks = peak < 0.0005f ? _silentTicks + 1 : 0;
        _meterLevel = Math.Max(Math.Sqrt(Math.Min(1f, peak)), _meterLevel * 0.85);
        MicMeter.Value = Math.Min(1.0, _meterLevel);
        if (_silentTicks == 100)
            MicTestHint.Text = "Nenhum som detectado. Confira se é o microfone certo e se o Windows permite que aplicativos de desktop usem o microfone (Configurações → Privacidade → Microfone).";
    }

    // ------------------------------------------------------------------ efeito de clique
    private void ClickHelpButton_Click(object sender, RoutedEventArgs e) => ClickHelpPopup.IsOpen = true;

    private void StartClickFx()
    {
        if (!_fxEnabled || _recTarget == null) return;
        try
        {
            if (_overlay == null) { _overlay = new ClickOverlay(_recTarget); _overlay.Show(); }
            _hook.Click -= OnGlobalClick;
            _hook.Click += OnGlobalClick;
            _hook.Start();
        }
        catch (Exception ex)
        {
            ShowInfo("Aviso: não foi possível ativar o efeito de clique (" + ex.Message + "). A gravação continua normalmente.");
        }
    }

    private void PauseClickFx() => _hook.Stop();

    private void StopClickFx()
    {
        _hook.Stop();
        if (_overlay != null) { try { _overlay.Close(); } catch { } _overlay = null; }
    }

    private void OnGlobalClick(int x, int y, bool left)
    {
        var mon = _recTarget;
        if (mon == null || _overlay == null) return;
        if (x < mon.X || y < mon.Y || x >= mon.X + mon.Width || y >= mon.Y + mon.Height) return; // só o monitor gravado
        Dispatcher.InvokeAsync(() =>
        {
            _overlay?.Ripple(x, y, left);
            if (_fxSound) ClickSounds.Play(left);
        });
    }

    // ------------------------------------------------------------------ mensagens
    private void ShowInfo(string text) => SetMessage(text, (Brush)FindResource("Text"), false);
    private void ShowError(string text) => SetMessage(text, (Brush)FindResource("Red"), false);

    private void SetMessage(string text, Brush color, bool actions)
    {
        MessageText.Text = text; MessageText.Foreground = color;
        ActionsPanel.Visibility = actions ? Visibility.Visible : Visibility.Collapsed;
        MessageBorder.Visibility = Visibility.Visible;
    }

    private void HideMessage() => MessageBorder.Visibility = Visibility.Collapsed;
    private void HideMessageIfNoWarning()
    {
        if (!MessageText.Text.StartsWith("Aviso")) HideMessage();
    }

    private void ShowResult(RecordingResult r)
    {
        if (r.Ignored) return;
        _lastFile = r.FilePath;
        _folderToOpen = r.FilePath != null ? System.IO.Path.GetDirectoryName(r.FilePath) : r.KeptFolder;
        SetMessage(r.Message, (Brush)FindResource(r.Success ? "Green" : "Red"), _folderToOpen != null);
        PlayButton.Visibility = _lastFile != null ? Visibility.Visible : Visibility.Collapsed;
        DriveButton.Visibility = (_lastFile != null && DriveConfigured()) ? Visibility.Visible : Visibility.Collapsed;
        if (r.Success && r.FilePath != null) _ = OfferDriveAsync(r.FilePath);
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_lastFile != null && File.Exists(_lastFile))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_lastFile}\"") { UseShellExecute = true });
            else if (_folderToOpen != null && Directory.Exists(_folderToOpen))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_folderToOpen}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { ShowError("Não foi possível abrir a pasta: " + ex.Message); }
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        try { if (_lastFile != null) Process.Start(new ProcessStartInfo(_lastFile) { UseShellExecute = true }); }
        catch (Exception ex) { ShowError("Não foi possível abrir o vídeo: " + ex.Message); }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Escolha a pasta para salvar as gravações" };
        if (Directory.Exists(FolderBox.Text)) dlg.InitialDirectory = FolderBox.Text;
        if (dlg.ShowDialog(this) == true) FolderBox.Text = dlg.FolderName;
    }

    // ------------------------------------------------------------------ encerramento
    private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closing) { ReadSettings().Save(_settingsPath); _rec?.Dispose(); return; }
        var rec = _rec;
        if (rec != null && rec.State is RecState.Recording or RecState.Paused or RecState.Finalizing)
        {
            e.Cancel = true;
            if (rec.State == RecState.Finalizing) { _closeAfter = true; return; }

            var ans = MessageBox.Show(this,
                "Há uma gravação em andamento. Deseja finalizar e salvar o vídeo antes de sair?",
                "TelaRec", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ans != MessageBoxResult.Yes) return;

            var r = await rec.FinishAsync();
            if (!r.Success && !r.Ignored) MessageBox.Show(this, r.Message, "TelaRec", MessageBoxButton.OK, MessageBoxImage.Warning);
            _closing = true;
            Close();
            return;
        }
        ReadSettings().Save(_settingsPath);
        rec?.Dispose();
    }
}
