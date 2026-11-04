using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using BenyComes.Helpers;
using BenyComes.Models;
using BenyComes.Services.Audio;
using BenyComes.Services.Library;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;

namespace BenyComes.ViewModels;

public partial class OsuLibraryViewModel : ObservableObject
{
    private readonly OsuLibraryService _osuService = OsuLibraryService.Instance;
    private readonly AudioPlayerService _audio = AudioPlayerService.Instance;

    [ObservableProperty]
    private int _selectedTabIndex = 0; // 0 = Canciones, 1 = Artistas, 2 = Con vídeo

    [ObservableProperty]
    private string _searchFilter = string.Empty;

    [ObservableProperty]
    private int _totalTracksCount;

    [ObservableProperty]
    private int _totalArtistsCount;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private ArtistModel? _selectedArtistDetail;

    [ObservableProperty]
    private AlbumModel? _selectedAlbumDetail;

    public bool IsInDetailView => SelectedArtistDetail != null || SelectedAlbumDetail != null;

    [ObservableProperty]
    private bool _isFetchingAlbumInfo;

    public ObservableCollection<string> ArtistSortOptions { get; } = new();

    [ObservableProperty]
    private string _selectedArtistSort = "Nombre (A-Z)";

    [ObservableProperty]
    private bool _isSortDescending = false;

    public string SortDirectionTooltip => IsSortDescending ? "Orden descendente (clic para ascendente)" : "Orden ascendente (clic para descendente)";
    public SymbolRegular SortDirectionIcon => IsSortDescending ? SymbolRegular.ArrowDown24 : SymbolRegular.ArrowUp24;

    public bool IsAlphabetEnabled => SelectedTabIndex != 1 || !string.Equals(SelectedArtistSort, "Cantidad de canciones", StringComparison.OrdinalIgnoreCase);

    public BulkObservableCollection<Track> Songs { get; } = new();
    public BulkObservableCollection<Track> VideoSongs { get; } = new();
    public BulkObservableCollection<ArtistModel> Artists { get; } = new();

    [ObservableProperty]
    private ObservableCollection<ArtistLetterSection> _artistSections = new();

    private int _artistSectionsBuildVersion;

    // JumpList State
    [ObservableProperty]
    private bool _isJumpListOpen;

    public ObservableCollection<JumpListLetterItem> JumpListLetters { get; } = new();
    public event Action<string>? RequestScrollToLetter;

    // Doubtful / Ambiguous Artist Pairs State
    public ObservableCollection<DoubtfulArtistPair> DoubtfulArtistPairs { get; } = new();

    public bool HasDoubtfulArtists => DoubtfulArtistPairs.Count > 0;

    public string DoubtfulBannerText => DoubtfulArtistPairs.Count == 1
        ? "Se detectó 1 artista similar pendiente de revisión"
        : $"Se detectaron {DoubtfulArtistPairs.Count} artistas similares";

    [ObservableProperty]
    private bool _isDoubtfulDialogOpen;

    [RelayCommand]
    public void OpenDoubtfulDialog()
    {
        if (DoubtfulArtistPairs.Count > 0)
            IsDoubtfulDialogOpen = true;
    }

    [RelayCommand]
    public void CloseDoubtfulDialog()
    {
        IsDoubtfulDialogOpen = false;
    }

    [RelayCommand]
    public void CombineDoubtfulPair(DoubtfulArtistPair? pair)
    {
        if (pair == null) return;
        OsuArtistAliasService.Instance.SetAlias(pair.UnicodeName, pair.RomajiName, saveImmediately: true);
        DoubtfulArtistPairs.Remove(pair);
        OnPropertyChanged(nameof(HasDoubtfulArtists));
        OnPropertyChanged(nameof(DoubtfulBannerText));
        if (DoubtfulArtistPairs.Count == 0) IsDoubtfulDialogOpen = false;
        _osuService.ReindexOsuArtists();
    }

