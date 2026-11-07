using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.IndexJelly.Api;

/// <summary>Check keys, shared by the API and the dashboard page.</summary>
public static class CheckKeys
{
    /// <summary>The item went through at least one metadata refresh.</summary>
    public const string Refreshed = "refreshed";

    /// <summary>ffprobe ran: run time and (for video) resolution are known.</summary>
    public const string MediaInfo = "mediaInfo";

    /// <summary>The item has a primary image (poster, cover, episode thumbnail).</summary>
    public const string PrimaryImage = "primaryImage";

    /// <summary>The item has an overview text.</summary>
    public const string Overview = "overview";

    /// <summary>The item is identified: it has at least one provider id (TMDb, TVDb, IMDb, MusicBrainz…).</summary>
    public const string ProviderIds = "providerIds";

    /// <summary>Trickplay (seek preview) images exist. Only counted in libraries that have trickplay enabled.</summary>
    public const string Trickplay = "trickplay";

    /// <summary>Every chapter has an image. Only counted in libraries with chapter image extraction, for items that have chapters.</summary>
    public const string ChapterImages = "chapterImages";

    /// <summary>Order in which the checks are listed.</summary>
    public static readonly string[] All = { Refreshed, MediaInfo, PrimaryImage, Overview, ProviderIds, Trickplay, ChapterImages };
}

public class StatusResponse
{
    public DateTime GeneratedAtUtc { get; set; }

    public string? ServerVersion { get; set; }

    public int RefreshSeconds { get; set; }

    /// <summary>Gets or sets the UI refresh interval while something is being indexed.</summary>
    public int ActiveRefreshSeconds { get; set; }

    /// <summary>Gets or sets a value indicating whether an indexing task or a library scan is running.</summary>
    public bool Busy { get; set; }

    /// <summary>Gets or sets when the item statistics were computed (null while the first count runs).</summary>
    public DateTime? StatsComputedAtUtc { get; set; }

    /// <summary>Gets or sets how long the last count took, in milliseconds.</summary>
    public long? StatsDurationMs { get; set; }

    /// <summary>Gets or sets a value indicating whether a count is running right now.</summary>
    public bool StatsComputing { get; set; }

    public string? StatsError { get; set; }

    /// <summary>Gets or sets a value indicating whether trickplay data could be read in this Jellyfin version.</summary>
    public bool TrickplayAvailable { get; set; }

    /// <summary>Gets or sets a value indicating whether chapter data could be read in this Jellyfin version.</summary>
    public bool ChaptersAvailable { get; set; }

    public List<TaskStatusInfo> Tasks { get; set; } = new();

    /// <summary>Gets or sets what is being processed right now (current items of the running jobs).</summary>
    public ActivityInfo Activity { get; set; } = new();

    public List<LibraryStatus> Libraries { get; set; } = new();
}

public class TaskStatusInfo
{
    public string? Id { get; set; }

    public string? Key { get; set; }

    public string? Name { get; set; }

    public string? Category { get; set; }

    /// <summary>Gets or sets Idle, Running or Cancelling.</summary>
    public string? State { get; set; }

    /// <summary>Gets or sets the progress in percent while running.</summary>
    public double? Progress { get; set; }

    /// <summary>Gets or sets the result of the last run: Completed, Failed, Cancelled or Aborted.</summary>
    public string? LastStatus { get; set; }

    public DateTime? LastStartUtc { get; set; }

    public DateTime? LastEndUtc { get; set; }

    public string? LastError { get; set; }

    /// <summary>Gets or sets a value indicating whether the task is one of the indexing related tasks.</summary>
    public bool IsIndexing { get; set; }
}

public class LibraryStatus
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? CollectionType { get; set; }

    public List<string> Locations { get; set; } = new();

    /// <summary>Gets or sets the library refresh state reported by Jellyfin (Idle, Active, Queued…).</summary>
    public string? RefreshStatus { get; set; }

    /// <summary>Gets or sets the progress of a running library scan in percent.</summary>
    public double? RefreshProgress { get; set; }

    public bool TrickplayEnabled { get; set; }

    public bool ChapterImagesEnabled { get; set; }

    /// <summary>Gets or sets the number of counted items (videos, tracks, series, albums).</summary>
    public int Items { get; set; }

    /// <summary>Gets or sets the item counts by type (Movie, Episode, Series, Audio…).</summary>
    public Dictionary<string, int> ItemTypes { get; set; } = new();

    /// <summary>Gets or sets the number of items added in the last 24 hours.</summary>
    public int AddedLast24h { get; set; }

    public DateTime? LastAddedUtc { get; set; }

    public List<CheckStatus> Checks { get; set; } = new();
}

public class CheckStatus
{
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the number of items this check applies to.</summary>
    public int Applicable { get; set; }

    /// <summary>Gets or sets the number of items that pass the check.</summary>
    public int Done { get; set; }

    /// <summary>Gets or sets a value indicating whether the check is turned off (library option or plugin setting).</summary>
    public bool Disabled { get; set; }
}

public class MissingResponse
{
    public string? LibraryId { get; set; }

    public string? Check { get; set; }

    /// <summary>Gets or sets the total number of items missing this check.</summary>
    public int Total { get; set; }

    /// <summary>Gets or sets a value indicating whether the list was cut at the configured limit.</summary>
    public bool Truncated { get; set; }

    public List<MissingItem> Items { get; set; } = new();
}

public class MissingItem
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? Type { get; set; }

    public string? SeriesName { get; set; }

    public int? SeasonNumber { get; set; }

    public int? EpisodeNumber { get; set; }

    public int? ProductionYear { get; set; }

    public string? Path { get; set; }

    public DateTime? DateCreatedUtc { get; set; }
}
