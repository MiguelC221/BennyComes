using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BenyComes.Data;
using BenyComes.Models;
using BenyComes.Services.Audio;
using BenyComes.Services.Metadata;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace BenyComes.ViewModels;

public partial class QueueViewModel : ObservableObject
{
    private readonly AudioPlayerService _audio = AudioPlayerService.Instance;
    private readonly LibraryDao _libraryDao = LibraryDao.Instance;
    private readonly PlaylistDao _playlistDao = PlaylistDao.Instance;

    public ObservableCollection<Track> Queue => _audio.Queue;

    [ObservableProperty]
    private Track? _selectedTrack;

    [ObservableProperty]
    private string _newPlaylistName = string.Empty;

    public int TrackCount => Queue.Count;

    public QueueViewModel()
    {
        Queue.CollectionChanged += (s, e) => OnPropertyChanged(nameof(TrackCount));
    }

    [RelayCommand]
    private void PlayTrack(Track? track)
    {
        if (track == null) return;
        _audio.PlayTrack(track);
    }

    [RelayCommand]
    private void RemoveTrack(Track? track)
    {
        if (track == null) return;
        int idx = Queue.IndexOf(track);
        if (idx >= 0)
        {
            _audio.RemoveFromQueue(idx);
        }
    }

    [RelayCommand]
    private void MoveUp(Track? track)
    {
        if (track == null) return;
        int idx = Queue.IndexOf(track);
        if (idx > 0)
        {
            _audio.MoveQueueItem(idx, idx - 1);
        }
    }

    [RelayCommand]
    private void MoveDown(Track? track)
    {
        if (track == null) return;
        int idx = Queue.IndexOf(track);
        if (idx >= 0 && idx < Queue.Count - 1)
        {
            _audio.MoveQueueItem(idx, idx + 1);
        }
    }

    [RelayCommand]
    private void ClearQueue()
    {
        _audio.ClearQueue();
    }

    [RelayCommand]
    private async Task AddFilesAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Seleccionar Archivos de Audio",
            Filter = "Audio Compatible (*.flac;*.ogg;*.wav;*.mp3;*.m4a;*.aac)|*.flac;*.ogg;*.wav;*.mp3;*.m4a;*.aac;*.wma|FLAC Lossless (*.flac)|*.flac|Ogg Vorbis (*.ogg)|*.ogg|WAV (*.wav)|*.wav|MP3 (*.mp3)|*.mp3|Todos los archivos (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            foreach (var file in dialog.FileNames)
            {
                var track = await MetadataService.Instance.LoadTrackMetadataAsync(file);
                await _libraryDao.InsertOrUpdateTrackAsync(track);
                _audio.AddToQueue(track, playImmediately: Queue.Count == 0);
            }
        }
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Seleccionar Carpeta con Música"
        };

        if (dialog.ShowDialog() == true && Directory.Exists(dialog.FolderName))
        {
            string[] extensions = { ".flac", ".ogg", ".wav", ".mp3", ".m4a", ".aac", ".wma" };
            var files = Directory.EnumerateFiles(dialog.FolderName, "*.*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();

            bool wasEmpty = Queue.Count == 0;
            foreach (var file in files)
            {
                var track = await MetadataService.Instance.LoadTrackMetadataAsync(file);
                await _libraryDao.InsertOrUpdateTrackAsync(track);
                _audio.AddToQueue(track, playImmediately: false);
            }

            if (wasEmpty && Queue.Count > 0)
            {
                _audio.PlayTrackAt(0);
            }
        }
    }

    [RelayCommand]
    private async Task SaveQueueAsPlaylistAsync(string? customName = null)
    {
        if (Queue.Count == 0) return;

        string name = !string.IsNullOrWhiteSpace(customName)
            ? customName
            : (!string.IsNullOrWhiteSpace(NewPlaylistName) ? NewPlaylistName : $"Cola {DateTime.Now:dd/MM/yyyy HH:mm}");

        await _playlistDao.CreatePlaylistAsync(name, Queue);
        NewPlaylistName = string.Empty;
    }
}
