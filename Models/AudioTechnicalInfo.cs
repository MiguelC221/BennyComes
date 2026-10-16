using CommunityToolkit.Mvvm.ComponentModel;

namespace BenyComes.Models;

public partial class AudioTechnicalInfo : ObservableObject
{
    [ObservableProperty]
    private string _format = "FLAC";

    [ObservableProperty]
    private string _sampleRate = "44.1 kHz";

    [ObservableProperty]
    private string _bitDepth = "16-bit";

    [ObservableProperty]
    private string _channels = "Stereo";

    [ObservableProperty]
    private string _bitrate = "Lossless";

    [ObservableProperty]
    private string _outputMode = "WASAPI Shared";

    [ObservableProperty]
    private bool _isBitPerfect;

    [ObservableProperty]
    private string _deviceName = "Default Output";

    public void UpdateFromTrack(Track? track, bool isExclusive, string deviceName)
    {
        if (track == null)
        {
            Format = "IDLE";
            SampleRate = "--";
            BitDepth = "--";
            Channels = "--";
            Bitrate = "--";
            OutputMode = isExclusive ? "WASAPI Exclusive Bit-Perfect" : "WASAPI Shared";
            IsBitPerfect = isExclusive;
            DeviceName = deviceName;
            return;
        }

        Format = string.IsNullOrWhiteSpace(track.FileFormat) ? "AUDIO" : track.FileFormat.ToUpperInvariant();
        SampleRate = track.SampleRateFormatted;
        BitDepth = track.BitDepthFormatted;
        Channels = track.Channels switch
        {
            1 => "Mono",
            2 => "Stereo 2.0",
            6 => "5.1 Surround",
            8 => "7.1 Surround",
            _ => $"{track.Channels} Ch"
        };

        Bitrate = track.Bitrate > 0 ? $"{track.Bitrate} kbps" : "Lossless";
        IsBitPerfect = isExclusive;
        OutputMode = isExclusive ? "WASAPI Exclusive Bit-Perfect" : "WASAPI Shared";
        DeviceName = deviceName;
    }
}
