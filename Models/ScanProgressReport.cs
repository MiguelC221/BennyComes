namespace BenyComes.Models;

public class ScanProgressReport
{
    public int Current { get; set; }
    public int Total { get; set; }
    public string CurrentFile { get; set; } = string.Empty;
    public double ProgressPercentage => Total > 0 ? (double)Current / Total * 100.0 : 0.0;
    public string StatusText => Total > 0 ? $"Procesando {Current} de {Total} canciones..." : "Iniciando escaneo...";
}
