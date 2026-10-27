using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using BenyComes.Models;
using BenyComes.Services.Library;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using PlaybackState = BenyComes.Models.PlaybackState;

namespace BenyComes.Services.Audio;

public class AudioPlayerService
{
    private static readonly Lazy<AudioPlayerService> _lazy = new(() => new AudioPlayerService());
    public static AudioPlayerService Instance => _lazy.Value;

    private readonly MMDeviceEnumerator _deviceEnumerator = new();
    private WasapiOut? _wasapiOut;
    private WaveStream? _currentReader;
    private IWaveProvider? _activeWaveProvider;
    private VolumeSampleProvider? _volumeSampleProvider;

    public bool IsAutoplayOnStartup { get; set; } = false;

    private readonly DispatcherTimer _positionTimer;
    private readonly Random _random = new();

    private readonly List<Track> _originalQueue = new();
    private readonly List<Track> _playbackQueue = new();

    private bool _isExclusiveMode = false;
    private string? _selectedDeviceId;
    private float _volume = 0.85f;
    private bool _isMuted = false;
    private PlaybackState _playbackState = PlaybackState.Stopped;
    private int _currentQueueIndex = -1;
    private RepeatMode _repeatMode = RepeatMode.Off;
    private bool _isShuffle = false;
    private bool _isUserSeeking = false;
    private bool _suppressStopEvent = false;

    // Events
    public event EventHandler<Track?>? TrackChanged;
    public event EventHandler<PlaybackState>? PlaybackStateChanged;
    public event Action<Track?, bool>? OnPlaybackStateChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler<string>? OutputDeviceChanged;
    public event EventHandler<bool>? AudioModeChanged;
    public event EventHandler<bool>? ShuffleChanged;
    public event EventHandler<RepeatMode>? RepeatModeChanged;
    public event EventHandler<bool>? MuteChanged;
    public event EventHandler<(string Title, string Message, bool IsWarning)>? NotificationRaised;

    public ObservableCollection<Track> Queue { get; } = new();

    public bool IsPlaying => PlaybackState == PlaybackState.Playing;

    public PlaybackState PlaybackState
    {
        get => _playbackState;
        private set
        {
            if (_playbackState != value)
            {
                _playbackState = value;
                var app = Application.Current;
                if (app?.Dispatcher != null)
                {
                    if (app.Dispatcher.CheckAccess())
                    {
                        PlaybackStateChanged?.Invoke(this, value);
                    }
                    else
                    {
                        app.Dispatcher.InvokeAsync(() => PlaybackStateChanged?.Invoke(this, value));
                    }
                }
                else
                {
                    PlaybackStateChanged?.Invoke(this, value);
                }
            }
        }
    }

    public bool IsExclusiveMode
    {
        get => _isExclusiveMode;
        set
        {
            if (_isExclusiveMode != value)
            {
                _isExclusiveMode = value;
                AudioModeChanged?.Invoke(this, value);
                // Restart current track with new output mode if playing
                if (CurrentTrack != null && (_playbackState == PlaybackState.Playing || _playbackState == PlaybackState.Paused))
                {
                    var currentPos = CurrentPosition;
                    bool wasPlaying = _playbackState == PlaybackState.Playing;
                    ReinitializeAudioOutput();
                    if (_currentReader != null)
                    {
                        _currentReader.CurrentTime = currentPos;
                    }
                    if (wasPlaying)
                    {
                        Play();
                    }
                }
            }
        }
    }

    public string? SelectedDeviceId
    {
        get => _selectedDeviceId;
        set
        {
            if (_selectedDeviceId != value)
            {
                _selectedDeviceId = value;
                OutputDeviceChanged?.Invoke(this, GetSelectedDeviceName());
                if (CurrentTrack != null && (_playbackState == PlaybackState.Playing || _playbackState == PlaybackState.Paused))
                {
                    var currentPos = CurrentPosition;
                    bool wasPlaying = _playbackState == PlaybackState.Playing;
                    ReinitializeAudioOutput();
                    if (_currentReader != null)
                    {
                        _currentReader.CurrentTime = currentPos;
                    }
                    if (wasPlaying)
                    {
                        Play();
                    }
                }
            }
        }
    }

