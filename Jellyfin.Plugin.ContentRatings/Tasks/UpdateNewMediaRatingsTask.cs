using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ContentRatings.Configuration;
using Jellyfin.Plugin.ContentRatings.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.ContentRatings.Tasks;

/// <summary>
/// Sets ratings on media added since the last run.
/// </summary>
public class UpdateNewMediaRatingsTask : IScheduledTask
{
    private readonly ContentRatingUpdater _updater;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateNewMediaRatingsTask"/> class.
    /// </summary>
    /// <param name="updater">Rating updater.</param>
    public UpdateNewMediaRatingsTask(ContentRatingUpdater updater)
    {
        _updater = updater;
    }

    /// <inheritdoc />
    public string Name => "Update content ratings (new media)";

    /// <inheritdoc />
    public string Key => "ContentRatingsNewMedia";

    /// <inheritdoc />
    public string Description => "Looks up content ratings for movies and series added since the last run.";

    /// <inheritdoc />
    public string Category => "Auto Content Ratings";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _updater.RunAsync(fullScan: false, progress, cancellationToken);

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => TaskScheduleSync.NewMediaTriggers(Plugin.Instance?.Configuration ?? new PluginConfiguration());
}
