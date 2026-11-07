using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.IndexJelly.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.IndexJelly;

/// <summary>
/// IndexJelly plugin: shows how far the server is with indexing the media libraries.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "IndexJelly";

    public override Guid Id => Guid.Parse("8485ee41-9157-4fba-887d-25c21ccee84c");

    public override string Description => "Shows the indexing state of every library: running scans, media info, metadata, images, trickplay and chapter images, with the items still missing them.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "indexjelly",
                DisplayName = "IndexJelly",
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Web.indexjelly.html", GetType().Namespace),
                EnableInMainMenu = true,
                MenuSection = "server",
                MenuIcon = "fact_check"
            }
        };
    }
}
