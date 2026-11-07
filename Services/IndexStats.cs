using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.IndexJelly.Api;
using Jellyfin.Plugin.IndexJelly.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AudioItem = MediaBrowser.Controller.Entities.Audio.Audio;
using MusicAlbumItem = MediaBrowser.Controller.Entities.Audio.MusicAlbum;

namespace Jellyfin.Plugin.IndexJelly.Services;

/// <summary>Counted state of one library.</summary>
internal sealed class LibraryCounts
{
    public int Items { get; set; }

    public Dictionary<string, int> ItemTypes { get; } = new();

    public int AddedLast24h { get; set; }

    public DateTime? LastAddedUtc { get; set; }

    public Dictionary<string, CheckStatus> Checks { get; } = new();

    public Dictionary<string, List<MissingItem>> Missing { get; } = new();
}

/// <summary>One finished count of all libraries.</summary>
internal sealed class Snapshot
{
    public DateTime StartedAtUtc { get; set; }

    public DateTime ComputedAtUtc { get; set; }

    public long DurationMs { get; set; }

    public bool TrickplayAvailable { get; set; }

    public bool ChaptersAvailable { get; set; }

    public Dictionary<string, LibraryCounts> Libraries { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Walks every library and counts which items are already indexed. Walking a large library takes time
/// (one database lookup per video for media streams, trickplay and chapters), so the result is cached
/// and recomputed in the background; the API always answers immediately with the last finished count.
/// </summary>
internal static class IndexStats
{
    private static readonly object _sync = new();
    private static Snapshot? _current;
    private static Task? _running;
    private static string? _lastError;

    public static Snapshot? Current => _current;

    public static bool Computing => _running is { IsCompleted: false };

    public static string? LastError => _lastError;

    /// <summary>Starts a background count if the cached one is older than the configured age (or when forced).</summary>
    /// <param name="busy">An indexing task or library scan is running: the counts are kept at most a minute old
    /// (longer only if a count itself is slow, so counting never takes more than about a fifth of the time).</param>
    /// <param name="lastIndexingFinishUtc">When the last indexing task finished; a count started before that is stale.</param>
    public static void StartIfStale(ILibraryManager libraryManager, IMediaSourceManager mediaSourceManager, IServiceProvider services, ILogger logger, PluginConfiguration config, bool force, bool busy, DateTime? lastIndexingFinishUtc)
    {
        lock (_sync)
        {
            if (Computing)
            {
                return;
            }

            var maxAge = TimeSpan.FromMinutes(Math.Clamp(config.StatsCacheMinutes, 1, 1440));
            if (busy && _current is not null)
            {
                var busyAge = TimeSpan.FromMilliseconds(Math.Max(60_000, _current.DurationMs * 5));
                if (busyAge < maxAge)
                {
                    maxAge = busyAge;
                }
            }

            var outdated = _current is not null && lastIndexingFinishUtc.HasValue && lastIndexingFinishUtc.Value > _current.StartedAtUtc;
            if (!force && !outdated && _current is not null && DateTime.UtcNow - _current.ComputedAtUtc < maxAge)
            {
                return;
            }

            // The request scope ends before the count does, so the count gets its own scope for the looked-up services.
            var scope = services.GetRequiredService<IServiceScopeFactory>().CreateScope();
            var lookups = ReflectedLookups.Create(scope.ServiceProvider, logger);
            _running = Task.Run(async () =>
            {
                try
                {
                    _current = await ComputeAsync(libraryManager, mediaSourceManager, lookups, logger, config).ConfigureAwait(false);
                    _lastError = null;
                }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                    logger.LogError(ex, "IndexJelly: counting the libraries failed");
                }
                finally
                {
                    scope.Dispose();
                }
            });
        }
    }

    private static async Task<Snapshot> ComputeAsync(ILibraryManager libraryManager, IMediaSourceManager mediaSourceManager, ReflectedLookups lookups, ILogger logger, PluginConfiguration config)
    {
        var sw = Stopwatch.StartNew();
        var snapshot = new Snapshot
        {
            StartedAtUtc = DateTime.UtcNow,
            TrickplayAvailable = lookups.TrickplayAvailable,
            ChaptersAvailable = lookups.ChaptersAvailable
        };
        var limit = Math.Clamp(config.MissingListLimit, 10, 10000);
        var now = DateTime.UtcNow;

        foreach (var folder in libraryManager.GetVirtualFolders())
        {
            if (!Guid.TryParse(folder.ItemId, out var libraryId))
            {
                continue;
            }

            var counts = new LibraryCounts();
            var trickplayOn = config.CheckTrickplay && lookups.TrickplayAvailable && ReflectedLookups.GetBool(folder.LibraryOptions, "EnableTrickplayImageExtraction");
            var chaptersOn = config.CheckChapterImages && lookups.ChaptersAvailable && ReflectedLookups.GetBool(folder.LibraryOptions, "EnableChapterImageExtraction");

            foreach (var key in CheckKeys.All)
            {
                counts.Checks[key] = new CheckStatus { Key = key };
                counts.Missing[key] = new List<MissingItem>();
            }

            counts.Checks[CheckKeys.Trickplay].Disabled = !trickplayOn;
            counts.Checks[CheckKeys.ChapterImages].Disabled = !chaptersOn;

            IEnumerable<BaseItem> items;
            try
            {
                items = libraryManager.GetItemList(new InternalItemsQuery
                {
                    ParentId = libraryId,
                    Recursive = true,
                    IsVirtualItem = false
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "IndexJelly: could not list the items of library {Library}", folder.Name);
                snapshot.Libraries[libraryId.ToString("N")] = counts;
                continue;
            }

            foreach (var item in items)
            {
                var kind = KindOf(item);
                if (kind is null || item.IsVirtualItem || item.ExtraType.HasValue)
                {
                    continue;
                }

                counts.Items++;
                counts.ItemTypes[kind] = counts.ItemTypes.TryGetValue(kind, out var n) ? n + 1 : 1;

                var created = item.DateCreated;
                if (created != default)
                {
                    if (now - created < TimeSpan.FromHours(24))
                    {
                        counts.AddedLast24h++;
                    }

                    if (counts.LastAddedUtc is null || created > counts.LastAddedUtc)
                    {
                        counts.LastAddedUtc = created;
                    }
                }

                var isVideo = item is Video;
                var isAudio = item is AudioItem;
                var described = kind is "Movie" or "Episode" or "Series";
                var identified = kind is "Movie" or "Episode" or "Series" or "MusicAlbum" or "MusicVideo";

                Count(counts, CheckKeys.Refreshed, item, kind, config, limit, item.DateLastRefreshed != default);

                if (isVideo || isAudio)
                {
                    Count(counts, CheckKeys.MediaInfo, item, kind, config, limit, HasMediaInfo(item, isVideo, mediaSourceManager, logger));
                }

                if (identified)
                {
                    Count(counts, CheckKeys.PrimaryImage, item, kind, config, limit, item.HasImage(ImageType.Primary, 0));
                    Count(counts, CheckKeys.ProviderIds, item, kind, config, limit, item.ProviderIds is { } ids && ids.Values.Any(v => !string.IsNullOrWhiteSpace(v)));
                }

                if (described)
                {
                    Count(counts, CheckKeys.Overview, item, kind, config, limit, !string.IsNullOrWhiteSpace(item.Overview));
                }

                if (isVideo && trickplayOn)
                {
                    int? resolutions = null;
                    try
                    {
                        resolutions = await lookups.GetTrickplayCountAsync(item.Id).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "IndexJelly: trickplay lookup failed for {Item}", item.Id);
                    }

                    if (resolutions.HasValue)
                    {
                        Count(counts, CheckKeys.Trickplay, item, kind, config, limit, resolutions.Value > 0);
                    }
                }

                if (isVideo && chaptersOn)
                {
                    (int Chapters, int WithImage)? chapters = null;
                    try
                    {
                        chapters = lookups.GetChapterImages(item);
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "IndexJelly: chapter lookup failed for {Item}", item.Id);
                    }

                    // Items without chapters get no chapter images at all, so they are not counted.
                    if (chapters is { Chapters: > 0 } c)
                    {
                        Count(counts, CheckKeys.ChapterImages, item, kind, config, limit, c.WithImage >= c.Chapters);
                    }
                }
            }

            snapshot.Libraries[libraryId.ToString("N")] = counts;
        }

        snapshot.ComputedAtUtc = DateTime.UtcNow;
        snapshot.DurationMs = sw.ElapsedMilliseconds;
        logger.LogInformation("IndexJelly: counted {Libraries} libraries in {Ms} ms", snapshot.Libraries.Count, snapshot.DurationMs);
        return snapshot;
    }

