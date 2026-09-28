# Auto Content Ratings for Jellyfin

A Jellyfin 12 plugin that automatically fills in the **Parental Rating** field (U, PG, 12A, 15, 18...) for movies and TV series, using TMDB or TVDB.

## Features

- **Choose your data source:** TMDB or TVDB, set separately for movies and TV series (e.g. TMDB for movies, TVDB for TV), with an optional fallback to the other if the first has no rating.
- **Choose the rating country:** e.g. United Kingdom for BBFC ratings, with an optional fallback country (e.g. US).
- **Choose your libraries:** only ticked movie and TV libraries are touched.
- **Choose how often:** check for new media every hour, 6 hours, 12 hours, day or week, or manually only. It can also check automatically after every library scan.
- **Whole library re-check:** on demand, or every week, 2 weeks or 30 days.
- **Run now** buttons on the settings page.
- Locked ratings: leave them alone (default), update them and keep the lock, or update them and unlock them. Items locked as a whole are always left alone. It can also lock ratings once set.
- Copies a series' rating to its seasons and episodes (the same as Jellyfin's own metadata editor does), so parental controls work on episodes.
- Updates the value Jellyfin's parental controls actually filter on, not just the text shown.

## Installing

### From the plugin catalogue (recommended)

1. In Jellyfin, go to **Dashboard > Plugins**, open the **Repositories** tab (called **Catalog settings** in some versions) and add a repository:
   - **Name:** `Auto Content Ratings`
   - **URL:** `https://raw.githubusercontent.com/scottnicholls36/jellyfin-auto-content-ratings/main/manifest.json`
2. Go to the **Catalog**, find **Auto Content Ratings** under Metadata, and click **Install**.
3. Restart Jellyfin.
4. Go to **Dashboard > Plugins > Auto Content Ratings**.

New versions then show up as updates in Jellyfin automatically.

### Manually

1. Download the latest `auto-content-ratings_x.x.x.x.zip` from the [Releases](../../releases) page.
2. Unzip it into a new folder inside Jellyfin's plugins folder, e.g. `plugins/AutoContentRatings_1.0.0.0/`:
   - Docker: `/config/plugins/` (or `/config/data/plugins/`)
   - Linux: `/var/lib/jellyfin/plugins/`
   - Windows: `%ProgramData%\Jellyfin\Server\plugins\`
3. Restart Jellyfin.

## Setting up

1. **Get an API key**
   - TMDB (free): create an account, then go to [Settings > API](https://www.themoviedb.org/settings/api). Either the "API Key" or the "API Read Access Token" works.
   - TVDB: create a v4 project key at [thetvdb.com](https://thetvdb.com/dashboard/account/apikey). If it's a user-supported key, you also need your subscriber PIN.
2. Pick a source for movies and one for TV series, paste a key for each source you use, choose the country and tick your libraries.
3. Pick the schedules and click **Save**.
4. Click **Check whole library now** for the first run. Progress shows under **Dashboard > Scheduled Tasks**, and details under **Dashboard > Logs**.

## How it works

- It looks at movies and series (not individual episodes) in the chosen libraries. Items need a TMDB, TVDB or IMDb id, which Jellyfin's normal metadata fetch provides. If an item lacks the chosen source's own id, the plugin looks it up from its other ids.
- **New media** means movies and series added or changed since the last successful run, plus any series that gained new episodes. "Changed" matters because Jellyfin dates new items by the file's creation date, which can be years old for copied or downloaded files; it also catches ratings a metadata refresh has overwritten. The very first run covers everything. With "retry items that still have no rating" on, any unrated items are also tried again each time.
- If any item fails (e.g. a network error), the next new-media run tries the same period again.
- A rejected API key stops the run straight away and shows the error against the task.
- Changing the schedules on the settings page updates the two tasks under **Scheduled Tasks**. If you edit them there, that is fine too, but the settings page won't reflect it.

### A note on Jellyfin's own metadata refresh

Jellyfin's built-in TMDB provider also sets ratings, based on each library's metadata country. A later metadata refresh ("Replace all metadata") may overwrite the plugin's value. To stop that, either tick **Lock the rating after updating it**, or set the library's country (Library > Manage library > Metadata country) to match the plugin.

## Building from source

Requires the .NET 10 SDK.

```sh
dotnet test Jellyfin.Plugin.ContentRatings.slnx
scripts/package.sh 1.0.0.0   # builds and zips the plugin into artifacts/
```

## Releasing a new version

1. On GitHub, go to **Actions > Release > Run workflow**.
2. Type the version (e.g. `1.1.0`) and, optionally, what changed.

The workflow runs the tests, builds the zip, publishes a GitHub release, and adds the release to `manifest.json` on `main`. Jellyfin servers using the repository then see the update. Pushing a tag such as `v1.1.0` does the same.

## Project layout

| Path | What it does |
| --- | --- |
| `Plugin.cs` | Entry point; applies schedule changes when settings are saved |
| `Configuration/` | Settings model and the settings page |
| `Sources/` | TMDB and TVDB clients and the logic that picks the right certification |
| `Services/ContentRatingUpdater.cs` | Finds items in the chosen libraries and writes ratings |
| `Tasks/` | The two scheduled tasks and the after-library-scan hook |
| `manifest.json` | The plugin repository file Jellyfin reads; updated by the Release workflow |
| `scripts/` | Packaging and manifest helpers used by the workflows |
