using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using TelaRec.Core;
using TelaRec.Native;
using TelaRec.Services;

namespace TelaRec;

/// <summary>
/// Barra compacta encostada no topo do monitor escolhido. Mostra o estado (pronto / gravando / pausado), o cronômetro e
/// os botões: gravar (quando parado), pausar/continuar e parar (durante a gravação). Fica fora do vídeo gravado
/// (exclusão de captura do Windows) e não rouba o foco do aplicativo em uso.
/// </summary>
internal sealed class RecordingToolbar : Window
{
    private MonitorInfo _mon;
    private readonly Ellipse _dot = new() { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
    private readonly TextBlock _status = new() { FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
    private readonly TextBlock _time = new() { Text = "00:00:00", FontFamily = new FontFamily("Consolas"), FontSize = 20, FontWeight = FontWeights.Light, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _record, _pause, _stop;

    public event Action? RecordClicked;
    public event Action? PauseToggleClicked;
    public event Action? StopClicked;

    /// <summary>Posição e tamanho da barra em pixels físicos (para ignorar cliques nela no efeito de clique).</summary>
    public Rect PhysicalBounds { get; private set; }

    public RecordingToolbar(MonitorInfo mon)
    {
        _mon = mon;
        Title = "TelaRec-barra";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = WinUi.Res("Surface");
        Foreground = WinUi.Res("Text");
        FontFamily = new FontFamily("Segoe UI");
        Left = mon.X; Top = mon.Y;

        _time.Foreground = WinUi.Res("Text");

        _record = IconButton(RecordIcon(), WinUi.Res("Red"), false);
        _record.ToolTip = "Iniciar a gravação";
        _record.Margin = new Thickness(0);
        _record.Click += (_, _) => RecordClicked?.Invoke();
        _pause = IconButton(PauseIcon(), WinUi.Res("Surface2"), true);
        _pause.Click += (_, _) => PauseToggleClicked?.Invoke();
        _stop = IconButton(StopIcon(), WinUi.Res("Red"), false);
        _stop.Margin = new Thickness(0);
        _stop.ToolTip = "Parar e salvar a gravação";
        _stop.Click += (_, _) => StopClicked?.Invoke();

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(_dot);
        row.Children.Add(_status);
        row.Children.Add(_time);
        row.Children.Add(new Border { Width = 1, Height = 24, Background = WinUi.Res("Border"), Margin = new Thickness(16, 0, 14, 0) });
        row.Children.Add(_record);
        row.Children.Add(_pause);
        row.Children.Add(_stop);

        // Sem borda em cima: a barra encosta literalmente no topo da tela.
        Content = new Border
        {
            BorderBrush = WinUi.Res("Border"), BorderThickness = new Thickness(1, 0, 1, 1),
            Padding = new Thickness(16, 5, 12, 5), Child = row,
        };

        SourceInitialized += (_, _) => Setup();
        ContentRendered += (_, _) => Place();
        SizeChanged += (_, _) => Place();
        DpiChanged += (_, _) => Place();
        SetMode(RecState.Ready, false);
    }

    private IntPtr Hwnd => new WindowInteropHelper(this).Handle;

    private void Setup()
    {
        var h = Hwnd;
        int ex = NativeMethods.GetWindowLong(h, -20);                          // GWL_EXSTYLE
        NativeMethods.SetWindowLong(h, -20, ex | 0x80 | 0x08000000);           // TOOLWINDOW | NOACTIVATE: sem Alt+Tab, sem roubar foco
        NativeMethods.SetWindowDisplayAffinity(h, 0x11);                       // WDA_EXCLUDEFROMCAPTURE: fora do vídeo
        int noRound = 1;                                                       // DWMWCP_DONOTROUND: o arredondamento é feito por região (só embaixo)
        NativeMethods.DwmSetWindowAttribute(h, 33, ref noRound, sizeof(int));
        Place();
    }

    /// <summary>Move a barra para outro monitor (o escolhido nas configurações).</summary>
    public void MoveTo(MonitorInfo mon)
    {
        if (mon.DeviceName == _mon.DeviceName && mon.X == _mon.X && mon.Y == _mon.Y && mon.Width == _mon.Width) return;
        _mon = mon;
        Place();
    }

    /// <summary>Centraliza no topo do monitor, encostada na borda (y = topo do monitor), em pixels físicos.</summary>
    private void Place()
    {
        var h = Hwnd;
        if (h == IntPtr.Zero) return;
        double sx = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (sx <= 0) sx = 1;
        int w = (int)Math.Round(ActualWidth * sx), ht = (int)Math.Round(ActualHeight * sx);
        if (w <= 0 || ht <= 0) return;
        int x = _mon.X + (_mon.Width - w) / 2;
        int y = _mon.Y;
        NativeMethods.SetWindowPos(h, new IntPtr(-1), x, y, w, ht, 0x0010 | 0x0040);   // NOACTIVATE | SHOWWINDOW
        PhysicalBounds = new Rect(x, y, w, ht);

        // Cantos arredondados só embaixo: a região começa acima da borda de cima, escondendo os cantos superiores.
        int r = (int)Math.Round(12 * sx);
        NativeMethods.SetWindowRgn(h, NativeMethods.CreateRoundRectRgn(0, -r, w + 1, ht + 1, r * 2, r * 2), true);
    }

    public void SetTime(string text) => _time.Text = text;

    public void SetMode(RecState s, bool resuming)
    {
        _dot.BeginAnimation(OpacityProperty, null);
        _dot.Opacity = 1;
        bool controls = s is RecState.Starting or RecState.Recording or RecState.Paused or RecState.Finalizing;
        _record.Visibility = controls ? Visibility.Collapsed : Visibility.Visible;
        _pause.Visibility = controls ? Visibility.Visible : Visibility.Collapsed;
        _stop.Visibility = controls ? Visibility.Visible : Visibility.Collapsed;

        switch (s)
        {
            case RecState.Recording:
                _status.Text = "GRAVANDO"; _status.Foreground = WinUi.Res("Red");
                _dot.Fill = WinUi.Res("Red"); Pulse();
                _pause.Content = PauseIcon(); _pause.ToolTip = "Pausar a gravação";
                _pause.IsEnabled = true; _stop.IsEnabled = true;
                break;
            case RecState.Paused:
                _status.Text = "PAUSADO"; _status.Foreground = WinUi.Res("Amber");
                _dot.Fill = WinUi.Res("Amber");
                _pause.Content = PlayIcon(); _pause.ToolTip = "Continuar a gravação";
                _pause.IsEnabled = true; _stop.IsEnabled = true;
                break;
            case RecState.Finalizing:
                _status.Text = "SALVANDO…"; _status.Foreground = WinUi.Res("Accent");
                _dot.Fill = WinUi.Res("Accent"); Pulse();
                _pause.IsEnabled = false; _stop.IsEnabled = false;
                break;
            case RecState.Starting:
                _status.Text = resuming ? "RETOMANDO…" : "INICIANDO…"; _status.Foreground = WinUi.Res("Accent");
                _dot.Fill = WinUi.Res("Accent"); Pulse();
                _pause.Content = PauseIcon(); _pause.ToolTip = "Pausar a gravação";
                _pause.IsEnabled = false; _stop.IsEnabled = false;
                break;
            default: // Pronto (parado)
                _status.Text = "PRONTO"; _status.Foreground = WinUi.Res("Green");
                _dot.Fill = WinUi.Res("Green");
                break;
        }
    }

    private void Pulse() =>
        _dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.3, TimeSpan.FromMilliseconds(700))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });

