using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.IndexJelly.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets the UI auto-refresh interval in seconds (scan and task progress).</summary>
    public int RefreshSeconds { get; set; } = 5;

    /// <summary>Gets or sets the UI refresh interval in seconds while something is being indexed.</summary>
    public int ActiveRefreshSeconds { get; set; } = 2;

    /// <summary>Gets or sets how long the per-library item statistics are cached, in minutes.</summary>
    public int StatsCacheMinutes { get; set; } = 5;

    /// <summary>Gets or sets a value indicating whether file system paths are returned by the API.</summary>
    public bool ShowPaths { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether trickplay images are checked (one database lookup per video).</summary>
    public bool CheckTrickplay { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether chapter images are checked (one database lookup per video).</summary>
    public bool CheckChapterImages { get; set; } = true;

    /// <summary>Gets or sets the maximum number of missing items kept per library and check.</summary>
    public int MissingListLimit { get; set; } = 500;
}
