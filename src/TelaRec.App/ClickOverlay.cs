using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using TelaRec.Core;
using TelaRec.Native;

namespace TelaRec;

/// <summary>
/// Janela transparente, sem foco e "atravessável" pelo mouse, posicionada exatamente sobre o monitor gravado.
/// Os círculos desenhados aqui aparecem no vídeo porque fazem parte da imagem final da tela.
/// </summary>
internal sealed class ClickOverlay : Window
{
    public static readonly Color LeftColor = Color.FromRgb(0x2F, 0x9B, 0xFF);  // azul
    public static readonly Color RightColor = Color.FromRgb(0xFF, 0x8A, 0x1F); // laranja

    private readonly Canvas _canvas = new();
    private readonly MonitorInfo _mon;

    public ClickOverlay(MonitorInfo mon)
    {
        _mon = mon;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        Title = "TelaRec-overlay";
        Left = mon.X; Top = mon.Y; Width = 200; Height = 200;
        Content = _canvas;

        SourceInitialized += (_, _) => { MakeClickThrough(); Place(); };
        ContentRendered += (_, _) => Place();
        DpiChanged += (_, _) => Place();
    }

    private IntPtr Hwnd => new WindowInteropHelper(this).Handle;

    private void MakeClickThrough()
    {
        var h = Hwnd;
        int ex = NativeMethods.GetWindowLong(h, -20); // GWL_EXSTYLE
        // TRANSPARENT (cliques passam) | TOOLWINDOW (fora do Alt+Tab) | NOACTIVATE (nunca rouba o foco)
        NativeMethods.SetWindowLong(h, -20, ex | 0x20 | 0x80 | 0x08000000);
    }

    /// <summary>Cobre exatamente o monitor, em pixels físicos, independente da escala de DPI.</summary>
    private void Place() =>
        NativeMethods.SetWindowPos(Hwnd, new IntPtr(-1), _mon.X, _mon.Y, _mon.Width, _mon.Height, 0x0010 | 0x0040);

    /// <param name="px">X físico no desktop virtual</param><param name="py">Y físico</param>
    public void Ripple(int px, int py, bool left)
    {
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (scale <= 0) scale = 1;
        double cx = (px - _mon.X) / scale, cy = (py - _mon.Y) / scale;
        var c = left ? LeftColor : RightColor;

        // Anel que cresce e some
        double d = 90 / scale;
        var ring = new Ellipse
        {
            Width = d, Height = d,
            Stroke = new SolidColorBrush(c), StrokeThickness = Math.Max(2.5, 5 / scale),
            Fill = new SolidColorBrush(Color.FromArgb(70, c.R, c.G, c.B)),
            IsHitTestVisible = false, RenderTransformOrigin = new Point(0.5, 0.5),
        };
        var st = new ScaleTransform(0.2, 0.2);
        ring.RenderTransform = st;
        Canvas.SetLeft(ring, cx - d / 2); Canvas.SetTop(ring, cy - d / 2);
        _canvas.Children.Add(ring);

        var dur = TimeSpan.FromMilliseconds(520);
        var grow = new DoubleAnimation(0.2, 1.0, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        var fade = new DoubleAnimation(1, 0, dur);
        fade.Completed += (_, _) => _canvas.Children.Remove(ring);
        ring.BeginAnimation(OpacityProperty, fade);

        // Ponto central sólido no local exato do clique
        double dd = 16 / scale;
        var dot = new Ellipse
        {
            Width = dd, Height = dd, Fill = new SolidColorBrush(c),
            Stroke = Brushes.White, StrokeThickness = Math.Max(1.5, 2 / scale), IsHitTestVisible = false,
        };
        Canvas.SetLeft(dot, cx - dd / 2); Canvas.SetTop(dot, cy - dd / 2);
        _canvas.Children.Add(dot);
        var dotFade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(380));
        dotFade.Completed += (_, _) => _canvas.Children.Remove(dot);
        dot.BeginAnimation(OpacityProperty, dotFade);
    }
}
