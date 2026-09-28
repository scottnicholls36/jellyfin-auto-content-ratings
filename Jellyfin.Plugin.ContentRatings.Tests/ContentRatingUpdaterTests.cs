using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.ContentRatings.Configuration;
using Jellyfin.Plugin.ContentRatings.Services;
using Jellyfin.Plugin.ContentRatings.Sources;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.ContentRatings.Tests;

public class ContentRatingUpdaterTests
{
    private static readonly Guid _libraryId = Guid.NewGuid();

    private readonly Mock<ILibraryManager> _libraryManager = new();
    private readonly FakeSource _tmdb = new(RatingSourceNames.Tmdb);
    private readonly FakeSource _tvdb = new(RatingSourceNames.Tvdb);
    private readonly InMemoryRunState _runState = new();
    private readonly List<InternalItemsQuery> _queries = [];
    private readonly Dictionary<Guid, List<BaseItem>> _children = [];
    private List<BaseItem> _libraryItems = [];
    private List<BaseItem> _libraryChildren = [];

    private readonly PluginConfiguration _config = new()
    {
        TmdbApiKey = "key",
        LibraryIds = [_libraryId.ToString()]
    };

    public ContentRatingUpdaterTests()
    {
        _libraryManager.Setup(m => m.GetItemById(_libraryId)).Returns(new Folder { Id = _libraryId });
        _libraryManager
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns((InternalItemsQuery q) =>
            {
                _queries.Add(q);
                if (q.AncestorIds.Length == 1)
                {
                    return _children.TryGetValue(q.AncestorIds[0], out var kids) ? kids : [];
                }

                IEnumerable<BaseItem> items = q.IncludeItemTypes.Contains(BaseItemKind.Episode) ? _libraryChildren : _libraryItems;
                if (q.HasOfficialRating == false)
                {
                    items = items.Where(i => string.IsNullOrEmpty(i.OfficialRating));
                }

                if (q.MinDateCreated is { } min)
                {
                    items = items.Where(i => i.DateCreated >= min);
                }

                if (q.MinDateLastSaved is { } minSaved)
                {
                    items = items.Where(i => i.DateLastSaved >= minSaved);
                }

                return items.ToList();
            });
    }

    private TestUpdater CreateUpdater()
        => new(_libraryManager.Object, [_tmdb, _tvdb], _runState, () => _config);

    private static Movie NewMovie(string name, string? rating = null, DateTime? created = null)
        => new() { Id = Guid.NewGuid(), Name = name, OfficialRating = rating, DateCreated = created ?? DateTime.UtcNow };

    [Fact]
    public async Task SetsRatingFromPrimarySource()
    {
        var movie = NewMovie("Fight Club", "R");
        _libraryItems = [movie];
        _tmdb.Ratings[movie.Id] = new SourceRating("18", "GB");
        var updater = CreateUpdater();

        var summary = await updater.RunAsync(fullScan: true, new Progress<double>(), CancellationToken.None);

        Assert.Equal(1, summary.Updated);
        Assert.Equal("18", movie.OfficialRating);
        Assert.Single(updater.Saved);
    }

    [Fact]
    public async Task QueriesSelectedLibraryRecursivelyForMoviesAndSeries()
    {
        await CreateUpdater().RunAsync(fullScan: true, new Progress<double>(), CancellationToken.None);

        var query = Assert.Single(_queries);
        Assert.Equal(_libraryId, query.ParentId);
        Assert.True(query.Recursive);
        Assert.Equal([BaseItemKind.Movie, BaseItemKind.Series], query.IncludeItemTypes);
        Assert.False(query.IsVirtualItem);
    }

    [Fact]
    public async Task LeavesCorrectRatingAlone()
    {
        var movie = NewMovie("Paddington", "PG");
        _libraryItems = [movie];
        _tmdb.Ratings[movie.Id] = new SourceRating("PG", "GB");
        var updater = CreateUpdater();

        var summary = await updater.RunAsync(fullScan: true, new Progress<double>(), CancellationToken.None);

        Assert.Equal(1, summary.Unchanged);
        Assert.Empty(updater.Saved);
    }

