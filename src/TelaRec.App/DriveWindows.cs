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
    public DriveResultWindow(string link, string fileName, string? folderPath = null)
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
        if (!string.IsNullOrWhiteSpace(folderPath))
            panel.Children.Add(new TextBlock { Text = "Pasta no Drive: " + folderPath, Foreground = WinUi.Res("Muted"), FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Link do vídeo (quem tem acesso à pasta consegue abrir):", Margin = new Thickness(0, 16, 0, 0) });
        panel.Children.Add(box);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => { copy.Focus(); };
    }
}

internal sealed record DriveHelpActions(
    Func<(bool Ok, string Message)> GenerateKey,
    Func<(bool Ok, string Message)> CopyCode,
    Func<Task<(bool Ok, string Message)>> Test,
    Func<Task<(bool Ok, string Message)>> TestUpload,
    int KeyLine);

/// <summary>Assistente passo a passo para configurar o envio ao Google Drive.</summary>
internal sealed class DriveHelpWindow : Window
{
    private readonly StackPanel _root = new() { Margin = new Thickness(24, 20, 24, 24) };

    public DriveHelpWindow(DriveHelpActions a)
    {
        Title = "Como configurar o envio ao Google Drive";
        Width = 800; Height = 780; MinWidth = 640; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = WinUi.Res("Bg"); Foreground = WinUi.Res("Text"); FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _root };

        Add(T("Configurar o envio ao Google Drive", 20, bold: true));
        Add(T("Você faz isso uma vez só. Siga os passos na ordem; em cada um há uma conferência para saber se deu certo. " +
              "Use o Chrome já logado com a conta Google da empresa que tem acesso à pasta.", muted: true, margin: new Thickness(0, 4, 0, 14)));

        // ---- O que é a chave
        Add(Card(
            T("Antes de tudo: o que é a \"chave\"?", bold: true),
            T("A chave é uma SENHA criada aqui pelo TelaRec. Ela não vem do Google e não é o ID que o Google mostra depois de publicar. " +
              "Ela fica escrita em dois lugares: dentro do script (no Google) e no campo \"Chave secreta\" do TelaRec. " +
              "Se as duas forem iguais, o envio é liberado.", margin: new Thickness(0, 6, 0, 0)),
            T($"Você NÃO precisa editar o código: o botão \"Copiar código do script\" já escreve a chave nele, na linha {a.KeyLine} (a que começa com const CHAVE). " +
              "Essa é a única linha do código que tem a chave; se um dia precisar trocá-la à mão, altere somente o texto entre as aspas dessa linha.",
              muted: true, margin: new Thickness(0, 8, 0, 0))));

        // ---- Passo 1
        var s1 = Status();
        Add(Step("1", "No TelaRec: gerar a chave e copiar o código",
            "Antes, cole o link da pasta do Drive no campo \"Link da pasta\" da tela principal. Depois clique nos dois botões, nesta ordem:",
            Buttons(("1) Gerar nova chave", () => Show(s1, a.GenerateKey())), ("2) Copiar código do script", () => Show(s1, a.CopyCode()))),
            s1));

        // ---- Passo 2
        Add(Step("2", "No Google: colar o código",
            "Clique no botão abaixo. Vai abrir o editor do Google Apps Script (uma página com um código de exemplo \"function myFunction\"). Então:\n" +
            "   • clique em cima do código;\n   • aperte Ctrl+A (seleciona tudo) e Delete (apaga);\n   • aperte Ctrl+V (cola o código copiado) e Ctrl+S (salva).\n\n" +
            $"Conferência: perto do início, na linha {a.KeyLine}, deve aparecer  const CHAVE = 'uma-sequência-de-letras-e-números'.  " +
            "Se aparecer TROQUE-POR-UMA-CHAVE-LONGA, você copiou o código sem gerar a chave: volte ao Passo 1.",
            Buttons(("Abrir script.new", () => Open("https://script.new")))));

        // ---- Passo 3
        Add(Step("3", "No Google: dar permissão ao script",
            "No topo do editor, ao lado do botão \"Depuração\", há um menu com o nome de uma função (por exemplo \"doGet\").\n" +
            "   • clique nesse menu e escolha  autorizar;\n   • clique em Executar;\n" +
            "   • na janela \"Autorização necessária\": Revisar permissões → escolha sua conta → \"O Google não verificou este app\" (é normal, o script é seu) → Avançado → \"Acessar projeto sem título (não seguro)\" → Permitir.\n\n" +
            "Conferência: no \"Registro de execução\" (parte de baixo) aparece \"Execução concluída\".\n\n" +
            "Importante: se você já tinha feito este passo com uma versão anterior do código, FAÇA DE NOVO. Esta versão pede também a permissão de criar arquivos no Drive; " +
            "sem ela o envio falha com \"insufficient authentication scopes\"."));

        // ---- Passo 4
        Add(Step("4", "No Google: publicar o script",
            "   • clique no botão azul  Implantar  (canto superior direito) → Nova implantação;\n" +
            "   • ao lado de \"Selecione o tipo\" clique na engrenagem e escolha  App da Web;\n" +
            "   • Executar como:  Eu (seu e-mail);\n" +
            "   • Quem pode acessar:  Qualquer pessoa  (é a ÚLTIMA opção da lista; não escolha \"Qualquer pessoa em [empresa]\" nem \"com uma Conta do Google\");\n" +
            "   • clique em Implantar e, na tela seguinte, copie o \"URL do app da Web\" (termina em /exec).\n\n" +
            "Conferência (recomendada): abra uma janela anônima do Chrome (Ctrl+Shift+N), cole esse URL e aperte Enter. Deve aparecer um texto com \"ok\":true, \"versao\":3 e \"chaveConfigurada\":true."));