    private static Button IconButton(UIElement icon, Brush background, bool bordered) => new()
    {
        Style = WinUi.Sty("IconBtn"), Width = 34, Height = 34, Background = background, Content = icon, Margin = new Thickness(0, 0, 8, 0),
        BorderBrush = WinUi.Res("Border"), BorderThickness = new Thickness(bordered ? 1 : 0),
    };

    private static UIElement PauseIcon()
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new Rectangle { Width = 5, Height = 15, RadiusX = 1.5, RadiusY = 1.5, Fill = Brushes.White, Margin = new Thickness(0, 0, 4, 0) });
        sp.Children.Add(new Rectangle { Width = 5, Height = 15, RadiusX = 1.5, RadiusY = 1.5, Fill = Brushes.White });
        return sp;
    }

    private static UIElement PlayIcon() => new Polygon
    {
        Points = new PointCollection { new Point(0, 0), new Point(0, 16), new Point(14, 8) },
        Fill = Brushes.White, Margin = new Thickness(3, 0, 0, 0), StrokeLineJoin = PenLineJoin.Round,
    };

    private static UIElement StopIcon() => new Rectangle { Width = 13, Height = 13, RadiusX = 3, RadiusY = 3, Fill = Brushes.White };

    private static UIElement RecordIcon() => new Ellipse { Width = 14, Height = 14, Fill = Brushes.White };
}