    [Fact]
    public async Task SkipsLockedItemsAndFields()
    {
        var lockedItem = NewMovie("Locked item", "15");
        lockedItem.IsLocked = true;
        var lockedField = NewMovie("Locked field", "15");
        lockedField.LockedFields = [MetadataField.OfficialRating];
        _libraryItems = [lockedItem, lockedField];
        _tmdb.Ratings[lockedItem.Id] = new SourceRating("18", "GB");
        _tmdb.Ratings[lockedField.Id] = new SourceRating("18", "GB");
        var updater = CreateUpdater();

        var summary = await updater.RunAsync(fullScan: true, new Progress<double>(), CancellationToken.None);

        Assert.Equal(2, summary.Skipped);
        Assert.Empty(updater.Saved);
        Assert.Equal(0, _tmdb.Calls);
    }

    [Fact]
    public async Task DoesNotOverwriteWhenDisabled()
    {
        var rated = NewMovie("Rated", "15");
        var unrated = NewMovie("Unrated");
        _libraryItems = [rated, unrated];
        _tmdb.Ratings[rated.Id] = new SourceRating("18", "GB");
        _tmdb.Ratings[unrated.Id] = new SourceRating("12A", "GB");
        _config.OverwriteExisting = false;
        var updater = CreateUpdater();

        await updater.RunAsync(fullScan: true, new Progress<double>(), CancellationToken.None);

        Assert.Equal("15", rated.OfficialRating);
        Assert.Equal("12A", unrated.OfficialRating);
    }

    [Fact]
    public async Task NewMediaRunOnlyChecksItemsSinceLastRunPlusUnrated()
    {
        var lastRun = DateTime.UtcNow.AddDays(-1);
        _runState.LastRun = lastRun;
        var oldRated = NewMovie("Old rated", "15", lastRun.AddDays(-10));
        var oldUnrated = NewMovie("Old unrated", null, lastRun.AddDays(-10));
        var fresh = NewMovie("New", "PG", lastRun.AddHours(1));
        _libraryItems = [oldRated, oldUnrated, fresh];
        foreach (var item in _libraryItems)
        {
            _tmdb.Ratings[item.Id] = new SourceRating("18", "GB");
        }

        var updater = CreateUpdater();
        var summary = await updater.RunAsync(fullScan: false, new Progress<double>(), CancellationToken.None);

        Assert.Equal(2, summary.Checked);
        Assert.Equal("15", oldRated.OfficialRating);
        Assert.Equal("18", oldUnrated.OfficialRating);
        Assert.Equal("18", fresh.OfficialRating);
        Assert.True(_runState.LastRun > lastRun);
    }

    [Fact]
    public async Task NewMediaRunPicksUpItemsWithOldFileDatesThatWereSavedRecently()
    {
        // Jellyfin's "date added" defaults to the file's creation time, which can predate the last run.
        var lastRun = DateTime.UtcNow.AddDays(-1);
        _runState.LastRun = lastRun;
        var copiedIn = NewMovie("Copied in", "R", lastRun.AddYears(-3));
        copiedIn.DateLastSaved = lastRun.AddHours(2);
        var untouched = NewMovie("Untouched", "R", lastRun.AddYears(-3));
        untouched.DateLastSaved = lastRun.AddDays(-30);
        _libraryItems = [copiedIn, untouched];
        _tmdb.Ratings[copiedIn.Id] = new SourceRating("18", "GB");
        _tmdb.Ratings[untouched.Id] = new SourceRating("18", "GB");

        var summary = await CreateUpdater().RunAsync(fullScan: false, new Progress<double>(), CancellationToken.None);

        Assert.Equal(1, summary.Checked);
        Assert.Equal("18", copiedIn.OfficialRating);
        Assert.Equal("R", untouched.OfficialRating);
    }