    [RelayCommand]
    public void RejectDoubtfulPair(DoubtfulArtistPair? pair)
    {
        if (pair == null) return;
        OsuArtistAliasService.Instance.RejectPair(pair.UnicodeName, pair.RomajiName, saveImmediately: true);
        DoubtfulArtistPairs.Remove(pair);
        OnPropertyChanged(nameof(HasDoubtfulArtists));
        OnPropertyChanged(nameof(DoubtfulBannerText));
        if (DoubtfulArtistPairs.Count == 0) IsDoubtfulDialogOpen = false;
        _osuService.ReindexOsuArtists();
    }

    public OsuLibraryViewModel()
    {
        ArtistSortOptions.Add("Nombre (A-Z)");
        ArtistSortOptions.Add("Cantidad de canciones");

        _osuService.LibraryUpdated += OnLibraryUpdated;
        _audio.TrackChanged += OnAudioTrackChanged;
        _audio.PlaybackStateChanged += OnAudioPlaybackStateChanged;

        RefreshData();
    }

    private void OnLibraryUpdated()
    {
        App.Current?.Dispatcher?.InvokeAsync(RefreshData);
    }

    private void OnAudioTrackChanged(object? sender, Track? current)
    {
        SyncPlayingState(current, _audio.IsPlaying);
    }

    private void OnAudioPlaybackStateChanged(object? sender, PlaybackState state)
    {
        SyncPlayingState(_audio.CurrentTrack, state == PlaybackState.Playing);
    }

    private void SyncPlayingState(Track? current, bool isPlaying)
    {
        string? currentPath = current?.FilePath;
        string? playingPath = isPlaying && current != null ? current.FilePath : null;

        void UpdateList(IEnumerable<Track> list)
        {
            foreach (var t in list)
            {
                bool isCur = !string.IsNullOrEmpty(currentPath) && string.Equals(t.FilePath, currentPath, StringComparison.OrdinalIgnoreCase);
                if (t.IsCurrent != isCur) t.IsCurrent = isCur;
                bool match = playingPath != null && isCur;
                if (t.IsPlaying != match) t.IsPlaying = match;
            }
        }

        UpdateList(Songs);
        UpdateList(VideoSongs);
        if (SelectedArtistDetail?.Tracks != null) UpdateList(SelectedArtistDetail.Tracks);
        if (SelectedAlbumDetail?.Tracks != null) UpdateList(SelectedAlbumDetail.Tracks);
    }

    partial void OnSearchFilterChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedArtistSortChanged(string value)
    {
        OnPropertyChanged(nameof(IsAlphabetEnabled));
        ApplyFilter();
    }

    [RelayCommand]
    private void ToggleSortDirection()
    {
        IsSortDescending = !IsSortDescending;
        OnPropertyChanged(nameof(SortDirectionTooltip));
        OnPropertyChanged(nameof(SortDirectionIcon));
        ApplyFilter();
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        IsJumpListOpen = false;
        SelectedArtistDetail = null;
        SelectedAlbumDetail = null;
        OnPropertyChanged(nameof(IsAlphabetEnabled));
        ApplyFilter();
        sw.Stop();
        System.Diagnostics.Debug.WriteLine($"[PERF] OsuLibrary.OnSelectedTabIndexChanged(tab={value}): {sw.Elapsed.TotalMilliseconds:F1} ms");
    }

