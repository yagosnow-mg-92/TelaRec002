using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using TelaRec.Services;

namespace TelaRec;

/// <summary>
/// Navegador de pastas do Drive, limitado à pasta configurada (raiz) e às suas subpastas.
/// Abrir subpasta (duplo clique), voltar (nunca acima da raiz), criar pasta, ordenar (nome / mais recentes /
/// mais antigas) e pesquisar (destaca em cor o trecho encontrado, sem filtrar nem abrir a pasta).
/// </summary>
internal sealed class DriveFolderWindow : Window
{
    public const int SortName = 0, SortNewest = 1, SortOldest = 2;

    private readonly string _rootId;
    private readonly Func<string, Task<DriveListing>> _list;
    private readonly Func<string, string, Task<DriveCreateResult>> _create;
    private readonly List<DriveFolder> _stack = new();     // [0] = raiz, último = pasta atual
    private List<DriveFolder> _folders = new();

    private readonly ListBox _lb = new();
    private readonly TextBlock _path = new();
    private readonly TextBlock _status = new();
    private readonly TextBox _search = new();
    private readonly Button _back, _newBtn, _ok, _cancel, _create2, _cancelNew;
    private readonly RadioButton _rbName, _rbNew, _rbOld;
    private readonly Grid _newPanel = new();
    private readonly TextBox _newName = new();
    private readonly List<ListBoxItem> _matches = new();
    private int _matchPos = -1;
    private string _baseStatus = "";
    private bool _busy;

    public bool Confirmed { get; private set; }
    public int SortMode { get; private set; }
    public double SavedWidth { get; private set; }
    public double SavedHeight { get; private set; }
    public string? SelectedFolderId => _stack.Count > 0 ? _stack[^1].Id : null;
    public string SelectedPath => string.Join(" / ", _stack.Select(f => f.Name));

