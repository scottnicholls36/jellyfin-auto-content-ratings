using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.ContentRatings.Configuration;
using Jellyfin.Plugin.ContentRatings.Sources;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ContentRatings.Services;

/// <summary>
/// Outcome counts for a run.
/// </summary>
/// <param name="Checked">Items looked at.</param>
/// <param name="Updated">Items whose rating was changed.</param>
/// <param name="Unchanged">Items that already had the right rating.</param>
/// <param name="NotFound">Items the source had no rating for.</param>
/// <param name="Skipped">Items skipped because they are locked or already rated with overwrite off.</param>
/// <param name="Failed">Items that errored.</param>
public sealed record UpdateSummary(int Checked, int Updated, int Unchanged, int NotFound, int Skipped, int Failed);

/// <summary>
/// Finds movies and series in the chosen libraries and writes their content rating.
/// </summary>
public class ContentRatingUpdater
{
    private static readonly BaseItemKind[] _topLevelKinds = [BaseItemKind.Movie, BaseItemKind.Series];
    private static readonly BaseItemKind[] _childKinds = [BaseItemKind.Season, BaseItemKind.Episode];

    private readonly ILibraryManager _libraryManager;
    private readonly IReadOnlyList<IRatingSource> _sources;
    private readonly RunStateStore _runState;
    private readonly ILogger<ContentRatingUpdater> _logger;
    private readonly Func<PluginConfiguration> _getConfig;

    // Scheduled runs, post-scan runs and manual runs must not overlap.
    private readonly SemaphoreSlim _runLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentRatingUpdater"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="sources">Available rating sources.</param>
    /// <param name="runState">Run state store.</param>
    /// <param name="logger">Logger.</param>
    public ContentRatingUpdater(
        ILibraryManager libraryManager,
        IEnumerable<IRatingSource> sources,
        RunStateStore runState,
        ILogger<ContentRatingUpdater> logger)
        : this(libraryManager, sources, runState, logger, () => Plugin.Instance?.Configuration ?? new PluginConfiguration())
    {
    }

    internal ContentRatingUpdater(
        ILibraryManager libraryManager,
        IEnumerable<IRatingSource> sources,
        RunStateStore runState,
        ILogger<ContentRatingUpdater> logger,
        Func<PluginConfiguration> getConfig)
    {
        _libraryManager = libraryManager;
        _sources = sources.ToList();
        _runState = runState;
        _logger = logger;
        _getConfig = getConfig;
    }

    /// <summary>
    /// Runs an update.
    /// </summary>
    /// <param name="fullScan"><c>true</c> to check every item; <c>false</c> for items added since the last run.</param>
    /// <param name="progress">Progress, 0 to 100.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Outcome counts.</returns>
    public async Task<UpdateSummary> RunAsync(bool fullScan, IProgress<double> progress, CancellationToken cancellationToken)
    {
        await _runLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RunCoreAsync(fullScan, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _runLock.Release();
        }
    }

