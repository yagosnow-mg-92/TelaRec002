using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using TelaRec.Core;
using TelaRec.Services;

namespace TelaRec;

// Envio das gravações ao Google Drive (via ponte Apps Script). Veja README.md.
public partial class MainWindow
{
    private bool _uploading;

    // ---------------------------------------------------------------- chave protegida (DPAPI)
    private static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        try
        {
            var enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(enc);
        }
        catch { return ""; }
    }

    private static string Unprotect(string? b64)
    {
        if (string.IsNullOrEmpty(b64)) return "";
        try
        {
            var dec = ProtectedData.Unprotect(Convert.FromBase64String(b64), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(dec);
        }
        catch { return ""; }
    }

    // ---------------------------------------------------------------- interface de configuração
    private void InitDriveUi()
    {
        DriveCheck.IsChecked = _cfg.DriveEnabled;
        DriveAutoCheck.IsChecked = _cfg.DriveAuto;
        DriveFolderBox.Text = _cfg.DriveFolderLink ?? "";
        DriveUrlBox.Text = _cfg.DriveScriptUrl ?? "";
        DriveKeyBox.Password = Unprotect(_cfg.DriveKeyEnc);
        UpdateDriveUi();
    }

    private void UpdateDriveUi() =>
        DriveOptions.Visibility = DriveCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void DriveOption_Click(object sender, RoutedEventArgs e) => UpdateDriveUi();

    private void ReadDriveSettings(AppSettings c)
    {
        c.DriveEnabled = DriveCheck.IsChecked == true;
        c.DriveAuto = DriveAutoCheck.IsChecked == true;
        c.DriveFolderLink = DriveFolderBox.Text.Trim();
        c.DriveScriptUrl = DriveUrlBox.Text.Trim();
        c.DriveKeyEnc = Protect(DriveKeyBox.Password.Trim());
    }

    private bool DriveConfigured() =>
        DriveCheck.IsChecked == true &&
        DriveLink.ParseFolderId(DriveFolderBox.Text) != null &&
        DriveLink.IsScriptUrl(DriveUrlBox.Text) &&
        !string.IsNullOrWhiteSpace(DriveKeyBox.Password);

    private void SetDriveStatus(string text, string brushKey)
    {
        DriveStatus.Text = text;
        DriveStatus.Foreground = (Brush)FindResource(brushKey);
    }

    private bool ValidateDriveFields(out string folderId, out string url, out string key)
    {
        folderId = DriveLink.ParseFolderId(DriveFolderBox.Text) ?? "";
        url = DriveUrlBox.Text.Trim();
        key = DriveKeyBox.Password.Trim();
        if (folderId.Length == 0) { SetDriveStatus("Cole o link da pasta do Google Drive (ex.: https://drive.google.com/drive/folders/...).", "Red"); return false; }
        if (!DriveLink.IsScriptUrl(url)) { SetDriveStatus("Cole o URL do App da Web do script (começa com https://script.google.com/ e termina com /exec).", "Red"); return false; }
        if (key.Length == 0) { SetDriveStatus("Informe a chave secreta que você definiu no script.", "Red"); return false; }
        return true;
    }

    private (bool Ok, string Message) GenerateKeyAction()
    {
        DriveKeyBox.Password = DriveKey.Generate();
        const string msg = "Chave gerada e colocada no campo \"Chave secreta\" do TelaRec. Agora clique em \"Copiar código do script\".";
        SetDriveStatus(msg, "Green");
        return (true, msg);
    }

    private (bool Ok, string Message) CopyScriptAction()
    {
        try
        {
            // A chave é uma senha inventada (não vem do Google). Se o campo estiver vazio, geramos uma.
            var key = DriveKeyBox.Password.Trim();
            bool generated = false;
            if (key.Length == 0) { key = DriveKey.Generate(); DriveKeyBox.Password = key; generated = true; }
            if (key.Contains('\'') || key.Contains('\\') || key.Contains('"') || key.Contains('\n'))
            {
                const string bad = "A chave não pode ter aspas, barras nem quebras de linha. Clique em \"Gerar nova chave\".";
                SetDriveStatus(bad, "Red");
                return (false, bad);
            }
            // A pasta configurada também vai no código (PASTAS_PERMITIDAS): o Google só aceitará essa pasta e as de dentro dela.
            var rootId = DriveLink.ParseFolderId(DriveFolderBox.Text);
            Clipboard.SetText(DriveBridgeScript.WithKey(key, rootId));
            var msg = (generated ? "Chave gerada. " : "") +
                      $"Código copiado, com a sua chave JÁ DENTRO (linha {DriveBridgeScript.KeyLine}, \"const CHAVE\")" +
                      (DriveLink.ParseFolderId(DriveFolderBox.Text) != null ? " e a sua pasta liberada" : "") + ". Você não precisa alterar nada: " +
                      "agora vá ao script.new, selecione tudo (Ctrl+A), apague (Delete), cole (Ctrl+V) e salve (Ctrl+S).";
            SetDriveStatus(msg, "Green");
            return (true, msg);
        }
        catch (Exception ex)
        {
            var msg = "Não foi possível copiar: " + ex.Message;
            SetDriveStatus(msg, "Red");
            return (false, msg);
        }
    }

    private async Task<(bool Ok, string Message)> RunDriveTestAsync()
    {
        if (!ValidateDriveFields(out var folderId, out var url, out var key)) return (false, DriveStatus.Text);
        SetDriveStatus("Testando a conexão…", "Muted");
        var r = await DriveUploader.CheckAsync(url, key, folderId, CancellationToken.None);
        var warn = url.Contains(key, StringComparison.Ordinal)
            ? "\n\n⚠ Sua chave é igual a um trecho do URL (o ID da implantação). Clique em \"Gerar nova chave\", copie o código de novo e atualize o script (Nova versão)."
            : "";
        SetDriveStatus(r.Message + warn, r.Ok ? "Green" : "Red");
        return (r.Ok, r.Message + warn);
    }

    private async Task<(bool Ok, string Message)> RunDriveUploadTestAsync()
    {
        if (!ValidateDriveFields(out var folderId, out var url, out var key)) return (false, DriveStatus.Text);
        SetDriveStatus("Enviando um arquivo de texto de teste para a pasta…", "Muted");
        var r = await DriveUploader.TestUploadAsync(url, key, folderId, CancellationToken.None);
        SetDriveStatus(r.Message, r.Ok ? "Green" : "Red");
        return (r.Ok, r.Message);
    }

    private async void DriveTestUploadButton_Click(object sender, RoutedEventArgs e)
    {
        DriveTestUploadButton.IsEnabled = false;
        try { await RunDriveUploadTestAsync(); }
        finally { DriveTestUploadButton.IsEnabled = true; }
    }

    private void DriveHelpButton_Click(object sender, RoutedEventArgs e)
    {
        var actions = new DriveHelpActions(GenerateKeyAction, CopyScriptAction, RunDriveTestAsync, RunDriveUploadTestAsync, DriveBridgeScript.KeyLine);
        new DriveHelpWindow(actions) { Owner = this }.Show();
    }

    private void DriveNewKeyButton_Click(object sender, RoutedEventArgs e) => GenerateKeyAction();

    private void DriveCopyScriptButton_Click(object sender, RoutedEventArgs e) => CopyScriptAction();

    private async void DriveTestButton_Click(object sender, RoutedEventArgs e)
    {
        DriveTestButton.IsEnabled = false;
        try { await RunDriveTestAsync(); }
        finally { DriveTestButton.IsEnabled = true; }
    }

    // ---------------------------------------------------------------- fluxo após gravar
    private sealed record DriveTarget(string FolderId, string PathText);

    /// <summary>Pergunta se o usuário quer enviar o vídeo ao Drive.</summary>
    private bool AskSendToDrive(string path)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        long size = File.Exists(path) ? new FileInfo(path).Length : 0;
        var ans = MessageBox.Show(this,
            $"Gravação concluída.\n\nDeseja enviar o vídeo para o Google Drive da empresa?\n\n{System.IO.Path.GetFileName(path)}  ({TimeFormat.Bytes(size)})",
            "TelaRec — Google Drive", MessageBoxButton.YesNo, MessageBoxImage.Question);
        return ans == MessageBoxResult.Yes;
    }

    /// <summary>Abre o navegador de pastas (sempre começando na raiz configurada). Null se o usuário cancelar.</summary>
    private DriveTarget? ChooseDriveFolder()
    {
        if (!ValidateDriveFields(out var rootId, out var url, out var key))
        {
            ShowError("Configure o envio ao Google Drive (seção Google Drive) antes de enviar.");
            return null;
        }
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        var win = new DriveFolderWindow(rootId,
            id => DriveUploader.ListAsync(url, key, rootId, id, CancellationToken.None),
            (parent, name) => DriveUploader.CreateFolderAsync(url, key, rootId, parent, name, CancellationToken.None))
        { Owner = this };
        win.ShowDialog();
        return win.Confirmed && win.SelectedFolderId != null ? new DriveTarget(win.SelectedFolderId, win.SelectedPath) : null;
    }

    private async void DriveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastFile == null) return;
        var target = ChooseDriveFolder();
        if (target != null) await UploadToDriveAsync(_lastFile, target.FolderId, target.PathText);
    }

    private async Task UploadToDriveAsync(string path, string? targetFolderId = null, string? targetPath = null)
    {
        if (_uploading) { MessageBox.Show(this, "Já existe um envio em andamento.", "TelaRec", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (!File.Exists(path)) { ShowError("O arquivo de vídeo não foi encontrado: " + path); return; }
        if (!ValidateDriveFields(out var folderId, out var url, out var key))
        {
            ShowError("Configure o envio ao Google Drive (seção Google Drive) antes de enviar.");
            return;
        }

        _uploading = true;
        try
        {
            ReadSettings().Save(_settingsPath);
            while (true)
            {
                var name = System.IO.Path.GetFileName(path);
                var win = new UploadProgressWindow(name, new FileInfo(path).Length) { Owner = this };
                win.Show();
                UploadResult res;
                try { res = await DriveUploader.UploadAsync(url, key, targetFolderId ?? folderId, path, win.Progress, win.Token, "video/mp4", folderId); }
                finally { win.Finish(); }

                if (res.Success)
                {
                    new DriveResultWindow(res.Link!, name, targetPath) { Owner = this }.ShowDialog();
                    return;
                }
                if (res.Cancelled) { ShowInfo("Envio ao Google Drive cancelado. O vídeo continua salvo no computador."); return; }

                var retry = MessageBox.Show(this,
                    "Não foi possível enviar o vídeo ao Google Drive:\n\n" + res.Error + "\n\nO vídeo continua salvo no computador. Deseja tentar novamente?",
                    "TelaRec — Google Drive", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (retry != MessageBoxResult.Yes) { ShowError("Envio ao Google Drive não concluído: " + res.Error); return; }
            }
        }
        finally { _uploading = false; }
    }
}
