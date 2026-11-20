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
/// Barra flutuante exibida durante a gravação, fixa e centralizada no topo do monitor gravado:
/// estado (gravando/pausado), cronômetro, botão pausar/continuar e botão parar.
/// É excluída da captura (não aparece no vídeo) e não rouba o foco do que você está fazendo.
/// </summary>
internal sealed class RecordingToolbar : Window
{
    private readonly MonitorInfo _mon;
    private readonly Ellipse _dot = new() { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
    private readonly TextBlock _status = new() { FontSize = 12, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _time = new() { Text = "00:00:00", FontFamily = new FontFamily("Consolas"), FontSize = 30, FontWeight = FontWeights.Light };
    private readonly Button _pause, _stop;

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

        _status.Foreground = WinUi.Res("Muted");
        _time.Foreground = WinUi.Res("Text");

        _pause = IconButton(PauseIcon(), WinUi.Res("Surface2"), true);
        _pause.Click += (_, _) => PauseToggleClicked?.Invoke();
        _stop = IconButton(StopIcon(), WinUi.Res("Red"), false);
        _stop.Margin = new Thickness(0);
        _stop.ToolTip = "Parar e salvar a gravação";
        _stop.Click += (_, _) => StopClicked?.Invoke();

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(_status);
        texts.Children.Add(_time);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_dot);
        row.Children.Add(texts);
        row.Children.Add(new Border { Width = 1, Height = 40, Background = WinUi.Res("Border"), Margin = new Thickness(22, 0, 20, 0) });
        row.Children.Add(_pause);
        row.Children.Add(_stop);

        Content = new Border
        {
            BorderBrush = WinUi.Res("Border"), BorderThickness = new Thickness(1),
            Padding = new Thickness(22, 14, 18, 14), Child = row,
        };

        SourceInitialized += (_, _) => Setup();
        ContentRendered += (_, _) => Place();
        SizeChanged += (_, _) => Place();
        DpiChanged += (_, _) => Place();
        SetMode(RecState.Starting, false);
    }

    private IntPtr Hwnd => new WindowInteropHelper(this).Handle;

    private void Setup()
    {
        var h = Hwnd;
        int ex = NativeMethods.GetWindowLong(h, -20);                          // GWL_EXSTYLE
        NativeMethods.SetWindowLong(h, -20, ex | 0x80 | 0x08000000);           // TOOLWINDOW | NOACTIVATE: sem Alt+Tab, sem roubar foco
        NativeMethods.SetWindowDisplayAffinity(h, 0x11);                       // WDA_EXCLUDEFROMCAPTURE: fora do vídeo
        int round = 2;                                                         // DWMWCP_ROUND (cantos arredondados no Windows 11)
        NativeMethods.DwmSetWindowAttribute(h, 33, ref round, sizeof(int));
        Place();
    }

    /// <summary>Centraliza no topo do monitor gravado, em pixels físicos (vale para qualquer escala de DPI).</summary>
    private void Place()
    {
        var h = Hwnd;
        if (h == IntPtr.Zero) return;
        double sx = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (sx <= 0) sx = 1;
        int w = (int)Math.Round(ActualWidth * sx), ht = (int)Math.Round(ActualHeight * sx);
        if (w <= 0 || ht <= 0) return;
        int x = _mon.X + (_mon.Width - w) / 2;
        int y = _mon.Y + (int)Math.Round(12 * sx);
        NativeMethods.SetWindowPos(h, new IntPtr(-1), x, y, w, ht, 0x0010 | 0x0040);   // NOACTIVATE | SHOWWINDOW
        PhysicalBounds = new Rect(x, y, w, ht);
    }

    public void SetTime(string text) => _time.Text = text;

    public void SetMode(RecState s, bool resuming)
    {
        _dot.BeginAnimation(OpacityProperty, null);
        _dot.Opacity = 1;
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
            default: // Starting
                _status.Text = resuming ? "RETOMANDO…" : "INICIANDO…"; _status.Foreground = WinUi.Res("Accent");
                _dot.Fill = WinUi.Res("Accent"); Pulse();
                _pause.Content = PauseIcon(); _pause.ToolTip = "Pausar a gravação";
                _pause.IsEnabled = false; _stop.IsEnabled = false;
                break;
        }
    }

    private void Pulse() =>
        _dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.3, TimeSpan.FromMilliseconds(700))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });

    private static Button IconButton(UIElement icon, Brush background, bool bordered) => new()
    {
        Style = WinUi.Sty("IconBtn"), Background = background, Content = icon, Margin = new Thickness(0, 0, 12, 0),
        BorderBrush = WinUi.Res("Border"), BorderThickness = new Thickness(bordered ? 1 : 0),
    };

    private static UIElement PauseIcon()
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new Rectangle { Width = 7, Height = 22, RadiusX = 2, RadiusY = 2, Fill = Brushes.White, Margin = new Thickness(0, 0, 6, 0) });
        sp.Children.Add(new Rectangle { Width = 7, Height = 22, RadiusX = 2, RadiusY = 2, Fill = Brushes.White });
        return sp;
    }

    private static UIElement PlayIcon() => new Polygon
    {
        Points = new PointCollection { new Point(0, 0), new Point(0, 24), new Point(21, 12) },
        Fill = Brushes.White, Margin = new Thickness(5, 0, 0, 0), StrokeLineJoin = PenLineJoin.Round,
    };

    private static UIElement StopIcon() => new Rectangle { Width = 20, Height = 20, RadiusX = 4, RadiusY = 4, Fill = Brushes.White };
}
