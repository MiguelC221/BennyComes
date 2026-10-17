using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace BenyComes;

public partial class App : Application
{
    public const string AppUserModelId = "BennyComes.MusicPlayer";
    public const string AppDisplayName = "BennyComes";

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            EnsureWindowsAppIdentityRegistered();
            SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        }
        catch { }

        LogMessage("App.OnStartup enter");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _ = Services.Diagnostics.DiagnosticLoggerService.Instance.InitializeAsync();

        base.OnStartup(e);
        LogMessage("App.OnStartup exit");
    }

    public static void EnsureWindowsAppIdentityRegistered()
    {
        try
        {
            string exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
            if (!File.Exists(iconPath))
            {
                iconPath = exePath;
            }

            // 1. Register AUMID in HKCU\Software\Classes\AppUserModelId\BennyComes.MusicPlayer
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppUserModelId}"))
            {
                if (key != null)
                {
                    key.SetValue("DisplayName", AppDisplayName, Microsoft.Win32.RegistryValueKind.String);
                    if (!string.IsNullOrEmpty(iconPath))
                    {
                        key.SetValue("IconUri", iconPath, Microsoft.Win32.RegistryValueKind.String);
                    }
                }
            }

            // 2. Ensure Start Menu shortcut (.lnk) exists with System.AppUserModel.ID so Windows SMTC shows "BennyComes"
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                string startMenuDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                string shortcutPath = Path.Combine(startMenuDir, $"{AppDisplayName}.lnk");
                CreateOrUpdateShortcutWithAumid(shortcutPath, exePath, iconPath, AppUserModelId);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AUMID] Registration warning: {ex.Message}");
        }
    }

    private static void CreateOrUpdateShortcutWithAumid(string shortcutPath, string exePath, string iconPath, string aumid)
    {
        try
        {
            IShellLinkW shellLink = (IShellLinkW)new CShellLink();
            shellLink.SetPath(exePath);
            shellLink.SetWorkingDirectory(Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory);
            shellLink.SetDescription("BennyComes");
            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
            {
                shellLink.SetIconLocation(iconPath, 0);
            }

            if (shellLink is IPropertyStore propertyStore)
            {
                // PKEY_AppUserModel_ID = {9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}, 5
                var appIdKey = new PROPERTYKEY(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
                using (var pv = new PropVariant(aumid))
                {
                    propertyStore.SetValue(ref appIdKey, pv);
                }

                // PKEY_Title = {F29F85E0-4FF9-1068-AB91-08002B27B3D9}, 2
                var titleKey = new PROPERTYKEY(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);
                using (var pvTitle = new PropVariant(AppDisplayName))
                {
                    propertyStore.SetValue(ref titleKey, pvTitle);
                }

                propertyStore.Commit();
            }

            if (shellLink is IPersistFile persistFile)
            {
                persistFile.Save(shortcutPath, true);
            }
        }
        catch { }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046"), ClassInterface(ClassInterfaceType.None)]
    private class CShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        uint GetCount(out uint cProps);
        uint GetAt(uint iProp, out PROPERTYKEY pkey);
        uint GetValue(ref PROPERTYKEY key, [Out] PropVariant pv);
        uint SetValue(ref PROPERTYKEY key, [In] PropVariant pv);
        uint Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
        public PROPERTYKEY(Guid guid, uint id) { fmtid = guid; pid = id; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private sealed class PropVariant : IDisposable
    {
        private ushort vt;
        private ushort wReserved1;
        private ushort wReserved2;
        private ushort wReserved3;
        private IntPtr ptr;
        private int data2;

        public PropVariant(string value)
        {
            vt = 31; // VT_LPWSTR
            ptr = Marshal.StringToCoTaskMemUni(value);
        }

        public void Dispose()
        {
            if (vt == 31 && ptr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(ptr);
                ptr = IntPtr.Zero;
                vt = 0;
            }
        }
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
        Services.Diagnostics.DiagnosticLoggerService.Instance.LogCrash(e.Exception, "DispatcherUnhandledException");
        _exceptionCount++;

        if (_exceptionCount > 3 || e.Exception is System.Windows.Markup.XamlParseException)
        {
            MessageBox.Show($"Ocurrió un error en la interfaz:\n\n{e.Exception.Message}\n\nRevisa el log detallado en %LOCALAPPDATA%\\BenyComes\\Logs\\", "BenyComes Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            Services.Diagnostics.DiagnosticLoggerService.Instance.LogCrash(ex, "AppDomainUnhandledException");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogMessage($"TaskScheduler.UnobservedTaskException: {e.Exception}");
        Services.Diagnostics.DiagnosticLoggerService.Instance.LogCrash(e.Exception, "TaskScheduler.UnobservedTaskException");
        e.SetObserved();
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
