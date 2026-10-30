using System.IO;
using System.Windows;
using TelaRec.Core;

namespace TelaRec;

// Fluxo ao terminar a gravação: 1) janela para confirmar/alterar o nome; 2) pergunta sobre o Google Drive.
public partial class MainWindow
{
    private async Task AfterRecordingAsync(string path)
    {
        var finalPath = AskFileName(path);
        await OfferDriveAsync(finalPath);
    }

    /// <summary>Mostra a janela de nome do arquivo e renomeia o vídeo. Devolve o caminho final.</summary>
    private string AskFileName(string path)
    {
        try
        {
            if (!File.Exists(path)) return path;
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();

            var folder = System.IO.Path.GetDirectoryName(path)!;
            var current = System.IO.Path.GetFileNameWithoutExtension(path);

            var win = new RenameWindow(current, folder) { Owner = this };
            win.ShowDialog();

            var wanted = FileNamer.SafeName(win.ChosenName);
            if (wanted.Length == 0 || string.Equals(wanted, current, StringComparison.Ordinal)) return path;

            var target = System.IO.Path.Combine(folder, wanted + ".mp4");
            bool collided = false;
            bool sameFileOtherCase = string.Equals(target, path, StringComparison.OrdinalIgnoreCase);

            if (!sameFileOtherCase && File.Exists(target))
            {
                target = FileNamer.UniquePath(folder, wanted);   // nunca sobrescreve outro arquivo
                collided = true;
            }

            if (sameFileOtherCase)
            {
                // Só mudou maiúsculas/minúsculas: o Windows precisa de um nome intermediário.
                var tmp = System.IO.Path.Combine(folder, "." + Guid.NewGuid().ToString("N")[..8] + ".tmp.mp4");
                File.Move(path, tmp);
                File.Move(tmp, target);
            }
            else File.Move(path, target);

            _lastFile = target;
            _folderToOpen = folder;
            SetMessage("Vídeo salvo em: " + target +
                       (collided ? "\n(Já existia um arquivo com esse nome, então foi acrescentado um número.)" : ""),
                       (System.Windows.Media.Brush)FindResource("Green"), true);
            return target;
        }
        catch (Exception ex)
        {
            ShowError("Não foi possível renomear o arquivo: " + ex.Message + "\nO vídeo continua salvo como: " + path);
            return path;
        }
    }
}
