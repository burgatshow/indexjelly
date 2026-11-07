using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.IndexJelly.Api;

/// <summary>What the server is working on right now, and what it processed last.</summary>
public class ActivityInfo
{
    /// <summary>Gets or sets a value indicating whether running ffmpeg / ffprobe processes can be read (Linux only).</summary>
    public bool ProcessesAvailable { get; set; }

    /// <summary>Gets or sets a value indicating whether refresh events are received.</summary>
    public bool EventsAvailable { get; set; }

    /// <summary>Gets or sets the indexing ffmpeg / ffprobe processes (playback transcodes are left out).</summary>
    public List<ProcessActivity> Processes { get; set; } = new();

    /// <summary>Gets or sets the items whose metadata refresh is running right now.</summary>
    public List<RefreshActivity> Refreshing { get; set; } = new();

    /// <summary>Gets or sets the last processed items, newest first.</summary>
    public List<RecentActivity> Recent { get; set; } = new();
}

/// <summary>A reference to a library item, enough to show it and link to it.</summary>
public class ItemRef
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    /// <summary>Gets or sets the item type (Movie, Episode, Series, Season, Audio, MusicAlbum, CollectionFolder…).</summary>
    public string? Type { get; set; }

    public string? SeriesName { get; set; }

    public int? SeasonNumber { get; set; }

    public int? EpisodeNumber { get; set; }

    public int? ProductionYear { get; set; }

    /// <summary>Gets or sets the item whose primary image represents this one (the series for an episode without a picture).</summary>
    public string? ImageItemId { get; set; }

    public bool IsFolder { get; set; }

    public string? Path { get; set; }
}

public class ProcessActivity
{
    public int Pid { get; set; }

    /// <summary>Gets or sets what the process does: probe, keyframes, trickplay, imageExtraction, audioNormalization, segments, ffmpeg.</summary>
    public string? Kind { get; set; }

    public DateTime? StartedUtc { get; set; }

    /// <summary>Gets or sets the processed item, when its file could be matched to the library.</summary>
    public ItemRef? Item { get; set; }

    /// <summary>Gets or sets the file name of the input, when no item matched.</summary>
    public string? FileName { get; set; }
}

public class RefreshActivity
{
    public ItemRef Item { get; set; } = new();

    public DateTime StartedUtc { get; set; }

    /// <summary>Gets or sets the progress in percent; Jellyfin only reports it for folders (libraries, series…).</summary>
    public double? Progress { get; set; }
}

public class RecentActivity
{
    /// <summary>Gets or sets Added, Updated, Removed or Refreshed.</summary>
    public string Action { get; set; } = string.Empty;

    public DateTime AtUtc { get; set; }

    public ItemRef Item { get; set; } = new();
}
