using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using TelaRec.Services;

namespace TelaRec;

/// <summary>
/// Navegador de pastas do Drive, limitado à pasta configurada (raiz) e às suas subpastas.
/// Permite abrir subpastas (duplo clique), voltar (nunca acima da raiz) e criar uma pasta nova.
/// </summary>
internal sealed class DriveFolderWindow : Window
{
    private readonly string _rootId;
    private readonly Func<string, Task<DriveListing>> _list;
    private readonly Func<string, string, Task<DriveCreateResult>> _create;
    private readonly List<DriveFolder> _stack = new();   // [0] = raiz, último = pasta atual

    private readonly ListBox _lb = new();
    private readonly TextBlock _path = new();
    private readonly TextBlock _status = new();
    private readonly Button _back, _newBtn, _ok, _cancel, _create2, _cancelNew;
    private readonly StackPanel _newPanel = new();
    private readonly TextBox _newName = new();
    private bool _busy;

    public bool Confirmed { get; private set; }
    public string? SelectedFolderId => _stack.Count > 0 ? _stack[^1].Id : null;
    public string SelectedPath => string.Join(" / ", _stack.Select(f => f.Name));

    public DriveFolderWindow(string rootId, Func<string, Task<DriveListing>> list, Func<string, string, Task<DriveCreateResult>> create)
    {
        _rootId = rootId; _list = list; _create = create;
        _stack.Add(new DriveFolder(rootId, "Pasta da empresa"));

        Title = "Escolher a pasta no Google Drive";
        Width = 640; Height = 640; MinWidth = 520; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = WinUi.Res("Bg"); Foreground = WinUi.Res("Text"); FontFamily = new FontFamily("Segoe UI"); FontSize = 14;

        var title = new TextBlock { Text = "Onde salvar o vídeo no Drive?", FontSize = 18, FontWeight = FontWeights.SemiBold };
        var hint = new TextBlock
        {
            Text = "Clique duas vezes para abrir uma pasta. Use \"Nova pasta\" para criar uma. Você só pode navegar dentro da pasta da empresa.",
            Foreground = WinUi.Res("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10),
        };
        _path.TextWrapping = TextWrapping.Wrap; _path.FontWeight = FontWeights.SemiBold; _path.Margin = new Thickness(0, 0, 0, 8);

        _lb.Background = WinUi.Res("Surface2"); _lb.BorderBrush = WinUi.Res("Border"); _lb.Foreground = WinUi.Res("Text");
        _lb.ItemContainerStyle = WinUi.Sty("MicItem");
        _lb.ItemTemplate = new DataTemplate
        {
            VisualTree = Factory()
        };
        _lb.MouseDoubleClick += async (_, _) => await OpenSelectedAsync();
        _lb.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await OpenSelectedAsync(); };

        _status.Foreground = WinUi.Res("Muted"); _status.FontSize = 12; _status.TextWrapping = TextWrapping.Wrap; _status.Margin = new Thickness(0, 8, 0, 0);

        _back = Btn("← Voltar", false); _back.Click += async (_, _) => await GoBackAsync();
        _newBtn = Btn("＋ Nova pasta", false); _newBtn.Click += (_, _) => ShowNewPanel(true);
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        toolbar.Children.Add(_back); toolbar.Children.Add(_newBtn);

        // painel "nova pasta" (inline)
        _newName.Background = WinUi.Res("Surface2"); _newName.Foreground = WinUi.Res("Text"); _newName.BorderBrush = WinUi.Res("Border");
        _newName.CaretBrush = Brushes.White; _newName.Padding = new Thickness(8, 7, 8, 7); _newName.MaxLength = 100; _newName.MinWidth = 280;
        _newName.PreviewTextInput += (_, e) => { if (e.Text.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0) e.Handled = true; };
        _newName.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await CreateAsync(); if (e.Key == Key.Escape) ShowNewPanel(false); };
        _create2 = Btn("Criar", true); _create2.Click += async (_, _) => await CreateAsync();
        _cancelNew = Btn("Cancelar", false); _cancelNew.Click += (_, _) => ShowNewPanel(false);
        _newPanel.Orientation = Orientation.Horizontal; _newPanel.Margin = new Thickness(0, 0, 0, 10); _newPanel.Visibility = Visibility.Collapsed;
        _newPanel.Children.Add(new TextBlock { Text = "Nome da pasta:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        _newPanel.Children.Add(_newName);
        _newPanel.Children.Add(new Border { Width = 10 });
        _newPanel.Children.Add(_create2); _newPanel.Children.Add(_cancelNew);

        _ok = Btn("Salvar aqui", true); _ok.MinWidth = 150;
        _ok.Click += (_, _) => { Confirmed = true; DialogResult = true; };
        _cancel = Btn("Cancelar envio", false); _cancel.MinWidth = 150;
        _cancel.Click += (_, _) => { Confirmed = false; DialogResult = false; };
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        bottom.Children.Add(_cancel); bottom.Children.Add(_ok);
        _ok.Margin = new Thickness(0);

        var grid = new Grid { Margin = new Thickness(24) };
        for (int i = 0; i < 8; i++) grid.RowDefinitions.Add(new RowDefinition { Height = i == 5 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        void Put(UIElement e, int row) { Grid.SetRow(e, row); grid.Children.Add(e); }
        Put(title, 0); Put(hint, 1); Put(_path, 2); Put(toolbar, 3); Put(_newPanel, 4); Put(_lb, 5); Put(_status, 6); Put(bottom, 7);
        Content = grid;

        Loaded += async (_, _) => await LoadAsync();
    }

    private static FrameworkElementFactory Factory()
    {
        var f = new FrameworkElementFactory(typeof(TextBlock));
        f.SetBinding(TextBlock.TextProperty, new Binding("Name") { StringFormat = "📁  {0}" });
        return f;
    }

    private static Button Btn(string text, bool primary)
    {
        var b = WinUi.MakeButton(text, primary ? WinUi.Res("Accent") : WinUi.Res("Surface2"), 120);
        b.Height = 36; b.FontSize = 13;
        if (!primary) { b.BorderBrush = WinUi.Res("Border"); b.BorderThickness = new Thickness(1); }
        return b;
    }

    private string CurrentId => _stack[^1].Id;

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        _lb.IsEnabled = !busy; _newBtn.IsEnabled = !busy; _ok.IsEnabled = !busy;
        _back.IsEnabled = !busy && _stack.Count > 1;       // na raiz o Voltar fica desativado
        _create2.IsEnabled = !busy;
        if (status != null) { _status.Text = status; _status.Foreground = WinUi.Res("Muted"); }
    }

    private void ShowNewPanel(bool show)
    {
        _newPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) { _newName.Text = ""; _newName.Focus(); }
    }

    private async Task LoadAsync()
    {
        SetBusy(true, "Carregando…");
        DriveListing r;
        try { r = await _list(CurrentId); }
        catch (Exception ex) { r = new DriveListing(false, ex.Message, "", new()); }

        if (!r.Ok)
        {
            _lb.ItemsSource = null;
            _status.Text = r.Error ?? "Não foi possível listar as pastas."; _status.Foreground = WinUi.Res("Red");
            UpdatePath(); SetBusy(false);
            _ok.IsEnabled = false;                        // não salva em uma pasta que não carregou
            return;
        }
        if (_stack.Count == 1 && !string.IsNullOrWhiteSpace(r.Name)) _stack[0] = new DriveFolder(_rootId, r.Name);
        _lb.ItemsSource = r.Folders;
        _status.Foreground = WinUi.Res("Muted");
        _status.Text = r.Folders.Count == 0
            ? "Esta pasta ainda não tem subpastas. Você pode criar uma ou salvar aqui."
            : $"{r.Folders.Count} subpasta(s). Clique duas vezes para abrir.";
        UpdatePath(); SetBusy(false);
    }

    private void UpdatePath() => _path.Text = "Você está em:  " + SelectedPath;

    private async Task OpenSelectedAsync()
    {
        if (_busy || _lb.SelectedItem is not DriveFolder f) return;
        _stack.Add(f);
        await LoadAsync();
    }

    private async Task GoBackAsync()
    {
        if (_busy || _stack.Count <= 1) return;
        _stack.RemoveAt(_stack.Count - 1);
        await LoadAsync();
    }

    private async Task CreateAsync()
    {
        if (_busy) return;
        var name = new string(_newName.Text.Where(c => Array.IndexOf(System.IO.Path.GetInvalidFileNameChars(), c) < 0).ToArray()).Trim().TrimEnd('.');
        if (name.Length == 0) { _status.Text = "Digite o nome da nova pasta."; _status.Foreground = WinUi.Res("Red"); return; }

        SetBusy(true, "Criando a pasta…");
        DriveCreateResult r;
        try { r = await _create(CurrentId, name); }
        catch (Exception ex) { r = new DriveCreateResult(false, ex.Message, null, false); }

        if (!r.Ok || r.Folder == null)
        {
            SetBusy(false);
            _status.Text = r.Error ?? "Não foi possível criar a pasta."; _status.Foreground = WinUi.Res("Red");
            return;
        }
        ShowNewPanel(false);
        _stack.Add(r.Folder);                              // entra na pasta nova: "Salvar aqui" já salva nela
        await LoadAsync();
        if (r.Existed) { _status.Text = $"A pasta \"{r.Folder.Name}\" já existia; você entrou nela."; }
        else _status.Text = $"Pasta \"{r.Folder.Name}\" criada. Clique em \"Salvar aqui\" para salvar nela.";
        _status.Foreground = WinUi.Res("Green");
    }
}
