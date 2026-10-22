using System.Windows;
using System.Windows.Threading;

namespace TelaRec;

public partial class App : Application
{
    public App()
    {
        // Erros inesperados nunca encerram o programa sem uma mensagem compreensível.
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            MessageBox.Show("Ocorreu um erro inesperado: " + (e.ExceptionObject as Exception)?.Message,
                "TelaRec", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show("Ocorreu um erro inesperado, mas o programa continuará aberto.\n\n" + e.Exception.Message,
            "TelaRec", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