    public float Volume
    {
        get => _isMuted ? 0f : _volume;
        set
        {
            _volume = Math.Clamp(value, 0.0f, 1.0f);
            _isMuted = false;
            ApplyVolume();
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (_isMuted != value)
            {
                _isMuted = value;
                ApplyVolume();
                MuteChanged?.Invoke(this, value);
            }
        }
    }

    public RepeatMode RepeatMode
    {
        get => _repeatMode;
        set
        {
            if (_repeatMode != value)
            {
                _repeatMode = value;
                RepeatModeChanged?.Invoke(this, value);
            }
        }
    }

    public bool IsShuffle
    {
        get => _isShuffle;
        set
        {
            if (_isShuffle != value)
            {
                _isShuffle = value;
                ApplyShuffleOrder();
                ShuffleChanged?.Invoke(this, value);
            }
        }
    }

    private static void FisherYatesShuffle<T>(IList<T> list)
    {
        int n = list.Count;
        for (int i = n - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private void ApplyShuffleOrder()
    {
        var current = CurrentTrack;
        if (_isShuffle)
        {
            _playbackQueue.Clear();
            var pool = new List<Track>(_originalQueue);
            if (current != null)
            {
                pool.Remove(current);
                FisherYatesShuffle(pool);
                _playbackQueue.Add(current);
                _playbackQueue.AddRange(pool);
            }
            else
            {
                FisherYatesShuffle(pool);
                _playbackQueue.AddRange(pool);
            }
        }
        else
        {
            _playbackQueue.Clear();
            _playbackQueue.AddRange(_originalQueue);
        }

        SyncQueueCollection(current);
    }

    private void SyncQueueCollection(Track? activeTrack)
    {
        var syncAction = new Action(() =>
        {
            Queue.Clear();
            foreach (var t in _playbackQueue)
            {
                Queue.Add(t);
            }
            if (activeTrack != null)
            {
                int idx = _playbackQueue.IndexOf(activeTrack);
                _currentQueueIndex = idx >= 0 ? idx : 0;
            }
            else
            {
                _currentQueueIndex = _playbackQueue.Count > 0 ? 0 : -1;
            }
            UpdateQueueItemStatus();
        });

        var app = Application.Current;
        if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(syncAction);
        }
        else
        {
            syncAction();
        }
    }

    public int CurrentQueueIndex
    {
        get => _currentQueueIndex;
        set
        {
            _currentQueueIndex = value;
            UpdateQueueItemStatus();
        }
    }

    public Track? CurrentTrack => _currentQueueIndex >= 0 && _currentQueueIndex < Queue.Count
        ? Queue[_currentQueueIndex]
        : null;

    public TimeSpan CurrentPosition => _currentReader?.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan TotalDuration => _currentReader?.TotalTime ?? CurrentTrack?.Duration ?? TimeSpan.Zero;
    public TimeSpan CurrentTime => CurrentPosition;
    public TimeSpan TotalTime => TotalDuration;

    private AudioPlayerService()
    {
        _positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _positionTimer.Tick += OnPositionTimerTick;

        // Auto-select default device
        try
        {
            var def = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _selectedDeviceId = def.ID;
        }
        catch
        {
            _selectedDeviceId = null;
        }

        try
        {
            SystemMediaTransportControlsService.Instance.Initialize(this);
        }
        catch { }
    }

    public List<AudioOutputDeviceInfo> GetAvailableOutputDevices()
    {
        var list = new List<AudioOutputDeviceInfo>();
        try
        {
            var endpoints = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            MMDevice? defaultDevice = null;
            try
            {
                defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
            catch { }

            foreach (var ep in endpoints)
            {
                bool isDef = defaultDevice != null && ep.ID == defaultDevice.ID;
                bool isSel = _selectedDeviceId != null ? ep.ID == _selectedDeviceId : isDef;

                list.Add(new AudioOutputDeviceInfo
                {
                    Id = ep.ID,
                    Name = ep.FriendlyName,
                    IsDefault = isDef,
                    IsSelected = isSel
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to enumerate audio devices: {ex.Message}");
        }

        return list;
    }

    public string GetSelectedDeviceName()
    {
        try
        {
            if (!string.IsNullOrEmpty(_selectedDeviceId))
            {
                var dev = _deviceEnumerator.GetDevice(_selectedDeviceId);
                return dev.FriendlyName;
            }
            var def = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return def.FriendlyName;
        }
        catch
        {
            return "Windows Default Audio Device";
        }
    }

    public MMDevice GetCurrentMMDevice()
    {
        if (!string.IsNullOrEmpty(_selectedDeviceId))
        {
            try
            {
                return _deviceEnumerator.GetDevice(_selectedDeviceId);
            }
            catch
            {
                // fallback to default
            }
        }
        return _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    public void PlayTrackAt(int index)
    {
        if (index < 0 || index >= Queue.Count) return;

        CurrentQueueIndex = index;
        var track = Queue[index];

        PlayTrack(track);
    }

    public void PlayTrack(Track track)
    {
        int existingIndex = _playbackQueue.IndexOf(track);
        if (existingIndex >= 0)
        {
            CurrentQueueIndex = existingIndex;
        }
        else
        {
            _originalQueue.Add(track);
            _playbackQueue.Add(track);
            Queue.Add(track);
            CurrentQueueIndex = Queue.Count - 1;
        }

        StartPlaybackForCurrentTrack();
    }

    public void ReplaceQueueAndPlay(IEnumerable<Track> tracks, Track targetTrack, bool shuffle)
    {
        var list = tracks.ToList();
        if (list.Count == 0) return;

        Stop();
        _originalQueue.Clear();
        _originalQueue.AddRange(list);
        _isShuffle = shuffle;

        _playbackQueue.Clear();
        if (shuffle)
        {
            var pool = new List<Track>(list);
            pool.Remove(targetTrack);
            FisherYatesShuffle(pool);
            _playbackQueue.Add(targetTrack);
            _playbackQueue.AddRange(pool);
        }
        else
        {
            _playbackQueue.AddRange(list);
        }

        SyncQueueCollection(targetTrack);
        StartPlaybackForCurrentTrack();
    }

    public void PlayQueue(IEnumerable<Track> tracks, int startIndex = 0)
    {
        var list = tracks.ToList();
        if (list.Count == 0) return;

        Stop();
        _originalQueue.Clear();
        _originalQueue.AddRange(list);

        _playbackQueue.Clear();
        _playbackQueue.AddRange(list);

        SyncQueueCollection(null);

        if (startIndex < 0 || startIndex >= Queue.Count)
            startIndex = 0;

        CurrentQueueIndex = startIndex;
        StartPlaybackForCurrentTrack();
    }

    public void PlayNext(Track track)
    {
        if (Queue.Count == 0 || _currentQueueIndex < 0)
        {
            AddToQueue(track, playImmediately: true);
            return;
        }

        int nextIndex = _currentQueueIndex + 1;
        if (nextIndex > Queue.Count) nextIndex = Queue.Count;

        _originalQueue.Insert(Math.Min(nextIndex, _originalQueue.Count), track);
        _playbackQueue.Insert(Math.Min(nextIndex, _playbackQueue.Count), track);
        Queue.Insert(nextIndex, track);
    }

    public void LoadTrackPaused(Track track, TimeSpan position)
    {
        if (!File.Exists(track.FilePath)) return;

        try
        {
            _suppressStopEvent = true;
            CleanupAudioEngine();
            _suppressStopEvent = false;

            _currentReader = AudioDecoderFactory.CreateReader(track.FilePath);
            track.Duration = _currentReader.TotalTime;
            track.SampleRate = _currentReader.WaveFormat.SampleRate;
            track.BitsPerSample = _currentReader.WaveFormat.BitsPerSample;
            track.Channels = _currentReader.WaveFormat.Channels;

            InitializeWasapiOutput(_currentReader);

            if (position > TimeSpan.Zero && position < _currentReader.TotalTime)
            {
                _currentReader.CurrentTime = position;
            }

            PlaybackState = PlaybackState.Paused;
            TrackChanged?.Invoke(this, track);
            PositionChanged?.Invoke(this, _currentReader.CurrentTime);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading paused track: {ex.Message}");
        }
    }

    public void AddToQueue(Track track, bool playImmediately = false)
    {
        _originalQueue.Add(track);
        _playbackQueue.Add(track);
        Queue.Add(track);
        if (playImmediately || Queue.Count == 1)
        {
            PlayTrackAt(Queue.Count - 1);
        }
    }

    public void AddRangeToQueue(IEnumerable<Track> tracks, bool playFirst = false)
    {
        int initialCount = Queue.Count;
        var list = tracks.ToList();
        _originalQueue.AddRange(list);
        _playbackQueue.AddRange(list);
        foreach (var t in list)
        {
            Queue.Add(t);
        }

        if (playFirst && Queue.Count > initialCount)
        {
            PlayTrackAt(initialCount);
        }
    }

    public void RemoveFromQueue(int index)
    {
        if (index < 0 || index >= _playbackQueue.Count) return;

        var track = _playbackQueue[index];
        _originalQueue.Remove(track);
        _playbackQueue.RemoveAt(index);
        bool isCurrent = index == _currentQueueIndex;
        Queue.RemoveAt(index);

        if (Queue.Count == 0)
        {
            Stop();
            CurrentQueueIndex = -1;
            TrackChanged?.Invoke(this, null);
        }
        else if (isCurrent)
        {
            if (_currentQueueIndex >= Queue.Count)
            {
                _currentQueueIndex = 0;
            }
            PlayTrackAt(_currentQueueIndex);
        }
        else if (index < _currentQueueIndex)
        {
            CurrentQueueIndex = _currentQueueIndex - 1;
        }
    }

    public void MoveQueueItem(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= Queue.Count || newIndex < 0 || newIndex >= Queue.Count) return;

        var item = Queue[oldIndex];
        bool isCurrent = oldIndex == _currentQueueIndex;

        Queue.Move(oldIndex, newIndex);
        if (oldIndex < _playbackQueue.Count && newIndex < _playbackQueue.Count)
        {
            var pItem = _playbackQueue[oldIndex];
            _playbackQueue.RemoveAt(oldIndex);
            _playbackQueue.Insert(newIndex, pItem);
        }

        if (isCurrent)
        {
            CurrentQueueIndex = newIndex;
        }
        else if (oldIndex < _currentQueueIndex && newIndex >= _currentQueueIndex)
        {
            CurrentQueueIndex = _currentQueueIndex - 1;
        }
        else if (oldIndex > _currentQueueIndex && newIndex <= _currentQueueIndex)
        {
            CurrentQueueIndex = _currentQueueIndex + 1;
        }
    }

    public void ClearQueue()
    {
        Stop();
        _originalQueue.Clear();
        _playbackQueue.Clear();
        Queue.Clear();
        CurrentQueueIndex = -1;
        TrackChanged?.Invoke(this, null);
    }

    private void StartPlaybackForCurrentTrack()
    {
        var track = CurrentTrack;
        if (track == null || !File.Exists(track.FilePath))
        {
            NotificationRaised?.Invoke(this, ("Archivo no encontrado", $"No se pudo localizar: {track?.FilePath}", true));
            return;
        }

        try
        {
            _suppressStopEvent = true;
            CleanupAudioEngine();
            _suppressStopEvent = false;

            _currentReader = AudioDecoderFactory.CreateReader(track.FilePath);

            // Update real track info from reader
            track.Duration = _currentReader.TotalTime;
            track.SampleRate = _currentReader.WaveFormat.SampleRate;
            track.BitsPerSample = _currentReader.WaveFormat.BitsPerSample;
            track.Channels = _currentReader.WaveFormat.Channels;

            InitializeWasapiOutput(_currentReader);

            _wasapiOut?.Play();
            PlaybackState = PlaybackState.Playing;
            _positionTimer.Start();

            TrackChanged?.Invoke(this, track);
            RaisePlaybackStateChanged(track, true);
            _ = LibraryCacheService.Instance.RecordRecentTrackAsync(track);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error starting playback: {ex}");
            NotificationRaised?.Invoke(this, ("Error de Reproducción", ex.Message, true));
        }
    }

    private void ReinitializeAudioOutput()
    {
        if (_currentReader == null) return;

        try
        {
            _suppressStopEvent = true;
            _wasapiOut?.Stop();
            _wasapiOut?.Dispose();
            _wasapiOut = null;
            _suppressStopEvent = false;

            InitializeWasapiOutput(_currentReader);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reinitialize error: {ex.Message}");
        }
    }

    private void InitializeWasapiOutput(WaveStream reader)
    {
        var device = GetCurrentMMDevice();

        if (_isExclusiveMode)
        {
            InitializeWasapiExclusive(device, reader);
        }
        else
        {
            InitializeWasapiShared(device, reader);
        }
    }

    private void InitializeWasapiExclusive(MMDevice device, WaveStream reader)
    {
        try
        {
            var audioClient = device.AudioClient;
            var nativeFormat = reader.WaveFormat;
            WaveFormat? negotiatedFormat = null;

            // Step 1: Check if hardware natively supports exact file format
            if (audioClient.IsFormatSupported(AudioClientShareMode.Exclusive, nativeFormat))
            {
                negotiatedFormat = nativeFormat;
            }
            else
            {
                // Step 2: Many modern USB DACs require 24-bit in 32-bit container or 32-bit int
                var pcm32 = WaveFormat.CreateCustomFormat(
                    WaveFormatEncoding.Pcm,
                    nativeFormat.SampleRate,
                    nativeFormat.Channels,
                    nativeFormat.SampleRate * nativeFormat.Channels * 4,
                    nativeFormat.Channels * 4,
                    32
                );

                if (audioClient.IsFormatSupported(AudioClientShareMode.Exclusive, pcm32))
                {
                    negotiatedFormat = pcm32;
                }
                else
                {
                    // Step 3: Test 24-bit PCM
                    var pcm24 = WaveFormat.CreateCustomFormat(
                        WaveFormatEncoding.Pcm,
                        nativeFormat.SampleRate,
                        nativeFormat.Channels,
                        nativeFormat.SampleRate * nativeFormat.Channels * 3,
                        nativeFormat.Channels * 3,
                        24
                    );

                    if (audioClient.IsFormatSupported(AudioClientShareMode.Exclusive, pcm24))
                    {
                        negotiatedFormat = pcm24;
                    }
                    else
                    {
                        // Step 4: Test 32-bit IEEE float
                        var float32 = WaveFormat.CreateIeeeFloatWaveFormat(nativeFormat.SampleRate, nativeFormat.Channels);
                        if (audioClient.IsFormatSupported(AudioClientShareMode.Exclusive, float32))
                        {
                            negotiatedFormat = float32;
                        }
                    }
                }
            }

            if (negotiatedFormat != null)
            {
                _activeWaveProvider = AudioVisualizerService.Instance.Wrap(new NativeAudioPcmAdapter(reader, negotiatedFormat));
            }
            else
            {
                // Use reader directly as best effort
                _activeWaveProvider = AudioVisualizerService.Instance.Wrap(reader);
            }

            // In Exclusive Bit-Perfect mode, initialize with low latency (25ms) and event sync
            _wasapiOut = new WasapiOut(device, AudioClientShareMode.Exclusive, useEventSync: true, latency: 25);
            _wasapiOut.Init(_activeWaveProvider);

            // In Bit-Perfect mode, software DSP volume must be at 100% (0 dBFS)
            _wasapiOut.PlaybackStopped += OnWasapiPlaybackStopped;

            NotificationRaised?.Invoke(this, (
                "WASAPI Exclusive Bit-Perfect",
                $"Salida directa al DAC activada: {nativeFormat.SampleRate} Hz / {nativeFormat.BitsPerSample}-bit sin resampling.",
                false
            ));
        }
        catch (COMException comEx)
        {
            // AUDCLNT_E_DEVICE_IN_USE = 0x8889000A
            string errorDesc = ((uint)comEx.ErrorCode == 0x8889000A)
                ? "El dispositivo de audio está siendo utilizado exclusivamente por otra aplicación."
                : $"El hardware no admitió el formato exclusivo (Código: 0x{comEx.ErrorCode:X}).";

            NotificationRaised?.Invoke(this, (
                "Modo Exclusivo no disponible",
                $"{errorDesc} Cambiando automáticamente a WASAPI Shared.",
                true
            ));

            // Fallback cleanly to Shared
            _isExclusiveMode = false;
            AudioModeChanged?.Invoke(this, false);
            InitializeWasapiShared(device, reader);
        }
        catch (Exception ex)
        {
            NotificationRaised?.Invoke(this, (
                "Fallo en Modo Exclusivo",
                $"{ex.Message}. Conmutando a WASAPI Shared.",
                true
            ));

            _isExclusiveMode = false;
            AudioModeChanged?.Invoke(this, false);
            InitializeWasapiShared(device, reader);
        }
    }

    private void InitializeWasapiShared(MMDevice device, WaveStream reader)
    {
        var sampleProvider = reader.ToSampleProvider();
        var eqProvider = EqualizerService.Instance.Wrap(sampleProvider);
        var visualizer = AudioVisualizerService.Instance.Wrap(eqProvider);
        _volumeSampleProvider = new VolumeSampleProvider(visualizer);
        _activeWaveProvider = _volumeSampleProvider.ToWaveProvider();

        _wasapiOut = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: 50);
        _wasapiOut.Init(_activeWaveProvider);
        _wasapiOut.PlaybackStopped += OnWasapiPlaybackStopped;
        ApplyVolume();
    }

    private void ApplyVolume()
    {
        if (_isExclusiveMode)
        {
            if (_volumeSampleProvider != null)
            {
                _volumeSampleProvider.Volume = 1.0f;
            }
            return;
        }

        if (_volumeSampleProvider != null)
        {
            float effective = _isMuted ? 0f : Math.Clamp(_volume, 0f, 1f);
            _volumeSampleProvider.Volume = MathF.Pow(effective, 2.0f);
        }
    }

    public void Play()
    {
        if (CurrentTrack == null)
        {
            if (Queue.Count > 0)
            {
                PlayTrackAt(0);
            }
            return;
        }

        if (_wasapiOut == null && _currentReader == null)
        {
            StartPlaybackForCurrentTrack();
            return;
        }

        _wasapiOut?.Play();
        PlaybackState = PlaybackState.Playing;
        _positionTimer.Start();
        RaisePlaybackStateChanged(CurrentTrack, true);
    }

    public void Pause()
    {
        _wasapiOut?.Pause();
        PlaybackState = PlaybackState.Paused;
        _positionTimer.Stop();
        RaisePlaybackStateChanged(CurrentTrack, false);
    }

    public void TogglePlayPause()
    {
        if (PlaybackState == PlaybackState.Playing)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    public void Stop()
    {
        _positionTimer.Stop();
        _suppressStopEvent = true;
        _wasapiOut?.Stop();
        _suppressStopEvent = false;

        if (_currentReader != null)
        {
            _currentReader.Position = 0;
        }

        PlaybackState = PlaybackState.Stopped;
        PositionChanged?.Invoke(this, TimeSpan.Zero);
        RaisePlaybackStateChanged(null, false);
    }

    public void Next()
    {
        if (Queue.Count == 0) return;

        if (_repeatMode == RepeatMode.One)
        {
            Seek(TimeSpan.Zero);
            Play();
            return;
        }

        int nextIndex = _currentQueueIndex + 1;
        if (nextIndex >= Queue.Count)
        {
            if (_repeatMode == RepeatMode.All)
            {
                if (_isShuffle && _playbackQueue.Count > 1)
                {
                    var last = CurrentTrack;
                    _playbackQueue.Clear();
                    var pool = new List<Track>(_originalQueue);
                    if (last != null) pool.Remove(last);
                    FisherYatesShuffle(pool);
                    if (last != null) _playbackQueue.Add(last);
                    _playbackQueue.AddRange(pool);
                    SyncQueueCollection(null);
                }
                PlayTrackAt(0);
            }
            else
            {
                Stop();
            }
        }
        else
        {
            PlayTrackAt(nextIndex);
        }
    }

    public void Previous()
    {
        if (Queue.Count == 0) return;

        // If played more than 3 seconds, rewind to beginning
        if (CurrentPosition.TotalSeconds > 3)
        {
            Seek(TimeSpan.Zero);
            return;
        }

        int prevIndex = _currentQueueIndex - 1;
        if (prevIndex < 0)
        {
            if (_repeatMode == RepeatMode.All)
            {
                PlayTrackAt(Queue.Count - 1);
            }
            else
            {
                Seek(TimeSpan.Zero);
            }
        }
        else
        {
            PlayTrackAt(prevIndex);
        }
    }

    public void Seek(TimeSpan targetTime)
    {
        if (_currentReader == null) return;

        try
        {
            _isUserSeeking = true;
            if (targetTime < TimeSpan.Zero) targetTime = TimeSpan.Zero;
            if (targetTime > _currentReader.TotalTime) targetTime = _currentReader.TotalTime;

            // Direct byte-aligned seek when possible to prevent MediaFoundation/VBR rounding down to 0
            if (_currentReader.WaveFormat != null && _currentReader.WaveFormat.AverageBytesPerSecond > 0)
            {
                long targetBytePos = (long)(targetTime.TotalSeconds * _currentReader.WaveFormat.AverageBytesPerSecond);
                int blockAlign = Math.Max(1, _currentReader.WaveFormat.BlockAlign);
                targetBytePos -= (targetBytePos % blockAlign);
                _currentReader.Position = Math.Clamp(targetBytePos, 0, _currentReader.Length);
            }
            else
            {
                _currentReader.CurrentTime = targetTime;
            }

            PositionChanged?.Invoke(this, targetTime);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Seek failed: {ex.Message}");
            try
            {
                _currentReader.CurrentTime = targetTime;
                PositionChanged?.Invoke(this, targetTime);
            }
            catch { }
        }
        finally
        {
            _isUserSeeking = false;
        }
    }

    private void OnPositionTimerTick(object? sender, EventArgs e)
    {
        if (!_isUserSeeking && _currentReader != null && PlaybackState == PlaybackState.Playing)
        {
            PositionChanged?.Invoke(this, _currentReader.CurrentTime);
        }
    }

    private void OnWasapiPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (_suppressStopEvent) return;
        if (sender == null || sender != _wasapiOut) return;

        App.Current.Dispatcher.Invoke(() =>
        {
            // Always auto-advance to next song when current track ends normally
            if (_currentReader != null && _currentReader.CurrentTime >= _currentReader.TotalTime - TimeSpan.FromMilliseconds(500))
            {
                Next();
            }
            else
            {
                PlaybackState = PlaybackState.Stopped;
                RaisePlaybackStateChanged(null, false);
            }
        });
    }

    private void RaisePlaybackStateChanged(Track? track, bool isPlaying)
    {
        var app = Application.Current;
        if (app?.Dispatcher != null)
        {
            if (app.Dispatcher.CheckAccess())
            {
                OnPlaybackStateChanged?.Invoke(track, isPlaying);
            }
            else
            {
                app.Dispatcher.InvokeAsync(() => OnPlaybackStateChanged?.Invoke(track, isPlaying));
            }
        }
        else
        {
            OnPlaybackStateChanged?.Invoke(track, isPlaying);
        }
    }

    private void UpdateQueueItemStatus()
    {
        for (int i = 0; i < Queue.Count; i++)
        {
            Queue[i].IsPlaying = (i == _currentQueueIndex);
        }
    }

    private void CleanupAudioEngine()
    {
        _positionTimer.Stop();

        if (_wasapiOut != null)
        {
            var oldOut = _wasapiOut;
            _wasapiOut = null;
            oldOut.PlaybackStopped -= OnWasapiPlaybackStopped;
            try { oldOut.Stop(); } catch { }
            try { oldOut.Dispose(); } catch { }
        }

        if (_currentReader != null)
        {
            try { _currentReader.Dispose(); } catch { }
            _currentReader = null;
        }

        _volumeSampleProvider = null;
        _activeWaveProvider = null;
    }
}
