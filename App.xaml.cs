using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace BenyComes;

public partial class App : Application
{
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID("BenyComes.MusicPlayer");
        }
        catch { }

        LogMessage("App.OnStartup enter");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        base.OnStartup(e);
        LogMessage("App.OnStartup exit");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LogMessage($"App.OnExit called with ApplicationCode={e.ApplicationExitCode}");
        base.OnExit(e);
    }

    private int _exceptionCount = 0;

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogMessage($"DispatcherUnhandledException: {e.Exception}");
        _exceptionCount++;

        if (_exceptionCount > 3 || e.Exception is System.Windows.Markup.XamlParseException)
        {
            MessageBox.Show($"Ocurrió un error en la interfaz:\n\n{e.Exception.Message}\n\nRevisa el log en %LOCALAPPDATA%\\BenyComes\\app.log", "BenyComes Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = false;
            return;
        }

        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogMessage($"AppDomainUnhandledException: {ex}");
        }
    }

    public static void LogMessage(string message)
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var logDir = Path.Combine(appData, "BenyComes");
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "app.log");
            File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] {message}\n");
        }
        catch { }
    }
}
