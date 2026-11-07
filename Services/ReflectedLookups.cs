using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IndexJelly.Services;

/// <summary>
/// Trickplay and chapter data have no stable public API across Jellyfin versions: the services were
/// renamed and moved several times (IItemRepository → IChapterRepository → IChapterManager, …).
/// They are looked up by name at runtime, so a change in a future version switches the check off
/// instead of breaking the plugin.
/// </summary>
internal sealed class ReflectedLookups
{
    private static readonly string[] _chapterServiceNames = { "IChapterManager", "IChapterRepository", "IItemRepository" };
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> _imagePathProps = new();
    private static readonly PropertyInfo? _heightProp = typeof(BaseItem).GetProperty("Height", BindingFlags.Instance | BindingFlags.Public);

    private object? _trickplayService;
    private MethodInfo? _trickplayMethod;
    private object? _chapterService;
    private MethodInfo? _chapterMethod;
    private bool _chapterTakesItem;

    public bool TrickplayAvailable => _trickplayMethod is not null;

    public bool ChaptersAvailable => _chapterMethod is not null;

    public static ReflectedLookups Create(IServiceProvider services, ILogger logger)
    {
        var r = new ReflectedLookups();
        var interfaces = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && a.GetName().Name is { } n
                        && (n.StartsWith("MediaBrowser", StringComparison.Ordinal) || n.StartsWith("Jellyfin", StringComparison.Ordinal) || n.StartsWith("Emby", StringComparison.Ordinal)))
            .SelectMany(SafeTypes)
            .Where(t => t.IsInterface)
            .ToList();

        try
        {
            var tType = interfaces.FirstOrDefault(t => t.Name == "ITrickplayManager");
            var tMethod = tType?.GetMethods().FirstOrDefault(m => m.Name == "GetTrickplayResolutions"
                                                                  && m.GetParameters() is { Length: 1 } p && p[0].ParameterType == typeof(Guid));
            var tService = tType is null ? null : services.GetService(tType);
            if (tMethod is not null && tService is not null)
            {
                r._trickplayService = tService;
                r._trickplayMethod = tMethod;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "IndexJelly: trickplay lookup unavailable");
        }

        foreach (var name in _chapterServiceNames)
        {
            try
            {
                var cType = interfaces.FirstOrDefault(t => t.Name == name);
                if (cType is null)
                {
                    continue;
                }

                var cMethod = cType.GetMethods().FirstOrDefault(m => m.Name == "GetChapters"
                                                                     && m.GetParameters() is { Length: 1 } p
                                                                     && (p[0].ParameterType == typeof(Guid) || p[0].ParameterType.IsAssignableFrom(typeof(BaseItem))));
                var cService = cMethod is null ? null : services.GetService(cType);
                if (cMethod is not null && cService is not null)
                {
                    r._chapterService = cService;
                    r._chapterMethod = cMethod;
                    r._chapterTakesItem = cMethod.GetParameters()[0].ParameterType != typeof(Guid);
                    break;
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "IndexJelly: chapter lookup via {Name} unavailable", name);
            }
        }

        logger.LogDebug("IndexJelly: trickplay lookup {Trickplay}, chapter lookup {Chapters}", r.TrickplayAvailable, r.ChaptersAvailable);
        return r;
    }

    /// <summary>Number of trickplay resolutions stored for the item (0 = none), or null when it cannot be read.</summary>
    public async Task<int?> GetTrickplayCountAsync(Guid itemId)
    {
        if (_trickplayMethod is null)
        {
            return null;
        }

        var result = _trickplayMethod.Invoke(_trickplayService, new object[] { itemId });
        if (result is Task task)
        {
            await task.ConfigureAwait(false);
            result = task.GetType().GetProperty("Result")?.GetValue(task);
        }

        return result is ICollection c ? c.Count : result is IEnumerable e ? e.Cast<object>().Count() : 0;
    }

    /// <summary>Chapter count and how many of them have an image, or null when chapters cannot be read.</summary>
    public (int Chapters, int WithImage)? GetChapterImages(BaseItem item)
    {
        if (_chapterMethod is null)
        {
            return null;
        }

        var result = _chapterMethod.Invoke(_chapterService, new object[] { _chapterTakesItem ? item : item.Id });
        if (result is not IEnumerable list)
        {
            return (0, 0);
        }

        int chapters = 0, withImage = 0;
        foreach (var ch in list)
        {
            if (ch is null)
            {
                continue;
            }

            chapters++;
            var prop = _imagePathProps.GetOrAdd(ch.GetType(), t => t.GetProperty("ImagePath"));
            if (prop?.GetValue(ch) is string s && !string.IsNullOrEmpty(s))
            {
                withImage++;
            }
        }

        return (chapters, withImage);
    }

    /// <summary>Video height written by ffprobe; null if the property does not exist in this version.</summary>
    public static int? GetHeight(BaseItem item)
    {
        try
        {
            return _heightProp?.GetValue(item) is int h ? h : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Reads a boolean property (e.g. a LibraryOptions switch) by name; false if missing.</summary>
    public static bool GetBool(object? obj, string name)
    {
        try
        {
            return obj?.GetType().GetProperty(name)?.GetValue(obj) is true;
        }
        catch
        {
            return false;
        }
    }

    private static Type[] SafeTypes(Assembly a)
    {
        try
        {
            return a.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>().ToArray();
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }
}