        // ---- Passo 5
        var s5 = Status();
        Add(Step("5", "No TelaRec: colar o URL e testar",
            "Cole o URL do app da Web no campo \"URL do App da Web\" da tela principal (a chave já está preenchida) e clique em Testar conexão (pode clicar aqui também).\n" +
            "Conferência: o resultado fica verde, com o nome da sua pasta, o seu e-mail e \"permissão para criar arquivos: OK\". " +
            "Se avisar que falta permissão, refaça o Passo 3 e crie uma Nova versão (veja abaixo).",
            ButtonsAsync(("Testar conexão", async () => Show(s5, await a.Test()))),
            s5));

        // ---- Passo 6
        var s6 = Status();
        Add(Step("6", "No TelaRec: testar o envio de verdade",
            "Este teste manda um arquivo de texto bem pequeno (\"TelaRec-teste-…txt\") para a sua pasta do Drive e o remove em seguida. " +
            "Ele confirma que a permissão, a pasta e o envio funcionam antes de você gravar um vídeo.",
            ButtonsAsync(("Testar envio de um arquivo", async () => Show(s6, await a.TestUpload()))),
            s6));

        // ---- Se mudar depois
        Add(Card(
            T("Mudou o código ou a chave depois de publicar?", bold: true),
            T("O Google guarda a publicação como uma \"foto\" do código. Para atualizar a foto:\n" +
              "   • Implantar → Gerenciar implantações;\n   • clique no ícone do lápis (Editar);\n   • em \"Versão\" escolha  Nova versão;\n   • clique em Implantar (o URL continua o mesmo).\n" +
              "Sem isso o Google continua usando o código antigo.\n\n" +
              "Trocou a pasta do Drive nas configurações? O código copiado pelo botão já libera a pasta que estava configurada na hora da cópia. " +
              "Se você trocar a pasta, copie o código de novo, cole no script.new e crie uma Nova versão.", margin: new Thickness(0, 6, 0, 0))));

        // ---- Erros
        Add(Card(
            T("Se aparecer uma mensagem de erro", bold: true),
            T("• \"Troque a CHAVE no script…\" → a publicação ainda é antiga. Salve o código (Ctrl+S) e crie uma Nova versão.\n" +
              "• \"Chave inválida\" → a chave do TelaRec é diferente da do script. Gerar nova chave → Copiar código → colar no script → Nova versão.\n" +
              "• \"O script publicado é de uma versão antiga\" → cole o código novo (Passo 2) e crie uma Nova versão.\n" +
              "• \"O Google respondeu com uma página…\" → confira o Passo 4 (\"Qualquer pessoa\") e crie uma Nova versão.\n" +
              "• \"insufficient authentication scopes\" / \"não tem permissão para criar arquivos\" → no editor do script rode a função \"autorizar\" de novo, aceite as permissões e crie uma Nova versão.\n" +
              "• \"não é permitida pelo script\" → a pasta do TelaRec não é a que está liberada no script: copie o código de novo, cole e crie uma Nova versão.\n" +
              "• \"Pasta não encontrada\" → o link da pasta está errado ou a conta que criou o script não tem acesso a ela.\n" +
              "• Qualquer \"Detalhe técnico\" → copie o texto inteiro e envie ao suporte.", muted: true, margin: new Thickness(0, 6, 0, 0))));

        Add(Card(
            T("Segurança", bold: true),
            T("Quem souber o URL e a chave só consegue enviar arquivos para a pasta (não lê nem apaga nada). " +
              "Não compartilhe prints com o URL e a chave. Se vazarem: Gerar nova chave → Copiar código → colar no script → Nova versão.",
              muted: true, margin: new Thickness(0, 6, 0, 0))));
    }

    private void Add(UIElement e) => _root.Children.Add(e);

    private static TextBlock T(string text, double size = 14, bool bold = false, bool muted = false, Thickness? margin = null) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = muted ? WinUi.Res("Muted") : WinUi.Res("Text"),
        Margin = margin ?? new Thickness(0),
    };

    private static TextBlock Status() => new() { TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 10, 0, 0), Foreground = WinUi.Res("Muted") };

    private static void Show(TextBlock tb, (bool Ok, string Message) r)
    {
        tb.Text = r.Message;
        tb.Foreground = WinUi.Res(r.Ok ? "Green" : "Red");
    }

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private static Border Card(params UIElement[] children)
    {
        var sp = new StackPanel();
        foreach (var c in children) sp.Children.Add(c);
        return new Border
        {
            Background = WinUi.Res("Surface"), BorderBrush = WinUi.Res("Border"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12), Child = sp,
        };
    }

    private static StackPanel Buttons(params (string Text, Action Click)[] items)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var (text, click) in items)
        {
            var b = WinUi.MakeButton(text, WinUi.Res("Accent"), 190);
            b.Click += (_, _) => click();
            sp.Children.Add(b);
        }
        return sp;
    }

    private static StackPanel ButtonsAsync(params (string Text, Func<Task> Click)[] items)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var (text, click) in items)
        {
            var b = WinUi.MakeButton(text, WinUi.Res("Accent"), 190);
            b.Click += async (_, _) => { b.IsEnabled = false; try { await click(); } finally { b.IsEnabled = true; } };
            sp.Children.Add(b);
        }
        return sp;
    }

    private static Border Step(string number, string title, string body, StackPanel? buttons = null, TextBlock? status = null)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = WinUi.Res("Accent"), Margin = new Thickness(0, 0, 10, 0),
            Child = new TextBlock { Text = number, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        header.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });

        var items = new List<UIElement> { header, T(body, margin: new Thickness(0, 10, 0, 0)) };
        if (buttons != null) items.Add(buttons);
        if (status != null) items.Add(status);
        return Card(items.ToArray());
    }
}
