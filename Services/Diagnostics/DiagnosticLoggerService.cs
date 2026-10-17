using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using BenyComes.Data;
using BenyComes.Services.Library;

namespace BenyComes.Services.Diagnostics;

public class DiagnosticLoggerService
{
    private static readonly Lazy<DiagnosticLoggerService> _lazy = new(() => new DiagnosticLoggerService());
    public static DiagnosticLoggerService Instance => _lazy.Value;

    public static string LogsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BenyComes",
        "Logs"
    );

    private readonly DispatcherTimer _debugTimer;
    private bool _isDebugModeEnabled;

    public bool IsDebugModeEnabled
    {
        get => _isDebugModeEnabled;
        set
        {
            if (_isDebugModeEnabled != value)
            {
                _isDebugModeEnabled = value;
                if (value)
                {
                    _debugTimer.Start();
                    LogDebugMessage("Modo Debug activado. Monitoreo de memoria cada 30 segundos iniciado.");
                }
                else
                {
                    _debugTimer.Stop();
                    LogDebugMessage("Modo Debug desactivado.");
                }
                _ = LibraryDao.Instance.SetSettingAsync("debug_mode_enabled", value.ToString());
            }
        }
    }

    private DiagnosticLoggerService()
    {
        try
        {
            Directory.CreateDirectory(LogsDirectory);
        }
        catch { }

        _debugTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _debugTimer.Tick += OnDebugTimerTick;
    }

    public async Task InitializeAsync()
    {
        try
        {
            string val = await LibraryDao.Instance.GetSettingAsync("debug_mode_enabled", "false");
            bool enabled = bool.TryParse(val, out var b) && b;
            _isDebugModeEnabled = enabled;
            if (enabled)
            {
                _debugTimer.Start();
                LogDebugMessage("Modo Debug iniciado desde la configuración persistente.");
            }
        }
        catch { }
    }

    private void OnDebugTimerTick(object? sender, EventArgs e)
    {
        if (!IsDebugModeEnabled) return;

        try
        {
            using var proc = Process.GetCurrentProcess();
            double workingSetMb = proc.WorkingSet64 / (1024.0 * 1024.0);
            double gcMemoryMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
            int trackCount = LibraryCacheService.Instance.CachedTracks.Count;

            string artistCacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MusicPlayer",
                "ArtistCache"
            );

            int artistCachedImagesCount = 0;
            if (Directory.Exists(artistCacheDir))
            {
                artistCachedImagesCount = Directory.EnumerateFiles(artistCacheDir, "*.*", SearchOption.AllDirectories)
                    .Count(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase));
            }

            string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [MEM_DIAGNOSTIC] " +
                              $"Working Set: {workingSetMb:F2} MB | " +
                              $"GC Heap: {gcMemoryMb:F2} MB | " +
                              $"Pistas en Memoria: {trackCount} | " +
                              $"Imágenes de Artistas en Disco: {artistCachedImagesCount}";

            LogDebugMessage(logEntry);
        }
        catch { }
    }

    public void LogDebugMessage(string message)
    {
        try
        {
            Directory.CreateDirectory(LogsDirectory);
            string file = Path.Combine(LogsDirectory, "debug_log.txt");
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
            File.AppendAllText(file, line);
        }
        catch { }
    }

    public void LogCrash(Exception ex, string source)
    {
        try
        {
            Directory.CreateDirectory(LogsDirectory);
            string filename = $"crash_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            string filepath = Path.Combine(LogsDirectory, filename);

            using var proc = Process.GetCurrentProcess();
            double workingSetMb = proc.WorkingSet64 / (1024.0 * 1024.0);
            double gcMemoryMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("================================================================================");
            sb.AppendLine("BENYCOMES CRASH REPORT");
            sb.AppendLine($"Fecha y Hora: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"Origen: {source}");
            sb.AppendLine($"Consumo RAM (Working Set): {workingSetMb:F2} MB");
            sb.AppendLine($"Consumo GC Heap: {gcMemoryMb:F2} MB");
            sb.AppendLine($"Hilos Activos: {proc.Threads.Count}");
            sb.AppendLine("================================================================================");
            sb.AppendLine($"Tipo de Excepción: {ex.GetType().FullName}");
            sb.AppendLine($"Mensaje: {ex.Message}");
            sb.AppendLine("Stack Trace:");
            sb.AppendLine(ex.StackTrace ?? "No disponible.");
            sb.AppendLine("================================================================================");
            if (ex.InnerException != null)
            {
                sb.AppendLine("Inner Exception:");
                sb.AppendLine($"{ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
                sb.AppendLine(ex.InnerException.StackTrace);
                sb.AppendLine("================================================================================");
            }

            File.WriteAllText(filepath, sb.ToString());
        }
        catch { }
    }

    public void OpenLogsFolder()
    {
        try
        {
            Directory.CreateDirectory(LogsDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = LogsDirectory,
                UseShellExecute = true
            });
        }
        catch { }
    }
}
