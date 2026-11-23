using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace FerdiumApp;

public partial class App : Application
{
    public App()
    {
        MigrateLegacyDataFolderIfNeeded(Environment.SpecialFolder.ApplicationData);
        MigrateLegacyDataFolderIfNeeded(Environment.SpecialFolder.LocalApplicationData);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
    }

    private static void MigrateLegacyDataFolderIfNeeded(Environment.SpecialFolder specialFolder)
    {
        try
        {
            var root = Environment.GetFolderPath(specialFolder);
            var legacyDir = Path.Combine(root, "Ferdium.Wpf");
            var newDir = Path.Combine(root, "FerdiumPlus");
            if (Directory.Exists(legacyDir) && !Directory.Exists(newDir))
            {
                Directory.Move(legacyDir, newDir);
            }
        }
        catch
        {
            // Non-fatal: continue with fresh FerdiumPlus folder if legacy folder is locked
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        MessageBox.Show(
            $"Ferdium Plus encountered an unexpected error:\n\n{e.Exception.Message}",
            "Ferdium Plus Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogCrash(ex);
        }
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FerdiumPlus");
            Directory.CreateDirectory(folder);
            File.AppendAllText(
                Path.Combine(folder, "crash.log"),
                $"[{DateTime.Now:O}] {ex}\n\n");
        }
        catch
        {
            // Ignore logging errors
        }
    }
}
