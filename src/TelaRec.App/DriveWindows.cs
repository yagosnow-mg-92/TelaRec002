using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TelaRec.Core;
using TelaRec.Services;

namespace TelaRec;

internal static class WinUi
{
    public static Brush Res(string key) => (Brush)Application.Current.Resources[key];
    public static Style Sty(string key) => (Style)Application.Current.Resources[key];

    public static Button MakeButton(string text, Brush background, double minWidth = 130)
    {
        var b = new Button { Content = text, Style = Sty("Btn"), Background = background, Height = 40, MinWidth = minWidth, FontSize = 13, Margin = new Thickness(0, 0, 10, 0) };
        return b;
    }
}

/// <summary>Janela de progresso do envio ao Google Drive.</summary>
internal sealed class UploadProgressWindow : Window
{
    private readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 100, Height = 14, Margin = new Thickness(0, 14, 0, 8), Foreground = WinUi.Res("Accent"), Background = WinUi.Res("Surface2"), BorderBrush = WinUi.Res("Border") };
    private readonly TextBlock _stats = new() { Foreground = WinUi.Res("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly CancellationTokenSource _cts = new();
    private bool _done;

    public CancellationToken Token => _cts.Token;
    public IProgress<UploadProgress> Progress { get; }

    public UploadProgressWindow(string fileName, long size)
    {
        Title = "Enviando para o Google Drive";
        Width = 540; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = WinUi.Res("Bg"); Foreground = WinUi.Res("Text"); FontFamily = new FontFamily("Segoe UI");
        Icon = null;

        var cancel = WinUi.MakeButton("Cancelar envio", WinUi.Res("Surface2"), 150);
        cancel.BorderBrush = WinUi.Res("Border"); cancel.BorderThickness = new Thickness(1);
        cancel.Margin = new Thickness(0, 16, 0, 0); cancel.HorizontalAlignment = HorizontalAlignment.Right;
        cancel.Click += (_, _) => RequestCancel();

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Enviando o vídeo…", FontSize = 18, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = $"{fileName}  ({TimeFormat.Bytes(size)})", Foreground = WinUi.Res("Muted"), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_bar);
        panel.Children.Add(_stats);
        panel.Children.Add(cancel);
        Content = panel;

        _stats.Text = "Iniciando…";
        Progress = new Progress<UploadProgress>(Update);
        Closing += (_, e) => { if (!_done) { e.Cancel = true; RequestCancel(); } };
    }

    private void RequestCancel()
    {
        _stats.Text = "Cancelando…";
        _cts.Cancel();
    }

    private void Update(UploadProgress p)
    {
        double pct = p.Total > 0 ? p.Sent * 100.0 / p.Total : 0;
        _bar.Value = pct;
        string eta = "";
        if (p.BytesPerSec > 1)
            eta = "  ·  faltam " + TimeFormat.Format(TimeSpan.FromSeconds((p.Total - p.Sent) / p.BytesPerSec));
        string speed = p.BytesPerSec > 1 ? $"  ·  {TimeFormat.Bytes((long)p.BytesPerSec)}/s" : "";
        _stats.Text = $"{pct:0}%  ·  {TimeFormat.Bytes(p.Sent)} de {TimeFormat.Bytes(p.Total)}{speed}{eta}";
    }

    public void Finish() { _done = true; Close(); }
}

/// <summary>Janela final com o link do vídeo e o botão de copiar.</summary>
internal sealed class DriveResultWindow : Window
{
    public DriveResultWindow(string link, string fileName)
    {
        Title = "Vídeo enviado!";
        Width = 600; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = WinUi.Res("Bg"); Foreground = WinUi.Res("Text"); FontFamily = new FontFamily("Segoe UI");

        var box = new TextBox
        {
            Text = link, IsReadOnly = true, Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(10, 9, 10, 9),
            Background = WinUi.Res("Surface2"), Foreground = WinUi.Res("Text"), BorderBrush = WinUi.Res("Border"),
            CaretBrush = Brushes.White, FontSize = 13,
        };
        box.GotKeyboardFocus += (_, _) => box.SelectAll();

        var copy = WinUi.MakeButton("Copiar link", WinUi.Res("Accent"), 140);
        copy.Click += async (_, _) =>
        {
            try { Clipboard.SetText(link); }
            catch { try { Clipboard.SetDataObject(link, true); } catch { box.SelectAll(); box.Focus(); return; } }
            copy.Content = "Copiado ✓";
            await Task.Delay(2000);
            copy.Content = "Copiar link";
        };
        var open = WinUi.MakeButton("Abrir no navegador", WinUi.Res("Surface2"), 170);
        open.BorderBrush = WinUi.Res("Border"); open.BorderThickness = new Thickness(1);
        open.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(link) { UseShellExecute = true }); } catch { }
        };
        var close = WinUi.MakeButton("Fechar", WinUi.Res("Surface2"), 100);
        close.BorderBrush = WinUi.Res("Border"); close.BorderThickness = new Thickness(1);
        close.Margin = new Thickness(0);
        close.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(copy); buttons.Children.Add(open); buttons.Children.Add(close);

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "✓  Vídeo enviado para o Google Drive", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = WinUi.Res("Green") });
        panel.Children.Add(new TextBlock { Text = fileName, Foreground = WinUi.Res("Muted"), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Link do vídeo (quem tem acesso à pasta consegue abrir):", Margin = new Thickness(0, 16, 0, 0) });
        panel.Children.Add(box);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => { copy.Focus(); };
    }
}
