using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TelaRec;

/// <summary>
/// Janela exibida ao terminar a gravação: mostra o nome do arquivo (já preenchido com o nome padrão) e permite
/// alterá-lo. A extensão .mp4 é fixa. Só tem o botão OK.
/// </summary>
internal sealed class RenameWindow : Window
{
    private readonly TextBox _box;

    /// <summary>Nome escolhido, sem extensão (pode vir com caracteres inválidos; o chamador sanitiza).</summary>
    public string ChosenName { get; private set; }

    public RenameWindow(string defaultNameWithoutExtension, string folder)
    {
        ChosenName = defaultNameWithoutExtension;
        Title = "Salvar gravação";
        Width = 600; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = WinUi.Res("Bg"); Foreground = WinUi.Res("Text"); FontFamily = new FontFamily("Segoe UI"); FontSize = 14;

        _box = new TextBox
        {
            Text = defaultNameWithoutExtension, MaxLength = 150,
            Background = WinUi.Res("Surface2"), Foreground = WinUi.Res("Text"), BorderBrush = WinUi.Res("Border"),
            CaretBrush = Brushes.White, Padding = new Thickness(10, 9, 10, 9), FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        // Impede a digitação de caracteres que o Windows não aceita em nomes de arquivo.
        _box.PreviewTextInput += (_, e) =>
        {
            if (e.Text.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0) e.Handled = true;
        };

        var ext = new TextBlock
        {
            Text = ".mp4", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = WinUi.Res("Muted"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "A extensão não pode ser alterada",
        };
        var row = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(_box);
        Grid.SetColumn(ext, 1);
        row.Children.Add(ext);

        var ok = WinUi.MakeButton("OK", WinUi.Res("Accent"), 120);
        ok.IsDefault = true;                       // Enter também confirma
        ok.Margin = new Thickness(0);
        ok.HorizontalAlignment = HorizontalAlignment.Right;
        ok.Click += (_, _) => { ChosenName = _box.Text; DialogResult = true; };

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Gravação concluída", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = WinUi.Res("Green") });
        panel.Children.Add(new TextBlock
        {
            Text = "Confirme o nome do arquivo ou altere-o. A extensão .mp4 é fixa.",
            Foreground = WinUi.Res("Muted"), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(row);
        panel.Children.Add(new TextBlock
        {
            Text = "Pasta: " + folder, Foreground = WinUi.Res("Muted"), FontSize = 12,
            Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap,
        });
        var bottom = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        bottom.Children.Add(ok);
        panel.Children.Add(bottom);
        Content = panel;

        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
        // Fechar pelo X equivale a confirmar o que está escrito (nunca perde a gravação nem fica sem nome).
        Closing += (_, _) => { ChosenName = _box.Text; };
    }
}
