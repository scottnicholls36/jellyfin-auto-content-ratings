using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.ContentRatings.Configuration;
using Jellyfin.Plugin.ContentRatings.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ContentRatings;

/// <summary>
/// Auto Content Ratings plugin entry point.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private readonly ITaskManager _taskManager;
    private readonly ILogger<Plugin> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Application paths.</param>
    /// <param name="xmlSerializer">XML serialiser.</param>
    /// <param name="taskManager">Scheduled task manager.</param>
    /// <param name="logger">Logger.</param>
    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        ITaskManager taskManager,
        ILogger<Plugin> logger)
        : base(applicationPaths, xmlSerializer)
    {
        _taskManager = taskManager;
        _logger = logger;
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Auto Content Ratings";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("44c838b7-b4cc-4cdf-bc1d-3f8fe3ce3748");

    /// <inheritdoc />
    public override string Description => "Automatically sets content ratings from TMDB or TVDB.";

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        ];
    }

    /// <inheritdoc />
    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        base.UpdateConfiguration(configuration);

        // Push the chosen frequencies into Jellyfin's scheduler so they show under Dashboard > Scheduled Tasks.
        TaskScheduleSync.Apply(_taskManager, Configuration, _logger);
    }
}