    [Fact]
    public async Task NewMediaRunRechecksSeriesThatGainedEpisodes()
    {
        var lastRun = DateTime.UtcNow.AddDays(-1);
        _runState.LastRun = lastRun;
        var series = new Series { Id = Guid.NewGuid(), Name = "Show", OfficialRating = "15", DateCreated = lastRun.AddYears(-1), DateLastSaved = lastRun.AddDays(-5) };
        var oldEpisode = new Episode { Id = Guid.NewGuid(), Name = "Old", SeriesId = series.Id, OfficialRating = "15", DateCreated = lastRun.AddYears(-1), DateLastSaved = lastRun.AddDays(-5) };
        var newEpisode = new Episode { Id = Guid.NewGuid(), Name = "New", SeriesId = series.Id, DateCreated = lastRun.AddYears(-1), DateLastSaved = lastRun.AddHours(1) };
        _libraryItems = [series];
        _libraryChildren = [oldEpisode, newEpisode];
        _children[series.Id] = [oldEpisode, newEpisode];
        _libraryManager.Setup(m => m.GetItemById(series.Id)).Returns(series);
        _tmdb.Ratings[series.Id] = new SourceRating("15", "GB");
        var updater = CreateUpdater();

        var summary = await updater.RunAsync(fullScan: false, new Progress<double>(), CancellationToken.None);

        Assert.Equal(1, summary.Checked);
        Assert.Equal("15", newEpisode.OfficialRating);
        Assert.Equal([newEpisode.Id], updater.Saved.Select(i => i.Id));
    }

    [Fact]
    public async Task NewMediaRunIgnoresNewEpisodesWhenNotCopyingToEpisodes()
    {
        var lastRun = DateTime.UtcNow.AddDays(-1);
        _runState.LastRun = lastRun;
        var series = new Series { Id = Guid.NewGuid(), Name = "Show", OfficialRating = "15", DateCreated = lastRun.AddYears(-1) };
        _libraryItems = [series];
        _libraryChildren = [new Episode { Id = Guid.NewGuid(), Name = "New", SeriesId = series.Id, DateCreated = lastRun.AddHours(1) }];
        _libraryManager.Setup(m => m.GetItemById(series.Id)).Returns(series);
        _config.ApplyToSeasonsAndEpisodes = false;
        _config.RetryUnrated = false;

        var summary = await CreateUpdater().RunAsync(fullScan: false, new Progress<double>(), CancellationToken.None);

        Assert.Equal(0, summary.Checked);
    }

    [Fact]
    public async Task FallsBackToOtherSourceWhenEnabled()
    {
        var movie = NewMovie("Obscure");
        _libraryItems = [movie];
        _tvdb.Ratings[movie.Id] = new SourceRating("12", "GB");
        _config.TvdbApiKey = "tvdb";
        var updater = CreateUpdater();

        await updater.RunAsync(fullScan: true, new Progress<double>(), CancellationToken.None);
        Assert.Null(movie.OfficialRating);

        _config.FallbackToOtherSource = true;
        await updater.RunAsync(fullScan: true, new Progress<double>(), CancellationToken.None);
        Assert.Equal("12", movie.OfficialRating);
    }

    [Fact]
    public async Task UsesTvdbWhenChosen()
    {
        var movie = NewMovie("Film");
        _libraryItems = [movie];
        _tmdb.Ratings[movie.Id] = new SourceRating("PG", "GB");
        _tvdb.Ratings[movie.Id] = new SourceRating("U", "GB");
        _config.Source = RatingSourceNames.Tvdb;
        _config.TvdbApiKey = "tvdb";

        await CreateUpdater().RunAsync(fullScan: true, new Progress<double>(), CancellationToken.None);

        Assert.Equal("U", movie.OfficialRating);
        Assert.Equal(0, _tmdb.Calls);
    }

