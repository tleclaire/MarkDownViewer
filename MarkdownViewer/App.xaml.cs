using MdViewer.Shared;
using System.Windows;

namespace MarkdownViewer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Globale Fehlerbehandlung
        DispatcherUnhandledException += (_, args) =>
        {
            var ex = args.Exception;

            FileLogger.Error("Unbehandelte Ausnahme auf dem Dispatcher-Thread", ex);

            // Kritische Ausnahmen nie schlucken — App soll crashen
            if (ex is OutOfMemoryException or StackOverflowException or AccessViolationException)
                return;

            MessageBox.Show(
                $"Ein unerwarteter Fehler ist aufgetreten:\n\n{ex.Message}\n\nDie Anwendung wird beendet.\n\nDetails: Log\\log-{DateTime.Now:yyyy-MM-dd}.log",
                "MdViewer — Fehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            args.Handled = true;
            Current.Shutdown();
        };
    }
}