    private static void Count(LibraryCounts counts, string key, BaseItem item, string kind, PluginConfiguration config, int limit, bool done)
    {
        var check = counts.Checks[key];
        check.Applicable++;
        if (done)
        {
            check.Done++;
            return;
        }

        var list = counts.Missing[key];
        if (list.Count < limit)
        {
            list.Add(ToMissing(item, kind, config));
        }
    }

    /// <summary>
    /// ffprobe writes the media streams; metadata providers only fill in the run time. So an item counts as
    /// probed when it has a video height (videos) or when the database holds a stream of the right type.
    /// </summary>
    private static bool HasMediaInfo(BaseItem item, bool isVideo, IMediaSourceManager mediaSourceManager, ILogger logger)
    {
        if (isVideo && ReflectedLookups.GetHeight(item) is > 0)
        {
            return true;
        }

        try
        {
            var wanted = isVideo ? MediaStreamType.Video : MediaStreamType.Audio;
            return mediaSourceManager.GetMediaStreams(item.Id).Any(s => s.Type == wanted);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "IndexJelly: media stream lookup failed for {Item}", item.Id);
            return item.RunTimeTicks is > 0;
        }
    }

    private static string? KindOf(BaseItem item)
    {
        return item switch
        {
            Episode => "Episode",
            Movie => "Movie",
            MusicVideo => "MusicVideo",
            Series => "Series",
            MusicAlbumItem => "MusicAlbum",
            AudioItem => "Audio",
            Video => "Video",
            _ => null
        };
    }

    private static MissingItem ToMissing(BaseItem item, string kind, PluginConfiguration config)
    {
        return new MissingItem
        {
            Id = item.Id.ToString("N"),
            Name = item.Name,
            Type = kind,
            SeriesName = (item as Episode)?.SeriesName,
            SeasonNumber = item is Episode ? item.ParentIndexNumber : null,
            EpisodeNumber = item is Episode ? item.IndexNumber : null,
            ProductionYear = item.ProductionYear,
            Path = config.ShowPaths ? item.Path : null,
            DateCreatedUtc = item.DateCreated == default ? null : item.DateCreated
        };
    }
}
