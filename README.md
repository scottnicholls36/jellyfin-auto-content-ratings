# Auto Content Ratings for Jellyfin

A Jellyfin 12 plugin that automatically fills in the **Parental Rating** field (U, PG, 12A, 15, 18...) for movies and TV series, using TMDB or TVDB.

## Features

- **Choose your data source:** TMDB or TVDB, with an optional fallback to the other if the first has no rating.
- **Choose the rating country:** e.g. United Kingdom for BBFC ratings, with an optional fallback country (e.g. US).
- **Choose your libraries:** only ticked movie and TV libraries are touched.
- **Choose how often:** check for new media every hour, 6 hours, 12 hours, day or week, or manually only. It can also check automatically after every library scan.
- **Whole library re-check:** on demand, or every week, 2 weeks or 30 days.
- **Run now** buttons on the settings page.
- Respects locked items and locked rating fields, and can optionally lock the rating once set.
- Copies a series' rating to its seasons and episodes (the same as Jellyfin's own metadata editor does), so parental controls work on episodes.
- Updates the value Jellyfin's parental controls actually filter on, not just the text shown.

## Installing

1. Download the latest `auto-content-ratings_x.x.x.x.zip` from the [Releases](../../releases) page, or from the artifacts of the latest run under the **Actions** tab.
2. Unzip it into a new folder inside Jellyfin's plugins folder, e.g. `plugins/AutoContentRatings_1.0.0.0/`:
   - Docker: `/config/plugins/` (or `/config/data/plugins/`)
   - Linux: `/var/lib/jellyfin/plugins/`
   - Windows: `%ProgramData%\Jellyfin\Server\plugins\`
3. Restart Jellyfin.
4. Go to **Dashboard > Plugins > Auto Content Ratings**.

## Setting up

1. **Get an API key**
   - TMDB (free): create an account, then go to [Settings > API](https://www.themoviedb.org/settings/api). Either the "API Key" or the "API Read Access Token" works.
   - TVDB: create a v4 project key at [thetvdb.com](https://thetvdb.com/dashboard/account/apikey). If it's a user-supported key, you also need your subscriber PIN.
2. Pick the source, paste the key, choose the country and tick your libraries.
3. Pick the schedules and click **Save**.
4. Click **Check whole library now** for the first run. Progress shows under **Dashboard > Scheduled Tasks**, and details under **Dashboard > Logs**.

## How it works

- It looks at movies and series (not individual episodes) in the chosen libraries. Items need a TMDB, TVDB or IMDb id, which Jellyfin's normal metadata fetch provides. If an item lacks the chosen source's own id, the plugin looks it up from its other ids.
- **New media** means items added since the last successful run. The very first run covers everything. With "retry items that still have no rating" on, any unrated items are also tried again each time.
- If any item fails (e.g. a network error), the next new-media run tries the same period again.
- A rejected API key stops the run straight away and shows the error against the task.
- Changing the schedules on the settings page updates the two tasks under **Scheduled Tasks**. If you edit them there, that is fine too, but the settings page won't reflect it.

### A note on Jellyfin's own metadata refresh

Jellyfin's built-in TMDB provider also sets ratings, based on each library's metadata country. A later metadata refresh ("Replace all metadata") may overwrite the plugin's value. To stop that, either tick **Lock the rating after updating it**, or set the library's country (Library > Manage library > Metadata country) to match the plugin.

## Building from source

Requires the .NET 10 SDK.

```sh
dotnet test Jellyfin.Plugin.ContentRatings.slnx
dotnet build Jellyfin.Plugin.ContentRatings/Jellyfin.Plugin.ContentRatings.csproj -c Release
```

The plugin is `Jellyfin.Plugin.ContentRatings/bin/Release/net10.0/Jellyfin.Plugin.ContentRatings.dll`. Pushing a tag like `v1.0.0` builds a GitHub release with the zip attached.

## Project layout

| Path | What it does |
| --- | --- |
| `Plugin.cs` | Entry point; applies schedule changes when settings are saved |
| `Configuration/` | Settings model and the settings page |
| `Sources/` | TMDB and TVDB clients and the logic that picks the right certification |
| `Services/ContentRatingUpdater.cs` | Finds items in the chosen libraries and writes ratings |
| `Tasks/` | The two scheduled tasks and the after-library-scan hook |
