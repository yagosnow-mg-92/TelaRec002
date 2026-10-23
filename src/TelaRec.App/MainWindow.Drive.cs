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

    private void DriveHelpButton_Click(object sender, RoutedEventArgs e) => DriveHelpPopup.IsOpen = true;

    private void DriveCopyScriptButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(DriveBridgeScript.Code);
            SetDriveStatus("Código copiado! Abra script.new, apague o conteúdo, cole e siga o passo a passo do botão \"?\".", "Green");
        }
        catch (Exception ex) { SetDriveStatus("Não foi possível copiar: " + ex.Message, "Red"); }
    }

    private async void DriveTestButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateDriveFields(out var folderId, out var url, out var key)) return;
        DriveTestButton.IsEnabled = false;
        SetDriveStatus("Testando a conexão…", "Muted");
        try
        {
            var r = await DriveUploader.CheckAsync(url, key, folderId, CancellationToken.None);
            var warn = url.Contains(key, StringComparison.Ordinal)
                ? "\n\n⚠ Sua chave é igual a um trecho do URL (o ID da implantação). Troque por uma chave diferente: altere CHAVE no script, salve e crie uma Nova versão da implantação."
                : "";
            SetDriveStatus(r.Message + warn, r.Ok ? "Green" : "Red");
        }
        finally { DriveTestButton.IsEnabled = true; }
    }

    // ---------------------------------------------------------------- fluxo após gravar
    private async Task OfferDriveAsync(string path)
    {
        if (!DriveConfigured() || !File.Exists(path)) return;
        if (DriveAutoCheck.IsChecked != true)
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            long size = new FileInfo(path).Length;
            var ans = MessageBox.Show(this,
                $"Gravação concluída.\n\nDeseja enviar o vídeo para o Google Drive da empresa?\n\n{System.IO.Path.GetFileName(path)}  ({TimeFormat.Bytes(size)})",
                "TelaRec — Google Drive", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ans != MessageBoxResult.Yes) return;
        }
        await UploadToDriveAsync(path);
    }

    private async void DriveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastFile != null) await UploadToDriveAsync(_lastFile);
    }

    private async Task UploadToDriveAsync(string path)
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
                try { res = await DriveUploader.UploadAsync(url, key, folderId, path, win.Progress, win.Token); }
                finally { win.Finish(); }

                if (res.Success)
                {
                    new DriveResultWindow(res.Link!, name) { Owner = this }.ShowDialog();
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
