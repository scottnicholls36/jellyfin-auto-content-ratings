using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ContentRatings.Configuration;
using Jellyfin.Plugin.ContentRatings.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.ContentRatings.Tasks;

/// <summary>
/// Re-checks the rating of every movie and series in the selected libraries.
/// </summary>
public class UpdateAllRatingsTask : IScheduledTask
{
    private readonly ContentRatingUpdater _updater;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateAllRatingsTask"/> class.
    /// </summary>
    /// <param name="updater">Rating updater.</param>
    public UpdateAllRatingsTask(ContentRatingUpdater updater)
    {
        _updater = updater;
    }

    /// <inheritdoc />
    public string Name => "Update content ratings (whole library)";

    /// <inheritdoc />
    public string Key => "ContentRatingsFullLibrary";

    /// <inheritdoc />
    public string Description => "Re-checks the content rating of every movie and series in the selected libraries.";

    /// <inheritdoc />
    public string Category => "Auto Content Ratings";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _updater.RunAsync(fullScan: true, progress, cancellationToken);

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => TaskScheduleSync.FullScanTriggers(Plugin.Instance?.Configuration ?? new PluginConfiguration());
}
