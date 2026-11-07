using System;
using Jellyfin.Plugin.IndexJelly.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.IndexJelly.Services;

internal static class ItemRefs
{
    /// <summary>Builds a small, serialisable reference to an item (the item itself is not kept).</summary>
    public static ItemRef From(BaseItem item)
    {
        var r = new ItemRef
        {
            Id = item.Id.ToString("N"),
            Name = item.Name,
            Type = item.GetType().Name,
            ProductionYear = item.ProductionYear,
            IsFolder = item.IsFolder,
            Path = item.Path
        };

        Guid? fallbackImage = null;
        switch (item)
        {
            case Episode e:
                r.SeriesName = e.SeriesName;
                r.SeasonNumber = e.ParentIndexNumber;
                r.EpisodeNumber = e.IndexNumber;
                fallbackImage = e.SeriesId;
                break;
            case Season s:
                r.SeriesName = s.SeriesName;
                r.SeasonNumber = s.IndexNumber;
                fallbackImage = s.SeriesId;
                break;
            default:
                // tracks use the album cover, other items their parent's image
                fallbackImage = item.ParentId;
                break;
        }

        bool own;
        try
        {
            own = item.HasImage(ImageType.Primary, 0);
        }
        catch
        {
            own = false;
        }

        var image = own ? item.Id : fallbackImage;
        r.ImageItemId = image.HasValue && image.Value != Guid.Empty ? image.Value.ToString("N") : null;
        return r;
    }

    /// <summary>Copy without the file path (when paths are turned off in the settings).</summary>
    public static ItemRef WithoutPath(ItemRef r)
    {
        return new ItemRef
        {
            Id = r.Id,
            Name = r.Name,
            Type = r.Type,
            SeriesName = r.SeriesName,
            SeasonNumber = r.SeasonNumber,
            EpisodeNumber = r.EpisodeNumber,
            ProductionYear = r.ProductionYear,
            ImageItemId = r.ImageItemId,
            IsFolder = r.IsFolder
        };
    }
}