    public DriveFolderWindow(string rootId, Func<string, Task<DriveListing>> list,
        Func<string, string, Task<DriveCreateResult>> create, int sortMode, double width, double height)
    {
        _rootId = rootId; _list = list; _create = create;
        SortMode = sortMode is >= 0 and <= 2 ? sortMode : SortNewest;
        _stack.Add(new DriveFolder(rootId, "Pasta da empresa"));

        Title = "Escolher a pasta no Google Drive";
        MinWidth = 620; MinHeight = 560;
        var wa = SystemParameters.WorkArea;
        Width = width > 0 ? Math.Clamp(width, MinWidth, wa.Width) : 800;      // largura padrão já comporta todos os botões
        Height = height > 0 ? Math.Clamp(height, MinHeight, wa.Height) : 760;
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
        _lb.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _lb.MouseDoubleClick += async (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(_lb, d) is ListBoxItem) await OpenSelectedAsync();
        };
        _lb.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await OpenSelectedAsync(); };

        _status.Foreground = WinUi.Res("Muted"); _status.FontSize = 12; _status.TextWrapping = TextWrapping.Wrap; _status.Margin = new Thickness(0, 8, 0, 0);

        // ---- barra: voltar / nova pasta / pesquisar
        _back = Btn("← Voltar", false); _back.Click += async (_, _) => await GoBackAsync();
        _newBtn = Btn("＋ Nova pasta", false); _newBtn.Click += (_, _) => ShowNewPanel(true);
        _search.Background = WinUi.Res("Surface2"); _search.Foreground = WinUi.Res("Text"); _search.BorderBrush = WinUi.Res("Border");
        _search.CaretBrush = Brushes.White; _search.Padding = new Thickness(8, 7, 8, 7); _search.Width = 240;
        _search.ToolTip = "Digite parte do nome: as pastas encontradas ficam destacadas. Enter vai para a próxima; Esc limpa.";
        _search.TextChanged += (_, _) => Render();
        _search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { NextMatch(); e.Handled = true; }
            else if (e.Key == Key.Escape) { _search.Text = ""; e.Handled = true; }
        };
        var searchPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        searchPanel.Children.Add(new TextBlock { Text = "🔍  Pesquisar:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        searchPanel.Children.Add(_search);

        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_newBtn, 1); Grid.SetColumn(searchPanel, 3);
        toolbar.Children.Add(_back); toolbar.Children.Add(_newBtn); toolbar.Children.Add(searchPanel);

        // ---- nova pasta (linha que se ajusta à largura: nunca corta o botão Cancelar)
        _newName.Background = WinUi.Res("Surface2"); _newName.Foreground = WinUi.Res("Text"); _newName.BorderBrush = WinUi.Res("Border");
        _newName.CaretBrush = Brushes.White; _newName.Padding = new Thickness(8, 7, 8, 7); _newName.MaxLength = 100;
        _newName.PreviewTextInput += (_, e) => { if (e.Text.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0) e.Handled = true; };
        _newName.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await CreateAsync(); if (e.Key == Key.Escape) ShowNewPanel(false); };
        _create2 = Btn("Criar", true); _create2.Margin = new Thickness(10, 0, 8, 0);
        _cancelNew = Btn("Cancelar", false); _cancelNew.Margin = new Thickness(0);
        _cancelNew.Click += (_, _) => ShowNewPanel(false);
        _create2.Click += async (_, _) => await CreateAsync();
        _newPanel.Margin = new Thickness(0, 0, 0, 10); _newPanel.Visibility = Visibility.Collapsed;
        _newPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _newPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _newPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _newPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lbl = new TextBlock { Text = "Nome da pasta:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        Grid.SetColumn(_newName, 1); Grid.SetColumn(_create2, 2); Grid.SetColumn(_cancelNew, 3);
        _newPanel.Children.Add(lbl); _newPanel.Children.Add(_newName); _newPanel.Children.Add(_create2); _newPanel.Children.Add(_cancelNew);

        // ---- ordenação
        _rbName = Radio("Nome (A–Z)", SortName); _rbNew = Radio("Mais recentes", SortNewest); _rbOld = Radio("Mais antigas", SortOldest);
        var sortRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        sortRow.Children.Add(new TextBlock { Text = "Ordenar por:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        sortRow.Children.Add(_rbName); sortRow.Children.Add(_rbNew); sortRow.Children.Add(_rbOld);

        // ---- botões finais
        _ok = Btn("Salvar aqui", true); _ok.MinWidth = 150; _ok.Margin = new Thickness(0);
        _ok.Click += (_, _) => { Confirmed = true; DialogResult = true; };
        _cancel = Btn("Cancelar envio", false); _cancel.MinWidth = 150;
        _cancel.Click += (_, _) => { Confirmed = false; DialogResult = false; };
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        bottom.Children.Add(_cancel); bottom.Children.Add(_ok);

        var grid = new Grid { Margin = new Thickness(24) };
        for (int i = 0; i < 9; i++) grid.RowDefinitions.Add(new RowDefinition { Height = i == 6 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        void Put(UIElement e, int row) { Grid.SetRow(e, row); grid.Children.Add(e); }
        Put(title, 0); Put(hint, 1); Put(_path, 2); Put(toolbar, 3); Put(_newPanel, 4); Put(sortRow, 5); Put(_lb, 6); Put(_status, 7); Put(bottom, 8);
        Content = grid;

        Loaded += async (_, _) => await LoadAsync();
        // Lembra o tamanho escolhido pelo usuário para a próxima vez.
        Closing += (_, _) =>
        {
            var rb = RestoreBounds;
            if (!rb.IsEmpty) { SavedWidth = rb.Width; SavedHeight = rb.Height; }
        };
    }

    private RadioButton Radio(string text, int mode)
    {
        var r = new RadioButton { Content = text, Style = WinUi.Sty("PillRadio"), GroupName = "ordem", IsChecked = SortMode == mode };
        r.Click += (_, _) => { SortMode = mode; Render(); };
        return r;
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
        _lb.IsEnabled = !busy; _newBtn.IsEnabled = !busy; _ok.IsEnabled = !busy; _search.IsEnabled = !busy;
        _back.IsEnabled = !busy && _stack.Count > 1;       // na raiz o Voltar fica desativado
        _create2.IsEnabled = !busy;
        if (status != null) { _baseStatus = status; _status.Text = status; _status.Foreground = WinUi.Res("Muted"); }
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

        _search.Text = "";
        if (!r.Ok)
        {
            _folders = new(); Render();
            _status.Text = r.Error ?? "Não foi possível listar as pastas."; _status.Foreground = WinUi.Res("Red");
            UpdatePath(); SetBusy(false);
            _ok.IsEnabled = false;                        // não salva em uma pasta que não carregou
            return;
        }
        if (_stack.Count == 1 && !string.IsNullOrWhiteSpace(r.Name)) _stack[0] = new DriveFolder(_rootId, r.Name);
        _folders = r.Folders;
        UpdatePath(); SetBusy(false);
        _baseStatus = r.Folders.Count == 0
            ? "Esta pasta ainda não tem subpastas. Você pode criar uma ou salvar aqui."
            : $"{r.Folders.Count} subpasta(s). Clique duas vezes para abrir.";
        Render();
        _status.Foreground = WinUi.Res("Muted");
    }

    private IEnumerable<DriveFolder> Sorted() => SortMode switch
    {
        SortNewest => _folders.OrderByDescending(f => f.Created).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase),
        SortOldest => _folders.OrderBy(f => f.Created).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase),
        _ => _folders.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase),
    };

    private static int IndexOfIgnoringCase(string text, string query) =>
        CultureInfo.CurrentCulture.CompareInfo.IndexOf(text, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);

    private UIElement BuildRow(DriveFolder f, string query)
    {
        var tb = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        tb.Inlines.Add(new Run("📁  "));
        int idx = query.Length > 0 ? IndexOfIgnoringCase(f.Name, query) : -1;
        if (idx >= 0)
        {
            int len = Math.Min(query.Length, f.Name.Length - idx);
            tb.Inlines.Add(new Run(f.Name[..idx]));
            tb.Inlines.Add(new Run(f.Name.Substring(idx, len))
            { Background = WinUi.Res("Amber"), Foreground = Brushes.Black, FontWeight = FontWeights.Bold });
            tb.Inlines.Add(new Run(f.Name[(idx + len)..]));
        }
        else tb.Inlines.Add(new Run(f.Name));

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(tb);
        if (f.Created > 0)
        {
            var date = new TextBlock
            {
                Text = DateTimeOffset.FromUnixTimeMilliseconds(f.Created).LocalDateTime.ToString("dd/MM/yyyy HH:mm"),
                Foreground = WinUi.Res("Muted"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
            };
            Grid.SetColumn(date, 1);
            row.Children.Add(date);
        }
        return row;
    }

    /// <summary>Redesenha a lista conforme a ordem e a pesquisa (a pesquisa só destaca: não filtra nem abre nada).</summary>
    private void Render()
    {
        var q = _search.Text.Trim();
        _lb.Items.Clear();
        _matches.Clear(); _matchPos = -1;
        foreach (var f in Sorted())
        {
            var item = new ListBoxItem { Content = BuildRow(f, q), Tag = f, Style = WinUi.Sty("MicItem") };
            _lb.Items.Add(item);
            if (q.Length > 0 && IndexOfIgnoringCase(f.Name, q) >= 0) _matches.Add(item);
        }

        if (q.Length == 0) { _status.Text = _baseStatus; _status.Foreground = WinUi.Res("Muted"); return; }
        if (_matches.Count == 0)
        {
            _status.Text = $"Nenhuma pasta encontrada com \"{q}\" nesta pasta.";
            _status.Foreground = WinUi.Res("Amber");
            return;
        }
        SelectMatch(0);
    }

    private void SelectMatch(int pos)
    {
        _matchPos = pos;
        var item = _matches[pos];
        _lb.SelectedItem = item;
        _lb.ScrollIntoView(item);
        _status.Text = $"{_matches.Count} pasta(s) encontrada(s) — mostrando a {pos + 1}ª. Enter vai para a próxima; dê dois cliques para abrir.";
        _status.Foreground = WinUi.Res("Green");
    }

    private void NextMatch()
    {
        if (_matches.Count == 0) return;
        SelectMatch((_matchPos + 1) % _matches.Count);
    }

    private void UpdatePath() => _path.Text = "Você está em:  " + SelectedPath;

    private async Task OpenSelectedAsync()
    {
        if (_busy || _lb.SelectedItem is not ListBoxItem { Tag: DriveFolder f }) return;
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
        _baseStatus = r.Existed ? $"A pasta \"{r.Folder.Name}\" já existia; você entrou nela."
                                : $"Pasta \"{r.Folder.Name}\" criada. Clique em \"Salvar aqui\" para salvar nela.";
        _status.Text = _baseStatus; _status.Foreground = WinUi.Res("Green");
    }
}
