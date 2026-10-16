using System;
using System.Windows.Media.Imaging;
using BenyComes.Models;
using BenyComes.Services;
using BenyComes.Services.Audio;
using BenyComes.Services.Metadata;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BenyComes.ViewModels;

public partial class NowPlayingViewModel : ObservableObject
{
    private readonly AudioPlayerService _audio = AudioPlayerService.Instance;
    public AudioVisualizerService Visualizer => AudioVisualizerService.Instance;

    [ObservableProperty]
    private Track? _currentTrack;

    [ObservableProperty]
    private string _title = "BenyComes Hi-Fi Player";

    [ObservableProperty]
    private string _artist = "Selecciona una canción o añade tu música";

    [ObservableProperty]
    private string _album = "";

    [ObservableProperty]
    private string _artistAndAlbum = "Bienvenido a BenyComes";

    [ObservableProperty]
    private BitmapImage? _albumArt;

    [ObservableProperty]
    private BitmapImage? _backgroundImage;

    [ObservableProperty]
    private string _currentTimeText = "0:00";

    [ObservableProperty]
    private string _totalTimeText = "0:00";

    [ObservableProperty]
    private double _currentPositionSeconds = 0;

    [ObservableProperty]
    private double _totalDurationSeconds = 1;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isShuffle;

    [ObservableProperty]
    private bool _isShuffleEnabled;

    [ObservableProperty]
    private RepeatMode _repeatMode = RepeatMode.Off;

    [ObservableProperty]
    private float _volume = 0.85f;

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private AudioTechnicalInfo _technicalInfo = new();

    public NowPlayingViewModel()
    {
        _audio.TrackChanged += OnTrackChanged;
        _audio.PlaybackStateChanged += OnPlaybackStateChanged;
        _audio.PositionChanged += OnPositionChanged;
        _audio.AudioModeChanged += (s, m) => UpdateTechnicalBadges();
        _audio.OutputDeviceChanged += (s, d) => UpdateTechnicalBadges();

        Volume = _audio.Volume;
        IsMuted = _audio.IsMuted;
        IsShuffle = _audio.IsShuffle;
        IsShuffleEnabled = _audio.IsShuffle;
        RepeatMode = _audio.RepeatMode;

        AlbumArt = MetadataService.Instance.GetFallbackArt();
        BackgroundImage = AlbumArt;
        UpdateTechnicalBadges();
    }

    private void OnTrackChanged(object? sender, Track? track)
    {
        CurrentTrack = track;

        if (track == null)
        {
            Title = "BenyComes Hi-Fi Player";
            Artist = "Reproductor Bit-Perfect";
            Album = "";
            ArtistAndAlbum = "Listo para reproducir";
            AlbumArt = MetadataService.Instance.GetFallbackArt();
            BackgroundImage = AlbumArt;
            CurrentTimeText = "0:00";
            TotalTimeText = "0:00";
            CurrentPositionSeconds = 0;
            TotalDurationSeconds = 1;
            UpdateTechnicalBadges();
            return;
        }

        Title = track.Title;
        Artist = track.Artist;
        Album = track.Album;
        ArtistAndAlbum = track.ArtistAndAlbum;

        var art = MetadataService.Instance.GetHighResAlbumArt(track);
        AlbumArt = art;
        BackgroundImage = MetadataService.Instance.GetBlurredBackgroundArt(track);
        ThemeService.Instance.OnTrackArtChanged(art);

        TotalDurationSeconds = Math.Max(1, track.Duration.TotalSeconds);
        TotalTimeText = track.DurationFormatted;
        CurrentPositionSeconds = 0;
        CurrentTimeText = "0:00";

        UpdateTechnicalBadges();
    }

    private void OnPlaybackStateChanged(object? sender, PlaybackState state)
    {
        IsPlaying = state == PlaybackState.Playing;
    }

    [ObservableProperty]
    private bool _isUserSeeking;

    private void OnPositionChanged(object? sender, TimeSpan pos)
    {
        if (!IsUserSeeking)
        {
            CurrentPositionSeconds = pos.TotalSeconds;
            CurrentTimeText = pos.Hours > 0
                ? pos.ToString(@"h\:mm\:ss")
                : pos.ToString(@"m\:ss");
        }
    }

    private void UpdateTechnicalBadges()
    {
        TechnicalInfo.UpdateFromTrack(CurrentTrack, _audio.IsExclusiveMode, _audio.GetSelectedDeviceName());
    }

    [RelayCommand]
    private void PlayPause()
    {
        _audio.TogglePlayPause();
    }

    [RelayCommand]
    private void Next()
    {
        _audio.Next();
    }

    [RelayCommand]
    private void Previous()
    {
        _audio.Previous();
    }

    [RelayCommand]
    private void ToggleShuffle()
    {
        _audio.IsShuffle = !_audio.IsShuffle;
        IsShuffle = _audio.IsShuffle;
        IsShuffleEnabled = _audio.IsShuffle;
    }

    [RelayCommand]
    private void CycleRepeat()
    {
        RepeatMode = RepeatMode switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            RepeatMode.One => RepeatMode.Off,
            _ => RepeatMode.Off
        };
        _audio.RepeatMode = RepeatMode;
    }

    [RelayCommand]
    private void ToggleMute()
    {
        _audio.IsMuted = !_audio.IsMuted;
        IsMuted = _audio.IsMuted;
    }

    [RelayCommand]
    public void Seek(double targetSeconds)
    {
        _audio.Seek(TimeSpan.FromSeconds(targetSeconds));
    }

    partial void OnVolumeChanged(float value)
    {
        if (value > 0.001f && IsMuted)
        {
            IsMuted = false;
            _audio.IsMuted = false;
        }
        _audio.Volume = value;
    }
}