    partial void OnSelectedArtistDetailChanged(ArtistModel? value)
    {
        OnPropertyChanged(nameof(IsInDetailView));
        if (value != null)
        {
            SelectedAlbumDetail = null;
            SyncPlayingState(_audio.CurrentTrack, _audio.IsPlaying);

            // Fetch artist bio if missing (Romaji first, then Unicode AltNames fallback)
            if (string.IsNullOrEmpty(value.BiographySummary) && Services.Online.LastFmService.Instance.IsEnabled)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var bio = await Services.Online.LastFmService.Instance.FetchArtistBioViaApiAsync(value.Name);
                        if (string.IsNullOrEmpty(bio.BioSummary) && value.AltNames.Count > 0)
                        {
                            foreach (var alt in value.AltNames)
                            {
                                bio = await Services.Online.LastFmService.Instance.FetchArtistBioViaApiAsync(alt);
                                if (!string.IsNullOrEmpty(bio.BioSummary)) break;
                            }
                        }
                        if (!string.IsNullOrEmpty(bio.BioSummary))
                        {
                            App.Current?.Dispatcher?.InvokeAsync(() =>
                            {
                                value.BiographySummary = bio.BioSummary;
                                value.BiographyFull = bio.BioFull ?? bio.BioSummary;
                                value.HasBiography = true;
                            });
                        }
                    }
                    catch { }
                });
            }
        }
    }

    partial void OnSelectedAlbumDetailChanged(AlbumModel? value)
    {
        OnPropertyChanged(nameof(IsInDetailView));
        if (value != null)
        {
            SyncPlayingState(_audio.CurrentTrack, _audio.IsPlaying);
        }
    }

    public void RefreshData()
    {
        TotalTracksCount = _osuService.Tracks.Count;
        TotalArtistsCount = _osuService.Artists.Count;

        DoubtfulArtistPairs.Clear();
        foreach (var pair in _osuService.DoubtfulArtists)
        {
            DoubtfulArtistPairs.Add(pair);
        }
        OnPropertyChanged(nameof(HasDoubtfulArtists));
        OnPropertyChanged(nameof(DoubtfulBannerText));
        if (DoubtfulArtistPairs.Count == 0)
        {
            IsDoubtfulDialogOpen = false;
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var swTotal = System.Diagnostics.Stopwatch.StartNew();
        string query = SearchFilter?.Trim() ?? string.Empty;

        // 1. Filtrar canciones (por título, Artist, OsuArtistRoumaji o OsuArtistUnicode)
        var swStep = System.Diagnostics.Stopwatch.StartNew();
        IEnumerable<Track> trackQuery = _osuService.Tracks;
        if (!string.IsNullOrEmpty(query))
        {
            trackQuery = trackQuery.Where(t =>
                t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(t.OsuArtistRoumaji) && t.OsuArtistRoumaji.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(t.OsuArtistUnicode) && t.OsuArtistUnicode.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        var trackList = trackQuery.ToList();
        Songs.ReplaceRange(trackList);
        VideoSongs.ReplaceRange(trackList.Where(t => t.HasVideo).ToList());
        System.Diagnostics.Debug.WriteLine($"[PERF] OsuLibrary.ApplyFilter Songs.ReplaceRange(count={trackList.Count}, video={VideoSongs.Count}): {swStep.Elapsed.TotalMilliseconds:F1} ms");

        swStep.Restart();
        UpdateSongsGrouping();
        UpdateVideoSongsGrouping();
        System.Diagnostics.Debug.WriteLine($"[PERF] OsuLibrary.ApplyFilter UpdateSongsGrouping: {swStep.Elapsed.TotalMilliseconds:F1} ms");

        swStep.Restart();
        SyncPlayingState(_audio.CurrentTrack, _audio.IsPlaying);
        System.Diagnostics.Debug.WriteLine($"[PERF] OsuLibrary.ApplyFilter SyncPlayingState: {swStep.Elapsed.TotalMilliseconds:F1} ms");

        // 2. Filtrar y ordenar artistas, y construir secciones A-Z + # en Task.Run
        var sourceArtists = _osuService.Artists.ToList();
        string sortOption = SelectedArtistSort;
        bool descending = IsSortDescending;
        int buildVersion = System.Threading.Interlocked.Increment(ref _artistSectionsBuildVersion);

        _ = Task.Run(() =>
        {
            var swBg = System.Diagnostics.Stopwatch.StartNew();
            IEnumerable<ArtistModel> artistQuery = sourceArtists;
            if (!string.IsNullOrEmpty(query))
            {
                artistQuery = artistQuery.Where(a => a.MatchesQuery(query));
            }

            bool isSortByName = string.Equals(sortOption, "Nombre (A-Z)", StringComparison.OrdinalIgnoreCase);
            bool isSortByTrackCount = string.Equals(sortOption, "Cantidad de canciones", StringComparison.OrdinalIgnoreCase);

            if (isSortByTrackCount)
            {
                artistQuery = descending
                    ? artistQuery.OrderBy(a => a.TrackCount).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                    : artistQuery.OrderByDescending(a => a.TrackCount).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase);
            }
            else // Nombre (A-Z)
            {
                artistQuery = descending
                    ? artistQuery.OrderByDescending(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                    : artistQuery.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase);
            }

            var filteredArtists = artistQuery.ToList();
            List<ArtistLetterSection> sections;
            if (isSortByTrackCount)
            {
                // Al filtrar por cantidad de canciones, se desagrupan los artistas mostrando todos en una lista plana continua sin cabecera de letra
                sections = new List<ArtistLetterSection>
                {
                    new ArtistLetterSection
                    {
                        Letter = string.Empty,
                        Artists = filteredArtists
                    }
                };
            }
            else
            {
                bool reverseSections = descending && isSortByName;
                sections = ArtistLetterSection.BuildSections(filteredArtists, reverseSections);
            }
            swBg.Stop();
            System.Diagnostics.Debug.WriteLine($"[PERF] OsuLibrary.ApplyFilter Task.Run(artists={filteredArtists.Count}, sections={sections.Count}): {swBg.Elapsed.TotalMilliseconds:F1} ms");

            if (buildVersion != _artistSectionsBuildVersion) return;

            App.Current?.Dispatcher?.Invoke(() =>
            {
                if (buildVersion != _artistSectionsBuildVersion) return;
                var swUi = System.Diagnostics.Stopwatch.StartNew();
                Artists.ReplaceRange(filteredArtists);
                ArtistSections = new ObservableCollection<ArtistLetterSection>(sections);
                swUi.Stop();
                System.Diagnostics.Debug.WriteLine($"[PERF] OsuLibrary.ApplyFilter UI ArtistSections apply: {swUi.Elapsed.TotalMilliseconds:F1} ms");
            });
        });

        swTotal.Stop();
        System.Diagnostics.Debug.WriteLine($"[PERF] OsuLibrary.ApplyFilter(sync part): {swTotal.Elapsed.TotalMilliseconds:F1} ms");
    }

    public void UpdateSongsGrouping()
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(Songs);
        if (view == null) return;

        if (view.GroupDescriptions.Count == 0)
        {
            using (view.DeferRefresh())
            {
                view.GroupDescriptions.Clear();
                view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("AlphaGroup"));
            }
        }
    }

    public void UpdateVideoSongsGrouping()
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(VideoSongs);
        if (view == null) return;

        if (view.GroupDescriptions.Count == 0)
        {
            using (view.DeferRefresh())
            {
                view.GroupDescriptions.Clear();
                view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("AlphaGroup"));
            }
        }
    }

    [RelayCommand]
    public void PlayTrack(Track? track)
    {
        if (track == null) return;
        var currentList = SelectedTabIndex == 2 ? VideoSongs.ToList() : Songs.ToList();
        if (currentList.Count == 0) currentList = Songs.ToList();
        _audio.ReplaceQueueAndPlay(currentList, track, _audio.IsShuffle);
    }

    [RelayCommand]
    public void PlayAllShuffled()
    {
        var pool = SelectedTabIndex == 2 ? VideoSongs.ToList() : Songs.ToList();
        if (pool.Count == 0) return;

        // Fisher-Yates shuffle
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        _audio.IsShuffle = true;
        _audio.PlayQueue(pool, 0);
    }

    [RelayCommand]
    public void OpenArtistDetail(ArtistModel? artist)
    {
        OpenArtistDetail(artist, skipPush: false);
    }

    public void OpenArtistDetail(ArtistModel? artist, bool skipPush = false)
    {
        if (artist == null) return;
        if (!skipPush)
        {
            MainViewModel.Instance?.PushCurrentNavigationState();
        }
        SelectedAlbumDetail = null;
        SelectedArtistDetail = artist;
    }

    [RelayCommand]
    public void CloseArtistDetail()
    {
        SelectedArtistDetail = null;
    }

    [RelayCommand]
    public void CloseAlbumDetail()
    {
        SelectedAlbumDetail = null;
    }

    [RelayCommand]
    public void OpenArtistByName(string? artistName)
    {
        if (string.IsNullOrWhiteSpace(artistName)) return;
        bool MatchesArtist(ArtistModel a, string name) =>
            string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase) ||
            a.AltNames.Any(alt => string.Equals(alt, name, StringComparison.OrdinalIgnoreCase));

        var artist = Artists.FirstOrDefault(a => MatchesArtist(a, artistName))
                  ?? _osuService.Artists.FirstOrDefault(a => MatchesArtist(a, artistName));

        if (artist == null)
        {
            var split = Services.Library.ArtistSplitterService.Instance.SplitArtists(artistName);
            if (split.Count > 0)
            {
                var first = split[0];
                artist = Artists.FirstOrDefault(a => MatchesArtist(a, first))
                      ?? _osuService.Artists.FirstOrDefault(a => MatchesArtist(a, first));
            }
        }

        if (artist != null)
        {
            OpenArtistDetail(artist);
        }
    }

    [RelayCommand]
    public async Task OpenTrackAlbumAsync(Track? track)
    {
        await OpenTrackAlbumInternalAsync(track, skipPush: false);
    }

    public async Task OpenTrackAlbumAsync(Track? track, bool skipPush)
    {
        await OpenTrackAlbumInternalAsync(track, skipPush: skipPush);
    }

    private async Task OpenTrackAlbumInternalAsync(Track? track, bool skipPush)
    {
        if (track == null) return;

        if (!skipPush)
        {
            MainViewModel.Instance?.PushCurrentNavigationState();
        }
        SelectedArtistDetail = null;

        // Inmediatamente mostrar el detalle del álbum con los datos ya conocidos
        // para dar retroalimentación instantánea al clic del usuario
        string localArt = !string.IsNullOrEmpty(track.ArtworkPath) ? track.ArtworkPath : string.Empty;
        string initialAlbumName = !string.IsNullOrWhiteSpace(track.Album) &&
                                  !string.Equals(track.Album, "osu! Beatmaps", StringComparison.OrdinalIgnoreCase)
                                  ? track.Album
                                  : (!string.IsNullOrWhiteSpace(track.Title) ? track.Title : "Álbum");

        var album = new AlbumModel
        {
            Name = initialAlbumName,
            Artist = track.Artist,
            ArtworkPath = localArt,
            Tracks = new List<Track> { track },
            PersistedTrackCount = 1,
            IsFetchingMetadata = true
        };

        SelectedAlbumDetail = album;
        IsFetchingAlbumInfo = true;

        try
        {
            string albumName = track.Album;
            string? coverUrl = null;

            if (string.Equals(albumName, "osu! Beatmaps", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(albumName))
            {
                string primaryArt = !string.IsNullOrWhiteSpace(track.OsuArtistRoumaji) ? track.OsuArtistRoumaji : track.Artist;
                string primaryTit = !string.IsNullOrWhiteSpace(track.OsuTitleRoumaji) ? track.OsuTitleRoumaji : track.Title;
                string? altArt = !string.IsNullOrWhiteSpace(track.OsuArtistUnicode) ? track.OsuArtistUnicode : null;
                string? altTit = !string.IsNullOrWhiteSpace(track.OsuTitleUnicode) ? track.OsuTitleUnicode : null;

                var info = await Services.Online.LastFmService.Instance.FetchTrackAlbumInfoAsync(primaryArt, primaryTit, altArt, altTit);
                if (!string.IsNullOrWhiteSpace(info.AlbumTitle))
                {
                    albumName = info.AlbumTitle;
                    coverUrl = info.AlbumCoverUrl;
                    track.Album = albumName;
                }
                else
                {
                    albumName = track.Title;
                }
            }

            // Si se obtuvo una carátula online de Last.fm, descargarla y guardarla en AppData para no descargarla de nuevo
            string finalCoverPath = localArt;
            if (!string.IsNullOrWhiteSpace(coverUrl))
            {
                string? cachedCover = await Services.Online.LastFmService.Instance.DownloadAndCacheAlbumCoverAsync(track.Artist, albumName, coverUrl);
                if (!string.IsNullOrEmpty(cachedCover))
                {
                    finalCoverPath = cachedCover;
                    track.ArtworkPath = cachedCover;
                }
                else
                {
                    finalCoverPath = coverUrl;
                }
            }

            album.Name = albumName;
            album.ArtworkPath = finalCoverPath;
            album.CoverImage = null;
            album.DominantHeaderBrush = null;
            album.RefreshStats();
        }
        finally
        {
            album.IsFetchingMetadata = false;
            IsFetchingAlbumInfo = false;
        }
    }

    [RelayCommand]
    public void OpenJumpList()
    {
        if (!IsAlphabetEnabled) return;
        JumpListLetters.Clear();
        var activeLetters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (SelectedTabIndex == 0)
        {
            foreach (var s in Songs)
            {
                if (!string.IsNullOrEmpty(s.AlphaGroup))
                    activeLetters.Add(s.AlphaGroup);
            }
        }
        else if (SelectedTabIndex == 1)
        {
            foreach (var sec in ArtistSections)
            {
                if (!string.IsNullOrEmpty(sec.Letter))
                    activeLetters.Add(sec.Letter);
            }
        }
        else if (SelectedTabIndex == 2)
        {
            foreach (var s in VideoSongs)
            {
                if (!string.IsNullOrEmpty(s.AlphaGroup))
                    activeLetters.Add(s.AlphaGroup);
            }
        }

        foreach (var letter in Helpers.AlphaGroupHelper.Alphabet)
        {
            JumpListLetters.Add(new JumpListLetterItem
            {
                Letter = letter,
                HasItems = activeLetters.Contains(letter)
            });
        }

        IsJumpListOpen = true;
    }

    [RelayCommand]
    public void CloseJumpList()
    {
        IsJumpListOpen = false;
    }

    [RelayCommand]
    public void SelectJumpLetter(string? letter)
    {
        if (string.IsNullOrEmpty(letter)) return;
        IsJumpListOpen = false;
        RequestScrollToLetter?.Invoke(letter);
    }

    [RelayCommand]
    public void SelectTab(string tabIndexStr)
    {
        if (int.TryParse(tabIndexStr, out int idx))
        {
            SelectedTabIndex = idx;
        }
    }

    [RelayCommand]
    public void PlayArtist(ArtistModel? artist)
    {
        if (artist == null || artist.Tracks == null || artist.Tracks.Count == 0) return;
        _audio.PlayQueue(artist.Tracks, 0);
    }

    [RelayCommand]
    public void AddTrackToQueue(Track? track)
    {
        if (track == null) return;
        _audio.AddRangeToQueue(new[] { track }, playFirst: false);
    }

    [RelayCommand]
    public void PlayTrackNext(Track? track)
    {
        if (track == null) return;
        _audio.PlayNext(track);
    }

    [RelayCommand]
    public void AddArtistToQueue(ArtistModel? artist)
    {
        if (artist == null || artist.Tracks == null || artist.Tracks.Count == 0) return;
        _audio.AddRangeToQueue(artist.Tracks, playFirst: false);
    }
}
