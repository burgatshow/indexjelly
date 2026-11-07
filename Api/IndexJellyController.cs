using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.IndexJelly.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PluginConfiguration = Jellyfin.Plugin.IndexJelly.Configuration.PluginConfiguration;

namespace Jellyfin.Plugin.IndexJelly.Api;

/// <summary>
/// Returns the indexing state of the libraries: running scans and tasks, and per library how many items
/// already have media info, metadata, images, trickplay and chapter images.
/// </summary>
[ApiController]
[Route("IndexJelly")]
[Authorize(Policy = Policies.RequiresElevation)]
[Produces(MediaTypeNames.Application.Json)]
public class IndexJellyController : ControllerBase
{
    /// <summary>Scheduled task keys that take part in indexing.</summary>
    private static readonly HashSet<string> _indexingTaskKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "RefreshLibrary",
        "RefreshTrickplayImages",
        "RefreshChapterImages",
        "ExtractKeyframes",
        "KeyframeExtraction",
        "RefreshPeople",
        "MediaSegmentExtract",
        "AudioNormalization",
        "RefreshMediaSegments"
    };

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSourceManager _mediaSourceManager;
    private readonly ITaskManager _taskManager;
    private readonly IServerApplicationHost _appHost;
    private readonly IServiceProvider _services;
    private readonly ActivityTracker _activity;
    private readonly ILogger<IndexJellyController> _logger;

    public IndexJellyController(
        ILibraryManager libraryManager,
        IMediaSourceManager mediaSourceManager,
        ITaskManager taskManager,
        IServerApplicationHost appHost,
        IServiceProvider services,
        ActivityTracker activity,
        ILogger<IndexJellyController> logger)
    {
        _libraryManager = libraryManager;
        _mediaSourceManager = mediaSourceManager;
        _taskManager = taskManager;
        _appHost = appHost;
        _services = services;
        _activity = activity;
        _logger = logger;
    }

    /// <summary>
    /// Gets the indexing state. Scan and task progress is live; item counts come from the last background
    /// count (see <c>statsComputedAtUtc</c>), which is restarted when it is older than the configured age.
    /// </summary>
    /// <param name="recount">Start a new count now, regardless of the age of the cached one.</param>
    /// <returns>The status payload.</returns>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetStatus([FromQuery] bool recount = false)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var tasks = GetTasks();
        var folders = _libraryManager.GetVirtualFolders(true);

        // While indexing runs the counts change, so they are recounted more often; and once an indexing
        // task has finished, the next request recounts right away instead of waiting for the cache to expire.
        var busy = tasks.Any(t => t.IsIndexing && t.State != nameof(TaskState.Idle))
                   || folders.Any(f => !string.IsNullOrEmpty(f.RefreshStatus) && f.RefreshStatus != "Idle");
        var lastFinish = tasks.Where(t => t.IsIndexing && t.LastEndUtc.HasValue).Select(t => t.LastEndUtc!.Value).DefaultIfEmpty().Max();
        IndexStats.StartIfStale(_libraryManager, _mediaSourceManager, _services, _logger, config, recount, busy, lastFinish == default ? null : lastFinish);
        var snapshot = IndexStats.Current;

        var response = new StatusResponse
        {
            GeneratedAtUtc = DateTime.UtcNow,
            ServerVersion = _appHost.ApplicationVersionString,
            RefreshSeconds = Math.Clamp(config.RefreshSeconds, 2, 120),
            ActiveRefreshSeconds = Math.Clamp(config.ActiveRefreshSeconds, 1, 30),
            Busy = busy,
            StatsComputedAtUtc = snapshot?.ComputedAtUtc,
            StatsDurationMs = snapshot?.DurationMs,
            StatsComputing = IndexStats.Computing,
            StatsError = IndexStats.LastError,
            TrickplayAvailable = snapshot?.TrickplayAvailable ?? false,
            ChaptersAvailable = snapshot?.ChaptersAvailable ?? false,
            Tasks = tasks
        };

        try
        {
            _activity.Fill(response.Activity, config.ShowPaths);
            response.Activity.ProcessesAvailable = ProcessScanner.Available;
            response.Activity.Processes = ProcessScanner.Scan(_libraryManager, config.ShowPaths, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IndexJelly: could not read the current activity");
        }

        foreach (var folder in folders)
        {
            var lib = new LibraryStatus
            {
                Id = Guid.TryParse(folder.ItemId, out var g) ? g.ToString("N") : folder.ItemId,
                Name = folder.Name,
                CollectionType = folder.CollectionType?.ToString(),
                Locations = config.ShowPaths ? (folder.Locations ?? Array.Empty<string>()).ToList() : new List<string>(),
                RefreshStatus = folder.RefreshStatus,
                RefreshProgress = folder.RefreshProgress,
                TrickplayEnabled = ReflectedLookups.GetBool(folder.LibraryOptions, "EnableTrickplayImageExtraction"),
                ChapterImagesEnabled = ReflectedLookups.GetBool(folder.LibraryOptions, "EnableChapterImageExtraction")
            };

            if (lib.Id is not null && snapshot is not null && snapshot.Libraries.TryGetValue(lib.Id, out var counts))
            {
                lib.Items = counts.Items;
                lib.ItemTypes = counts.ItemTypes;
                lib.AddedLast24h = counts.AddedLast24h;
                lib.LastAddedUtc = counts.LastAddedUtc;
                lib.Checks = CheckKeys.All.Where(counts.Checks.ContainsKey).Select(k => counts.Checks[k]).ToList();
            }

            response.Libraries.Add(lib);
        }

        return new JsonResult(response, _jsonOptions);
    }

    /// <summary>Gets the items of a library that do not pass a check yet (from the last count).</summary>
    /// <param name="libraryId">The library id.</param>
    /// <param name="check">The check key, e.g. <c>mediaInfo</c> or <c>trickplay</c>.</param>
    /// <returns>The missing items.</returns>
    [HttpGet("Missing")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult GetMissing([FromQuery] string libraryId, [FromQuery] string check)
    {
        var snapshot = IndexStats.Current;
        var id = Guid.TryParse(libraryId, out var g) ? g.ToString("N") : libraryId;
        if (snapshot is null || !snapshot.Libraries.TryGetValue(id, out var counts)
                             || !counts.Missing.TryGetValue(check, out var list)
                             || !counts.Checks.TryGetValue(check, out var status))
        {
            return NotFound();
        }

        var total = status.Applicable - status.Done;
        var response = new MissingResponse
        {
            LibraryId = id,
            Check = check,
            Total = total,
            Truncated = total > list.Count,
            Items = list
        };
        return new JsonResult(response, _jsonOptions);
    }

    private List<TaskStatusInfo> GetTasks()
    {
        var list = new List<TaskStatusInfo>();
        try
        {
            foreach (var w in _taskManager.ScheduledTasks)
            {
                var key = SafeKey(w);
                var indexing = key is not null && _indexingTaskKeys.Contains(key);
                var running = w.State != TaskState.Idle;
                if (!indexing && !running && !string.Equals(w.Category, "Library", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var last = w.LastExecutionResult;
                list.Add(new TaskStatusInfo
                {
                    Id = w.Id,
                    Key = key,
                    Name = w.Name,
                    Category = w.Category,
                    State = w.State.ToString(),
                    Progress = running ? w.CurrentProgress : null,
                    LastStatus = last?.Status.ToString(),
                    LastStartUtc = last is null || last.StartTimeUtc == default ? null : last.StartTimeUtc,
                    LastEndUtc = last is null || last.EndTimeUtc == default ? null : last.EndTimeUtc,
                    LastError = last?.ErrorMessage,
                    IsIndexing = indexing
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IndexJelly: could not read the scheduled tasks");
        }

        return list
            .OrderBy(t => t.State == nameof(TaskState.Idle) ? 1 : 0)
            .ThenBy(t => t.IsIndexing ? 0 : 1)
            .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string? SafeKey(IScheduledTaskWorker worker)
    {
        try
        {
            return worker.ScheduledTask?.Key;
        }
        catch
        {
            return null;
        }
    }
}