    [Fact]
    public async Task UsesSeparateSourcesForMoviesAndSeries()
    {
        var movie = NewMovie("Film");
        var series = new Series { Id = Guid.NewGuid(), Name = "Show" };
        _libraryItems = [movie, series];
        _tmdb.Ratings[movie.Id] = new SourceRating("PG", "GB");
        _tmdb.Ratings[series.Id] = new SourceRating("18", "GB");
        _tvdb.Ratings[movie.Id] = new SourceRating("U", "GB");
        _tvdb.Ratings[series.Id] = new SourceRating("15", "GB");
        _config.TvdbApiKey = "tvdb";
        _config.SeriesSource = RatingSourceNames.Tvdb;

        await CreateUpdater().RunAsync(fullScan: true, new Progress<double>(), CancellationToken.None);

        Assert.Equal("PG", movie.OfficialRating);
        Assert.Equal("15", series.OfficialRating);
    }

    [Fact]
    public async Task EmptySeriesSourceFollowsMovieSource()
    {
        var series = new Series { Id = Guid.NewGuid(), Name = "Show" };
        _libraryItems = [series];
        _tmdb.Ratings[series.Id] = new SourceRating("12", "GB");
        _tvdb.Ratings[series.Id] = new SourceRating("15", "GB");
        _config.TvdbApiKey = "tvdb";

        await CreateUpdater().RunAsync(fullScan: true, new Progress<double>(), CancellationToken.None);

        Assert.Equal("12", series.OfficialRating);
    }

    [Fact]
    public async Task SkipsOnlyTheTypeWhoseSourceHasNoKey()
    {
        var movie = NewMovie("Film");
        var series = new Series { Id = Guid.NewGuid(), Name = "Show" };
        _libraryItems = [movie, series];
        _tmdb.Ratings[movie.Id] = new SourceRating("PG", "GB");
        _config.SeriesSource = RatingSourceNames.Tvdb;

        var summary = await CreateUpdater().RunAsync(fullScan: true, new Progress<double>(), CancellationToken.None);

        Assert.Equal("PG", movie.OfficialRating);
        Assert.Null(series.OfficialRating);
        Assert.Equal(1, summary.Skipped);
        Assert.Equal(0, _tvdb.Calls);
    }

    [Fact]
    public async Task CopiesSeriesRatingToSeasonsAndEpisodes()
    {
        var series = new Series { Id = Guid.NewGuid(), Name = "Show" };
        var season = new Season { Id = Guid.NewGuid(), Name = "Season 1" };
        var episode = new Episode { Id = Guid.NewGuid(), Name = "Pilot", OfficialRating = "15" };
        var lockedEpisode = new Episode { Id = Guid.NewGuid(), Name = "Locked", LockedFields = [MetadataField.OfficialRating] };
        _libraryItems = [series];
        _children[series.Id] = [season, episode, lockedEpisode];
        _tmdb.Ratings[series.Id] = new SourceRating("15", "GB");
        var updater = CreateUpdater();

        await updater.RunAsync(fullScan: true, new Progress<double>(), CancellationToken.None);

        Assert.Equal("15", series.OfficialRating);
        Assert.Equal("15", season.OfficialRating);
        Assert.Null(lockedEpisode.OfficialRating);
        // The episode already matched, so only the series and season are written.
        Assert.Equal([series.Id, season.Id], updater.Saved.Select(i => i.Id));
    }

    private Movie NewLockedMovie(string name, string rating)
    {
        var movie = NewMovie(name, rating);
        movie.LockedFields = [MetadataField.OfficialRating];
        _libraryItems = [.. _libraryItems, movie];
        return movie;
    }

    [Fact]
    public async Task OverwriteModeUpdatesLockedRatingAndKeepsLock()
    {
        var movie = NewLockedMovie("Locked", "R");
        _tmdb.Ratings[movie.Id] = new SourceRating("18", "GB");
        _config.LockedRatings = LockedRatingModes.Overwrite;

        await CreateUpdater().RunAsync(fullScan: true, new Progress<double>(), CancellationToken.None);

        Assert.Equal("18", movie.OfficialRating);
        Assert.Contains(MetadataField.OfficialRating, movie.LockedFields);
    }

