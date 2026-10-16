using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using BenyComes.Data;
using BenyComes.Models;
using BenyComes.Services.Audio;
using BenyComes.Services.Library;
using BenyComes.Services.Metadata;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BenyComes.ViewModels;

public partial class RecentPlaysViewModel : ObservableObject
{
    private readonly LibraryDao _libraryDao = LibraryDao.Instance;
    private readonly LibraryCacheService _cache = LibraryCacheService.Instance;
    private readonly AudioPlayerService _audio = AudioPlayerService.Instance;

    public ObservableCollection<Track> RecentTracks => _cache.RecentTracks;
    public ObservableCollection<AlbumModel> RecentAlbums => _cache.RecentAlbums;

    public RecentPlaysViewModel()
    {
        _cache.RecentPlaysUpdated += () =>
        {
            OnPropertyChanged(nameof(RecentTracks));
            OnPropertyChanged(nameof(RecentAlbums));
        };
        _ = LoadRecentAsync();
    }

    [RelayCommand]
    public async Task LoadRecentAsync()
    {
        await _cache.LoadRecentPlaysFromDbAsync();
    }

    [RelayCommand]
    private void PlayTrack(Track? track)
    {
        if (track == null) return;
        _audio.PlayTrack(track);
        MainViewModel.Instance?.ForceUiPlaybackUpdate(track, true);
    }

    [RelayCommand]
    private void PlayAlbum(AlbumModel? album)
    {
        if (album == null || album.Tracks.Count == 0) return;
        _audio.ClearQueue();
        _audio.AddRangeToQueue(album.Tracks, playFirst: true);
        MainViewModel.Instance?.ForceUiPlaybackUpdate(album.Tracks.FirstOrDefault(), true);
    }

    [RelayCommand]
    private void AddAlbumToQueue(AlbumModel? album)
    {
        if (album == null || album.Tracks.Count == 0) return;
        _audio.AddRangeToQueue(album.Tracks, playFirst: false);
    }

    [RelayCommand]
    private async Task PlayAllRandomAsync()
    {
        var all = _cache.CachedTracks.Count > 0 ? _cache.CachedTracks : await _libraryDao.GetAllTracksAsync();
        if (all.Count == 0) return;

        var pool = all.ToList();
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        _audio.ClearQueue();
        _audio.AddRangeToQueue(pool, playFirst: true);
        _audio.IsShuffle = true;
        MainViewModel.Instance?.ForceUiPlaybackUpdate(pool.FirstOrDefault(), true);
    }
}
