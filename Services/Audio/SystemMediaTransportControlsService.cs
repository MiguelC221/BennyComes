using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using BenyComes.Models;
using BenyComes.Services.Metadata;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;

namespace BenyComes.Services.Audio;

public class SystemMediaTransportControlsService
{
    private static readonly Lazy<SystemMediaTransportControlsService> _instance =
        new(() => new SystemMediaTransportControlsService());

    public static SystemMediaTransportControlsService Instance => _instance.Value;

    [ComImport]
    [Guid("ddb0472d-c911-4a1f-86d9-dc3d71a95f5a")]
    [InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
    private interface ISystemMediaTransportControlsInterop
    {
        IntPtr GetForWindow(IntPtr appWindow, [In] ref Guid riid);
    }

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(
        [MarshalAs(UnmanagedType.HString)] string activatableClassId,
        [In] ref Guid iid,
        out IntPtr factory);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [Out, MarshalAs(UnmanagedType.Interface)] out IWindowPropertyStore? propertyStore);

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IWindowPropertyStore
    {
        uint GetCount(out uint cProps);
        uint GetAt(uint iProp, out WndPropertyKey pkey);
        uint GetValue(ref WndPropertyKey key, IntPtr pv);
        uint SetValue(ref WndPropertyKey key, ref WndPropVariant pv);
        uint Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct WndPropertyKey
    {
        public Guid fmtid;
        public uint pid;
        public WndPropertyKey(Guid guid, uint id) { fmtid = guid; pid = id; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WndPropVariant
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public IntPtr pwszVal;
        public IntPtr unionPadding;
    }

    private SystemMediaTransportControls? _smtc;
    private MediaPlayer? _mediaPlayerHolder;
    private AudioPlayerService? _audio;
    private bool _isInitialized;
    private bool _isBoundToWindow;
    private bool _audioEventsSubscribed;
    private IntPtr _hwnd = IntPtr.Zero;

    public void Initialize(AudioPlayerService audio)
    {
        _audio = audio;
        TryInitialize();
    }

    public void InitializeForWindow(IntPtr hwnd)
    {
        _hwnd = hwnd;
        if (_hwnd != IntPtr.Zero && !_isBoundToWindow)
        {
            // Force re-binding to the actual Window HWND so Windows SMTC identifies "BennyComes" instead of "Unknown app"
            _isInitialized = false;
        }
        TryInitialize();
    }

    private static void StampWindowAumid(IntPtr hwnd)
    {
        try
        {
            App.EnsureWindowsAppIdentityRegistered();
            SetCurrentProcessExplicitAppUserModelID(App.AppUserModelId);

            Guid iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
            if (SHGetPropertyStoreForWindow(hwnd, ref iid, out var propStore) == 0 && propStore != null)
            {
                var key = new WndPropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
                var pv = new WndPropVariant
                {
                    vt = 31, // VT_LPWSTR
                    pwszVal = Marshal.StringToCoTaskMemUni(App.AppUserModelId)
                };
                try
                {
                    propStore.SetValue(ref key, ref pv);
                    propStore.Commit();
                }
                finally
                {
                    if (pv.pwszVal != IntPtr.Zero)
                        Marshal.FreeCoTaskMem(pv.pwszVal);
                }
            }
        }
        catch { }
    }

    private void TryInitialize()
    {
        if (_isInitialized) return;

        try
        {
            // 1. Try ISystemMediaTransportControlsInterop with the Window Handle (HWND)
            if (_hwnd != IntPtr.Zero)
            {
                try
                {
                    StampWindowAumid(_hwnd);

                    Guid interopGuid = new Guid("ddb0472d-c911-4a1f-86d9-dc3d71a95f5a");
                    int hr = RoGetActivationFactory("Windows.Media.SystemMediaTransportControls", ref interopGuid, out IntPtr factoryPtr);
                    if (hr == 0 && factoryPtr != IntPtr.Zero)
                    {
                        var interop = (ISystemMediaTransportControlsInterop)Marshal.GetObjectForIUnknown(factoryPtr);
                        Guid smtcGuid = new Guid("99fa3ff4-1742-42a6-902e-087d41f965ec");
                        IntPtr smtcPtr = interop.GetForWindow(_hwnd, ref smtcGuid);
                        if (smtcPtr != IntPtr.Zero)
                        {
                            if (_smtc != null)
                            {
                                try { _smtc.ButtonPressed -= OnSmtcButtonPressed; } catch { }
                            }
                            if (_mediaPlayerHolder != null)
                            {
                                try { _mediaPlayerHolder.Dispose(); } catch { }
                                _mediaPlayerHolder = null;
                            }

                            _smtc = WinRT.MarshalInterface<SystemMediaTransportControls>.FromAbi(smtcPtr);
                            _isBoundToWindow = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ISystemMediaTransportControlsInterop failed: {ex.Message}");
                }
            }

            // 2. Fallback: GetForCurrentView
            if (_smtc == null)
            {
                try
                {
                    _smtc = SystemMediaTransportControls.GetForCurrentView();
                }
                catch { }
            }

            // 3. Fallback: Win32 MediaPlayer holder (only if HWND is not yet available)
            if (_smtc == null && _hwnd == IntPtr.Zero)
            {
                _mediaPlayerHolder = new MediaPlayer();
                _mediaPlayerHolder.CommandManager.IsEnabled = false;
                _smtc = _mediaPlayerHolder.SystemMediaTransportControls;
            }

            if (_smtc != null)
            {
                _smtc.IsEnabled = true;
                _smtc.IsPlayEnabled = true;
                _smtc.IsPauseEnabled = true;
                _smtc.IsNextEnabled = true;
                _smtc.IsPreviousEnabled = true;
                _smtc.IsStopEnabled = true;

                _smtc.ButtonPressed -= OnSmtcButtonPressed;
                _smtc.ButtonPressed += OnSmtcButtonPressed;

                if (_audio != null && !_audioEventsSubscribed)
                {
                    _audio.TrackChanged += OnTrackChanged;
                    _audio.OnPlaybackStateChanged += OnPlaybackStateChanged;
                    _audioEventsSubscribed = true;
                }

                _isInitialized = true;

                if (_audio?.CurrentTrack != null)
                {
                    _ = UpdateMetadataAsync(_audio.CurrentTrack);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize SMTC: {ex.Message}");
        }
    }

    private void OnSmtcButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        if (_audio == null) return;

        var app = Application.Current;
        if (app?.Dispatcher == null) return;

        app.Dispatcher.Invoke(() =>
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Play:
                    _audio.Play();
                    break;
                case SystemMediaTransportControlsButton.Pause:
                    _audio.Pause();
                    break;
                case SystemMediaTransportControlsButton.Next:
                    _audio.Next();
                    break;
                case SystemMediaTransportControlsButton.Previous:
                    _audio.Previous();
                    break;
                case SystemMediaTransportControlsButton.Stop:
                    _audio.Stop();
                    break;
            }
        });
    }

    private void OnTrackChanged(object? sender, Track? track)
    {
        if (_smtc == null) return;
        _ = UpdateMetadataAsync(track);
    }

    private void OnPlaybackStateChanged(Track? track, bool isPlaying)
    {
        if (_smtc == null) return;

        var app = Application.Current;
        if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.InvokeAsync(() => UpdatePlaybackStatus(isPlaying));
        }
        else
        {
            UpdatePlaybackStatus(isPlaying);
        }
    }

    private void UpdatePlaybackStatus(bool isPlaying)
    {
        if (_smtc == null) return;
        try
        {
            _smtc.PlaybackStatus = isPlaying ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;
        }
        catch { }
    }

    public async Task UpdateMetadataAsync(Track? track)
    {
        if (_smtc == null) return;

        try
        {
            var updater = _smtc.DisplayUpdater;
            updater.Type = MediaPlaybackType.Music;
            updater.AppMediaId = App.AppUserModelId;

            if (track == null)
            {
                updater.MusicProperties.Title = string.Empty;
                updater.MusicProperties.Artist = string.Empty;
                updater.MusicProperties.AlbumTitle = string.Empty;
                updater.Thumbnail = null;
                _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
                updater.Update();
                return;
            }

            updater.MusicProperties.Title = string.IsNullOrWhiteSpace(track.Title) ? "Canción desconocida" : track.Title;
            updater.MusicProperties.Artist = string.IsNullOrWhiteSpace(track.Artist) ? "Artista desconocido" : track.Artist;
            updater.MusicProperties.AlbumTitle = track.Album ?? string.Empty;

            // Resolve Artwork disk path (ensuring extracted and cached)
            string? artDiskPath = MetadataService.Instance.EnsureAlbumArtDiskPath(track);
            if (!string.IsNullOrEmpty(artDiskPath) && File.Exists(artDiskPath))
            {
                try
                {
                    string fullPath = Path.GetFullPath(artDiskPath);
                    var storageFile = await StorageFile.GetFileFromPathAsync(fullPath);
                    updater.Thumbnail = RandomAccessStreamReference.CreateFromFile(storageFile);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to set thumbnail: {ex.Message}");
                    updater.Thumbnail = null;
                }
            }
            else
            {
                updater.Thumbnail = null;
            }

            updater.Update();
            _smtc.PlaybackStatus = _audio != null && _audio.IsPlaying ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to update SMTC metadata: {ex.Message}");
        }
    }
}
