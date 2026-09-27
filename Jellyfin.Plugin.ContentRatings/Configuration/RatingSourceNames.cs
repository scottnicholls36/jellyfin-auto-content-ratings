namespace Jellyfin.Plugin.ContentRatings.Configuration;

/// <summary>
/// Values accepted by <see cref="PluginConfiguration.Source"/>.
/// </summary>
public static class RatingSourceNames
{
    /// <summary>The Movie Database.</summary>
    public const string Tmdb = "Tmdb";

    /// <summary>TheTVDB.</summary>
    public const string Tvdb = "Tvdb";
}
