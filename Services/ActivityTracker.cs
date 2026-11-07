using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.IndexJelly.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IndexJelly.Services;

/// <summary>
/// Listens to Jellyfin's refresh and library events and remembers what is being refreshed right now
/// (with progress for folders) and the last processed items. Jellyfin has no API that tells which item
/// a scheduled task is working on; these events are the closest thing to it.
/// The events are attached by name, so a renamed event only switches this feed off.
/// </summary>
public sealed class ActivityTracker : IHostedService, IDisposable
{
    private const int RecentLimit = 80;
    private static readonly TimeSpan _staleRefresh = TimeSpan.FromMinutes(30);
    private static readonly HashSet<string> _hiddenTypes = new(StringComparer.Ordinal) { "UserRootFolder", "AggregateFolder" };

    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly ILogger<ActivityTracker> _logger;
    private readonly object _sync = new();
    private readonly Dictionary<Guid, RefreshActivity> _refreshing = new();
    private readonly LinkedList<RecentActivity> _recent = new();
    private readonly List<(object Source, EventInfo Event, Delegate Handler)> _subscriptions = new();

    public ActivityTracker(ILibraryManager libraryManager, IProviderManager providerManager, ILogger<ActivityTracker> logger)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _logger = logger;
    }

    /// <summary>Gets a value indicating whether at least one event could be attached.</summary>
    public bool EventsAvailable => _subscriptions.Count > 0;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Subscribe(_providerManager, "RefreshStarted", nameof(OnRefreshStarted));
        Subscribe(_providerManager, "RefreshProgress", nameof(OnRefreshProgress));
        Subscribe(_providerManager, "RefreshCompleted", nameof(OnRefreshCompleted));
        Subscribe(_libraryManager, "ItemAdded", nameof(OnItemAdded));
        Subscribe(_libraryManager, "ItemUpdated", nameof(OnItemUpdated));
        Subscribe(_libraryManager, "ItemRemoved", nameof(OnItemRemoved));
        _logger.LogInformation("IndexJelly: activity tracking attached to {Count} events", _subscriptions.Count);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        foreach (var (source, ev, handler) in _subscriptions)
        {
            try
            {
                ev.RemoveEventHandler(source, handler);
            }
            catch
            {
                // shutting down
            }
        }

        _subscriptions.Clear();
    }

    /// <summary>Copies the current state for the API.</summary>
    public void Fill(ActivityInfo info, bool showPaths)
    {
        info.EventsAvailable = EventsAvailable;
        var now = DateTime.UtcNow;
        lock (_sync)
        {
            // a refresh that failed may never report completion
            foreach (var id in _refreshing.Where(kv => now - kv.Value.StartedUtc > _staleRefresh).Select(kv => kv.Key).ToList())
            {
                _refreshing.Remove(id);
            }

            info.Refreshing = _refreshing.Values
                .OrderBy(r => r.Item.IsFolder ? 0 : 1)
                .ThenBy(r => r.StartedUtc)
                .Select(r => new RefreshActivity { Item = Strip(r.Item, showPaths), StartedUtc = r.StartedUtc, Progress = r.Progress })
                .ToList();
            info.Recent = _recent
                .Select(r => new RecentActivity { Action = r.Action, AtUtc = r.AtUtc, Item = Strip(r.Item, showPaths) })
                .ToList();
        }
    }

    private static ItemRef Strip(ItemRef r, bool showPaths) => showPaths ? r : ItemRefs.WithoutPath(r);

    private void OnRefreshStarted(object? sender, EventArgs e)
    {
        var (item, _) = Unwrap(e);
        if (!Accept(item))
        {
            return;
        }

        var r = ItemRefs.From(item!);
        lock (_sync)
        {
            _refreshing[item!.Id] = new RefreshActivity { Item = r, StartedUtc = DateTime.UtcNow };
        }
    }

    private void OnRefreshProgress(object? sender, EventArgs e)
    {
        var (item, progress) = Unwrap(e);
        if (!Accept(item))
        {
            return;
        }

        lock (_sync)
        {
            if (_refreshing.TryGetValue(item!.Id, out var r))
            {
                r.Progress = progress;
                return;
            }
        }

        // progress of a folder whose start we missed (e.g. the plugin loaded mid-scan)
        var created = new RefreshActivity { Item = ItemRefs.From(item!), StartedUtc = DateTime.UtcNow, Progress = progress };
        lock (_sync)
        {
            _refreshing.TryAdd(item!.Id, created);
        }
    }

    private void OnRefreshCompleted(object? sender, EventArgs e)
    {
        var (item, _) = Unwrap(e);
        if (item is null)
        {
            return;
        }

        lock (_sync)
        {
            _refreshing.Remove(item.Id);
        }

        if (Accept(item))
        {
            AddRecent("Refreshed", item);
        }
    }

    private void OnItemAdded(object? sender, EventArgs e) => OnLibraryEvent("Added", e);

    private void OnItemUpdated(object? sender, EventArgs e) => OnLibraryEvent("Updated", e);

    private void OnItemRemoved(object? sender, EventArgs e) => OnLibraryEvent("Removed", e);

    private void OnLibraryEvent(string action, EventArgs e)
    {
        var (item, _) = Unwrap(e);
        if (Accept(item))
        {
            AddRecent(action, item!);
        }
    }

    /// <summary>Adds an event to the feed; repeated events of one item within a few seconds are merged.</summary>
    private void AddRecent(string action, BaseItem item)
    {
        ItemRef r;
        try
        {
            r = ItemRefs.From(item);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "IndexJelly: could not describe item {Id}", item.Id);
            return;
        }

        var now = DateTime.UtcNow;
        lock (_sync)
        {
            var node = _recent.First;
            for (var i = 0; node is not null && i < 10; i++, node = node.Next)
            {
                if (node.Value.Item.Id == r.Id && now - node.Value.AtUtc < TimeSpan.FromSeconds(15))
                {
                    if (Rank(action) > Rank(node.Value.Action))
                    {
                        node.Value.Action = action;
                    }

                    node.Value.AtUtc = now;
                    node.Value.Item = r;
                    _recent.Remove(node);
                    _recent.AddFirst(node);
                    return;
                }
            }

            _recent.AddFirst(new RecentActivity { Action = action, AtUtc = now, Item = r });
            while (_recent.Count > RecentLimit)
            {
                _recent.RemoveLast();
            }
        }
    }

    private static int Rank(string action) => action switch
    {
        "Added" => 3,
        "Removed" => 3,
        "Refreshed" => 2,
        _ => 1
    };

    private static bool Accept(BaseItem? item)
    {
        return item is not null && !item.IsVirtualItem && !_hiddenTypes.Contains(item.GetType().Name);
    }

    /// <summary>
    /// Finds the item in the event arguments: GenericEventArgs&lt;BaseItem&gt; (Argument),
    /// GenericEventArgs&lt;Tuple&lt;BaseItem, double&gt;&gt; (Argument.Item1/Item2) or ItemChangeEventArgs (Item).
    /// </summary>
    private static (BaseItem? Item, double? Progress) Unwrap(object? arg, int depth = 0)
    {
        if (arg is null || depth > 3)
        {
            return (null, null);
        }

        if (arg is BaseItem b)
        {
            return (b, null);
        }

        var t = arg.GetType();
        var item1 = t.GetProperty("Item1")?.GetValue(arg) ?? t.GetField("Item1")?.GetValue(arg);
        if (item1 is BaseItem bi)
        {
            var item2 = t.GetProperty("Item2")?.GetValue(arg) ?? t.GetField("Item2")?.GetValue(arg);
            return (bi, item2 is double d ? (double?)d : null);
        }

        foreach (var name in new[] { "Argument", "Item" })
        {
            var v = t.GetProperty(name)?.GetValue(arg);
            if (v is not null)
            {
                return Unwrap(v, depth + 1);
            }
        }

        return (null, null);
    }

    private void Subscribe(object source, string eventName, string handlerName)
    {
        try
        {
            var ev = source.GetType().GetEvent(eventName, BindingFlags.Instance | BindingFlags.Public)
                     ?? source.GetType().GetInterfaces().Select(i => i.GetEvent(eventName)).FirstOrDefault(x => x is not null);
            var method = GetType().GetMethod(handlerName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (ev?.EventHandlerType is null || method is null)
            {
                _logger.LogWarning("IndexJelly: event {Event} not found on {Type}", eventName, source.GetType().Name);
                return;
            }

            // (object, EventArgs) binds to any EventHandler<TEventArgs> through parameter contravariance.
            var handler = Delegate.CreateDelegate(ev.EventHandlerType, this, method, false);
            if (handler is null)
            {
                _logger.LogWarning("IndexJelly: event {Event} has an unexpected signature", eventName);
                return;
            }

            ev.AddEventHandler(source, handler);
            _subscriptions.Add((source, ev, handler));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IndexJelly: could not attach to event {Event}", eventName);
        }
    }
}
