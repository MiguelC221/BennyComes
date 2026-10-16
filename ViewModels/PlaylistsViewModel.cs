using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using BenyComes.Data;
using BenyComes.Models;
using BenyComes.Services.Audio;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BenyComes.ViewModels;

public partial class PlaylistsViewModel : ObservableObject
{
    private readonly PlaylistDao _playlistDao = PlaylistDao.Instance;
    private readonly AudioPlayerService _audio = AudioPlayerService.Instance;

    public ObservableCollection<Playlist> Playlists { get; } = new();

    [ObservableProperty]
    private Playlist? _selectedPlaylist;

    [ObservableProperty]
    private string _newPlaylistName = string.Empty;

    public PlaylistsViewModel()
    {
        _ = LoadPlaylistsAsync();
    }

    [RelayCommand]
    public async Task LoadPlaylistsAsync()
    {
        Playlists.Clear();
        var list = await _playlistDao.GetAllPlaylistsAsync();
        foreach (var p in list)
        {
            Playlists.Add(p);
        }
    }

    [RelayCommand]
    private async Task PlayPlaylistAsync(Playlist? playlist)
    {
        if (playlist == null) return;

        var tracks = await _playlistDao.GetPlaylistTracksAsync(playlist.Id);
        if (tracks.Count > 0)
        {
            _audio.ClearQueue();
            _audio.AddRangeToQueue(tracks, playFirst: true);
        }
    }

    [RelayCommand]
    private async Task DeletePlaylistAsync(Playlist? playlist)
    {
        if (playlist == null) return;

        await _playlistDao.DeletePlaylistAsync(playlist.Id);
        Playlists.Remove(playlist);
    }

    [RelayCommand]
    private async Task CreatePlaylistAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPlaylistName)) return;

        var created = await _playlistDao.CreatePlaylistAsync(NewPlaylistName.Trim());
        Playlists.Add(created);
        NewPlaylistName = string.Empty;
    }
}
