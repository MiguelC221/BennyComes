namespace BenyComes.Models;

public enum PlaybackState
{
    Stopped,
    Playing,
    Paused
}

public enum RepeatMode
{
    Off,
    All,
    One
}

public enum AudioOutputMode
{
    WasapiShared,
    WasapiExclusive
}