    [Fact]
    public async Task UnlockModeUpdatesLockedRatingAndRemovesLock()
    {
        var movie = NewLockedMovie("Locked", "R");
        _tmdb.Ratings[movie.Id] = new SourceRating("18", "GB");
        _config.LockedRatings = LockedRatingModes.Unlock;

        await CreateUpdater().RunAsync(fullScan: true, new Progress<double>(), CancellationToken.None);

        Assert.Equal("18", movie.OfficialRating);
        Assert.DoesNotContain(MetadataField.OfficialRating, movie.LockedFields);
    }

    [Fact]
    public async Task UnlockModeUnlocksEvenWhenRatingAlreadyCorrect()
    {
        var movie = NewLockedMovie("Locked", "18");
        _tmdb.Ratings[movie.Id] = new SourceRating("18", "GB");
        _config.LockedRatings = LockedRatingModes.Unlock;
        var updater = CreateUpdater();

        var summary = await updater.RunAsync(fullScan: true, new Progress<double>(), CancellationToken.None);

        Assert.Equal(1, summary.Updated);
        Assert.DoesNotContain(MetadataField.OfficialRating, movie.LockedFields);
        Assert.Single(updater.Saved);
    }

    [Fact]
    public async Task LockAfterUpdateTakesPriorityOverUnlock()
    {
        var movie = NewLockedMovie("Locked", "R");
        _tmdb.Ratings[movie.Id] = new SourceRating("18", "GB");
        _config.LockedRatings = LockedRatingModes.Unlock;
        _config.LockAfterUpdate = true;

        await CreateUpdater().RunAsync(fullScan: true, new Progress<double>(), CancellationToken.None);

        Assert.Equal("18", movie.OfficialRating);
        Assert.Contains(MetadataField.OfficialRating, movie.LockedFields);
    }

    [Fact]
    public async Task WholeItemLockIsAlwaysRespected()
    {
        var movie = NewMovie("Item lock", "R");
        movie.IsLocked = true;
        _libraryItems = [movie];
        _tmdb.Ratings[movie.Id] = new SourceRating("18", "GB");
        _config.LockedRatings = LockedRatingModes.Unlock;

        var summary = await CreateUpdater().RunAsync(fullScan: true, new Progress<double>(), CancellationToken.None);

        Assert.Equal(1, summary.Skipped);
        Assert.Equal("R", movie.OfficialRating);
    }

    [Fact]
    public async Task UnlockModeAppliesToLockedEpisodes()
    {
        var series = new Series { Id = Guid.NewGuid(), Name = "Show" };
        var lockedEpisode = new Episode { Id = Guid.NewGuid(), Name = "Locked", OfficialRating = "15", LockedFields = [MetadataField.OfficialRating] };
        var lockedWrong = new Episode { Id = Guid.NewGuid(), Name = "Locked wrong", OfficialRating = "TV-MA", LockedFields = [MetadataField.OfficialRating] };
        _libraryItems = [series];
        _children[series.Id] = [lockedEpisode, lockedWrong];
        _tmdb.Ratings[series.Id] = new SourceRating("15", "GB");
        _config.LockedRatings = LockedRatingModes.Unlock;

        await CreateUpdater().RunAsync(fullScan: true, new Progress<double>(), CancellationToken.None);

        Assert.Equal("15", lockedWrong.OfficialRating);
        Assert.Empty(lockedEpisode.LockedFields);
        Assert.Empty(lockedWrong.LockedFields);
    }

