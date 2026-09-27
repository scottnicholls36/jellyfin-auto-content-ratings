namespace Jellyfin.Plugin.ContentRatings.Configuration;

/// <summary>
/// Values accepted by <see cref="PluginConfiguration.LockedRatings"/>: what to do when an item's
/// rating field is locked.
/// </summary>
public static class LockedRatingModes
{
    /// <summary>Leave locked ratings alone.</summary>
    public const string Skip = "Skip";

    /// <summary>Update locked ratings and keep them locked.</summary>
    public const string Overwrite = "Overwrite";

    /// <summary>Update locked ratings and remove the lock.</summary>
    public const string Unlock = "Unlock";
}