    private async Task<UpdateSummary> RunCoreAsync(bool fullScan, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = _getConfig();
        var startedUtc = DateTime.UtcNow;

        var movieSources = OrderedSources(config, config.Source);
        var seriesSources = OrderedSources(config, config.GetSeriesSource());
        if (movieSources.Count == 0 && seriesSources.Count == 0)
        {
            _logger.LogWarning("No API key is set for the chosen sources; nothing to do. Add one on the plugin settings page");
            return new UpdateSummary(0, 0, 0, 0, 0, 0);
        }

        if (movieSources.Count == 0)
        {
            _logger.LogWarning("No API key is set for {Source}, so movies will be skipped", config.Source);
        }

        if (seriesSources.Count == 0)
        {
            _logger.LogWarning("No API key is set for {Source}, so TV series will be skipped", config.GetSeriesSource());
        }

        var libraryIds = config.LibraryIds
            .Select(id => Guid.TryParse(id, out var guid) ? guid : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToList();

        if (libraryIds.Count == 0)
        {
            _logger.LogWarning("No libraries are selected; nothing to do. Choose them on the plugin settings page");
            return new UpdateSummary(0, 0, 0, 0, 0, 0);
        }

        // With no previous run, the first new-media check covers everything.
        var since = fullScan ? null : _runState.GetLastRunUtc();
        var items = CollectItems(libraryIds, since, retryUnrated: config.RetryUnrated);

        _logger.LogInformation(
            "Checking content ratings for {Count} items ({Mode}); movies from {MovieSources}, TV series from {SeriesSources}",
            items.Count,
            since is null ? "all items" : $"added since {since:u}",
            Describe(movieSources),
            Describe(seriesSources));

        var countries = new[] { config.CountryCode, config.FallbackCountryCode }
            .Select(c => c?.Trim().ToUpperInvariant())
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct()
            .Cast<string>()
            .ToList();

        int updated = 0, unchanged = 0, notFound = 0, skipped = 0, failed = 0;

        for (var i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = items[i];

            try
            {
                var sources = item is Series ? seriesSources : movieSources;
                switch (await ProcessItemAsync(item, sources, countries, config, cancellationToken).ConfigureAwait(false))
                {
                    case ItemOutcome.Updated: updated++; break;
                    case ItemOutcome.Unchanged: unchanged++; break;
                    case ItemOutcome.NotFound: notFound++; break;
                    case ItemOutcome.Skipped: skipped++; break;
                }
            }
            catch (Exception ex) when (ex is not RatingSourceAuthException && !cancellationToken.IsCancellationRequested)
            {
                failed++;
                _logger.LogWarning(ex, "Could not get a content rating for {Name} ({Id})", item.Name, item.Id);
            }

            progress.Report(100.0 * (i + 1) / items.Count);
        }

        // Only advance the marker when nothing failed, so failed items are retried next time.
        if (failed == 0)
        {
            _runState.SetLastRunUtc(startedUtc);
        }

        var summary = new UpdateSummary(items.Count, updated, unchanged, notFound, skipped, failed);
        _logger.LogInformation(
            "Content ratings finished: {Updated} updated, {Unchanged} already correct, {NotFound} not found, {Skipped} skipped, {Failed} failed",
            summary.Updated,
            summary.Unchanged,
            summary.NotFound,
            summary.Skipped,
            summary.Failed);

        return summary;
    }

    private List<IRatingSource> OrderedSources(PluginConfiguration config, string chosen)
    {
        var primary = _sources.FirstOrDefault(s => string.Equals(s.Name, chosen, StringComparison.OrdinalIgnoreCase))
            ?? _sources.First(s => s.Name == RatingSourceNames.Tmdb);

        var result = new List<IRatingSource>();
        if (primary.IsConfigured(config))
        {
            result.Add(primary);
        }

        if (config.FallbackToOtherSource)
        {
            result.AddRange(_sources.Where(s => s != primary && s.IsConfigured(config)));
        }

        return result;
    }

    private static string Describe(IReadOnlyList<IRatingSource> sources)
        => sources.Count == 0 ? "(none: no API key)" : string.Join(" then ", sources.Select(s => s.Name));

    private List<BaseItem> CollectItems(IReadOnlyList<Guid> libraryIds, DateTime? since, bool retryUnrated)
    {
        var items = new Dictionary<Guid, BaseItem>();

        foreach (var libraryId in libraryIds)
        {
            if (_libraryManager.GetItemById(libraryId) is null)
            {
                _logger.LogWarning("Library {Id} no longer exists; skipping it", libraryId);
                continue;
            }

            // The server narrows a recursive ParentId query to everything inside that library.
            foreach (var item in Query(libraryId, since, hasRating: null))
            {
                items.TryAdd(item.Id, item);
            }

            if (since is not null && retryUnrated)
            {
                foreach (var item in Query(libraryId, since: null, hasRating: false))
                {
                    items.TryAdd(item.Id, item);
                }
            }
        }

        return items.Values.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private IReadOnlyList<BaseItem> Query(Guid libraryId, DateTime? since, bool? hasRating)
        => _libraryManager.GetItemList(new InternalItemsQuery
        {
            ParentId = libraryId,
            Recursive = true,
            IncludeItemTypes = _topLevelKinds,
            IsVirtualItem = false,
            MinDateCreated = since,
            HasOfficialRating = hasRating
        });

    private async Task<ItemOutcome> ProcessItemAsync(
        BaseItem item,
        IReadOnlyList<IRatingSource> sources,
        IReadOnlyList<string> countries,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        if (sources.Count == 0
            || IsRatingLocked(item)
            || (!config.OverwriteExisting && !string.IsNullOrEmpty(item.OfficialRating)))
        {
            return ItemOutcome.Skipped;
        }

        SourceRating? found = null;
        foreach (var source in sources)
        {
            found = await source.GetRatingAsync(item, countries, config, cancellationToken).ConfigureAwait(false);
            if (found is not null)
            {
                break;
            }
        }

        if (found is null)
        {
            _logger.LogDebug("No {Countries} rating found for {Name}", string.Join("/", countries), item.Name);
            return ItemOutcome.NotFound;
        }

        var rating = RatingSelector.Format(found, config.IncludeCountryPrefix);
        var changed = !string.Equals(item.OfficialRating, rating, StringComparison.OrdinalIgnoreCase);

        if (changed)
        {
            _logger.LogInformation("{Name}: {Old} -> {New}", item.Name, string.IsNullOrEmpty(item.OfficialRating) ? "(none)" : item.OfficialRating, rating);
            await SaveRatingAsync(item, rating, config.LockAfterUpdate, cancellationToken).ConfigureAwait(false);
        }

        if (item is Series && config.ApplyToSeasonsAndEpisodes)
        {
            await ApplyToChildrenAsync(item, rating, config, cancellationToken).ConfigureAwait(false);
        }

        return changed ? ItemOutcome.Updated : ItemOutcome.Unchanged;
    }

    private async Task ApplyToChildrenAsync(BaseItem series, string rating, PluginConfiguration config, CancellationToken cancellationToken)
    {
        var children = _libraryManager.GetItemList(new InternalItemsQuery
        {
            AncestorIds = [series.Id],
            Recursive = true,
            IncludeItemTypes = _childKinds
        });

        foreach (var child in children)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsRatingLocked(child)
                || string.Equals(child.OfficialRating, rating, StringComparison.OrdinalIgnoreCase)
                || (!config.OverwriteExisting && !string.IsNullOrEmpty(child.OfficialRating)))
            {
                continue;
            }

            await SaveRatingAsync(child, rating, config.LockAfterUpdate, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsRatingLocked(BaseItem item)
        => item.IsLocked || item.LockedFields.Contains(MetadataField.OfficialRating);

    /// <summary>
    /// Writes the rating the same way Jellyfin's metadata editor does, including recalculating the
    /// parental rating score that parental controls filter on.
    /// </summary>
    internal virtual async Task SaveRatingAsync(BaseItem item, string rating, bool lockField, CancellationToken cancellationToken)
    {
        item.OfficialRating = rating;

        if (lockField && !item.LockedFields.Contains(MetadataField.OfficialRating))
        {
            item.LockedFields = [.. item.LockedFields, MetadataField.OfficialRating];
        }

        item.OnMetadataChanged();
        await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
    }

    private enum ItemOutcome
    {
        Updated,
        Unchanged,
        NotFound,
        Skipped
    }
}