    [Fact]
    public async Task LockAfterUpdateAddsLockedField()
    {
        var movie = NewMovie("Film");
        _libraryItems = [movie];
        _tmdb.Ratings[movie.Id] = new SourceRating("PG", "GB");
        _config.LockAfterUpdate = true;

        await CreateUpdater().RunAsync(fullScan: true, new Progress<double>(), CancellationToken.None);

        Assert.Contains(MetadataField.OfficialRating, movie.LockedFields);
    }

    [Fact]
    public async Task FailedItemDoesNotStopRunOrAdvanceMarker()
    {
        var broken = NewMovie("A broken");
        var fine = NewMovie("B fine");
        _libraryItems = [broken, fine];
        _tmdb.Throw.Add(broken.Id);
        _tmdb.Ratings[fine.Id] = new SourceRating("PG", "GB");

        var summary = await CreateUpdater().RunAsync(fullScan: true, new Progress<double>(), CancellationToken.None);

        Assert.Equal(1, summary.Failed);
        Assert.Equal("PG", fine.OfficialRating);
        Assert.Null(_runState.LastRun);
    }

    [Fact]
    public async Task AuthFailureAbortsRun()
    {
        _libraryItems = [NewMovie("Film")];
        _tmdb.AuthFails = true;

        await Assert.ThrowsAsync<RatingSourceAuthException>(
            () => CreateUpdater().RunAsync(fullScan: true, new Progress<double>(), CancellationToken.None));
    }

    [Fact]
    public async Task DoesNothingWithoutKeyOrLibraries()
    {
        _libraryItems = [NewMovie("Film")];
        _config.TmdbApiKey = string.Empty;
        var summary = await CreateUpdater().RunAsync(fullScan: true, new Progress<double>(), CancellationToken.None);
        Assert.Equal(0, summary.Checked);

        _config.TmdbApiKey = "key";
        _config.LibraryIds = [];
        summary = await CreateUpdater().RunAsync(fullScan: true, new Progress<double>(), CancellationToken.None);
        Assert.Equal(0, summary.Checked);
        Assert.Empty(_queries);
    }

    private sealed class TestUpdater : ContentRatingUpdater
    {
        public TestUpdater(ILibraryManager libraryManager, IEnumerable<IRatingSource> sources, RunStateStore runState, Func<PluginConfiguration> config)
            : base(libraryManager, sources, runState, NullLogger<ContentRatingUpdater>.Instance, config)
        {
        }

        public List<BaseItem> Saved { get; } = [];

        internal override Task SaveRatingAsync(BaseItem item, string rating, bool lockField, CancellationToken cancellationToken)
        {
            // Skip the real save, which needs a running server; keep the field changes.
            item.OfficialRating = rating;
            ApplyLock(item, lockField);

            Saved.Add(item);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSource : IRatingSource
    {
        public FakeSource(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public Dictionary<Guid, SourceRating> Ratings { get; } = [];

        public HashSet<Guid> Throw { get; } = [];

        public bool AuthFails { get; set; }

        public int Calls { get; private set; }

        public bool IsConfigured(PluginConfiguration config)
            => !string.IsNullOrEmpty(Name == RatingSourceNames.Tmdb ? config.TmdbApiKey : config.TvdbApiKey);

        public Task<SourceRating?> GetRatingAsync(BaseItem item, IReadOnlyList<string> countryCodes, PluginConfiguration config, CancellationToken cancellationToken)
        {
            Calls++;
            if (AuthFails)
            {
                throw new RatingSourceAuthException("bad key");
            }

            if (Throw.Contains(item.Id))
            {
                throw new System.Net.Http.HttpRequestException("boom");
            }

            return Task.FromResult(Ratings.TryGetValue(item.Id, out var r) ? r : null);
        }
    }

    private sealed class InMemoryRunState : RunStateStore
    {
        public InMemoryRunState()
            : base(NullLogger<RunStateStore>.Instance)
        {
        }

        public DateTime? LastRun { get; set; }

        public override DateTime? GetLastRunUtc() => LastRun;

        public override void SetLastRunUtc(DateTime startedUtc) => LastRun = startedUtc;
    }
}
